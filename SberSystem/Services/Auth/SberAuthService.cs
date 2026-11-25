using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace SberSystem.Services.Auth
{
    public sealed class SberAuthService
    {
        public static bool IsDebugMode { get; set; }

        private const string AccessTokenKey = "sber_access_token";
        private const string RefreshTokenKey = "sber_refresh_token";
        private const string ExpiresAtKey = "sber_token_expires_at";

        private readonly CertificateService _certService;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        public SberAuthService(CertificateService certService)
        {
            _certService = certService;
        }

        public async Task<bool> HasValidSessionAsync()
        {
            if (!_certService.HasCertificate)
                return false;

            var token = await GetValidAccessTokenAsync();
            return !string.IsNullOrEmpty(token);
        }

        public async Task<(bool Success, string? Error)> LoginAsync()
        {
            if (!_certService.HasCertificate)
                return (false, "Сначала загрузите клиентский сертификат (.p12 / .pfx).");

            var codeVerifier = GenerateRandomBase64Url(32);
            var codeChallenge = GenerateCodeChallenge(codeVerifier);
            var state = GenerateRandomBase64Url(16);

            var authUrl = new Uri(
                $"{SberAuthConfig.AuthorizeEndpoint}" +
                $"?response_type=code" +
                $"&client_id={Uri.EscapeDataString(SberAuthConfig.ClientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(SberAuthConfig.RedirectUri)}" +
                $"&scope={Uri.EscapeDataString(SberAuthConfig.Scopes)}" +
                $"&state={Uri.EscapeDataString(state)}" +
                $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                $"&code_challenge_method=S256");

            WebAuthenticatorResult result;
            try
            {
                result = await WebAuthenticator.Default.AuthenticateAsync(
                    authUrl,
                    new Uri(SberAuthConfig.RedirectUri));
            }
            catch (TaskCanceledException)
            {
                return (false, "Авторизация отменена пользователем.");
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка открытия браузера: {ex.Message}");
            }

            if (!result.Properties.TryGetValue("state", out var returnedState) || returnedState != state)
                return (false, "Ошибка безопасности: несовпадение параметра state.");

            if (!result.Properties.TryGetValue("code", out var authCode) || string.IsNullOrEmpty(authCode))
                return (false, "СберБизнес не вернул код авторизации.");

            return await ExchangeCodeForTokensAsync(authCode, codeVerifier);
        }

        public async Task<string?> GetValidAccessTokenAsync()
        {
            var accessToken = await SecureStorage.Default.GetAsync(AccessTokenKey);
            var expiresAtRaw = await SecureStorage.Default.GetAsync(ExpiresAtKey);

            if (!string.IsNullOrEmpty(accessToken) &&
                DateTimeOffset.TryParse(expiresAtRaw, out var expiresAt) &&
                expiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return accessToken;
            }

            await _refreshLock.WaitAsync();
            try
            {
                accessToken = await SecureStorage.Default.GetAsync(AccessTokenKey);
                expiresAtRaw = await SecureStorage.Default.GetAsync(ExpiresAtKey);

                if (!string.IsNullOrEmpty(accessToken) &&
                    DateTimeOffset.TryParse(expiresAtRaw, out expiresAt) &&
                    expiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
                {
                    return accessToken;
                }

                var refreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);
                if (string.IsNullOrEmpty(refreshToken))
                    return null;

                var refreshed = await RefreshTokensAsync(refreshToken);
                return refreshed ? await SecureStorage.Default.GetAsync(AccessTokenKey) : null;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        public async Task<HttpClient> CreateMtlsHttpClientAsync()
        {
            var cert = await _certService.LoadCertificateAsync()
                ?? throw new InvalidOperationException("Клиентский сертификат не найден.");

            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(cert);
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;

            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public void Logout()
        {
            IsDebugMode = false;
            SecureStorage.Default.Remove(AccessTokenKey);
            SecureStorage.Default.Remove(RefreshTokenKey);
            SecureStorage.Default.Remove(ExpiresAtKey);
        }

        private async Task<(bool Success, string? Error)> ExchangeCodeForTokensAsync(string code, string codeVerifier)
        {
            try
            {
                using var client = await CreateMtlsHttpClientAsync();
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = SberAuthConfig.ClientId,
                    ["redirect_uri"] = SberAuthConfig.RedirectUri,
                    ["code"] = code,
                    ["code_verifier"] = codeVerifier
                });

                var response = await client.PostAsync(SberAuthConfig.TokenEndpoint, content);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    return (false, $"Сбер отклонил обмен токена ({(int)response.StatusCode}): {body}");
                }

                var tokenData = await response.Content.ReadFromJsonAsync<SberTokenResponse>();
                if (tokenData is null || string.IsNullOrEmpty(tokenData.AccessToken))
                    return (false, "Пустой ответ от сервера токенов.");

                await SaveTokensAsync(tokenData);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"Сетевая ошибка mTLS: {ex.Message}");
            }
        }

        private async Task<bool> RefreshTokensAsync(string refreshToken)
        {
            try
            {
                using var client = await CreateMtlsHttpClientAsync();
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = SberAuthConfig.ClientId,
                    ["refresh_token"] = refreshToken
                });

                var response = await client.PostAsync(SberAuthConfig.TokenEndpoint, content);
                if (!response.IsSuccessStatusCode)
                {
                    Logout();
                    return false;
                }

                var tokenData = await response.Content.ReadFromJsonAsync<SberTokenResponse>();
                if (tokenData is null || string.IsNullOrEmpty(tokenData.AccessToken))
                    return false;

                await SaveTokensAsync(tokenData);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task SaveTokensAsync(SberTokenResponse token)
        {
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            await SecureStorage.Default.SetAsync(AccessTokenKey, token.AccessToken);
            await SecureStorage.Default.SetAsync(ExpiresAtKey, expiresAt.ToString("O"));

            if (!string.IsNullOrEmpty(token.RefreshToken))
                await SecureStorage.Default.SetAsync(RefreshTokenKey, token.RefreshToken);
        }

        private static string GenerateRandomBase64Url(int byteLength)
        {
            var bytes = RandomNumberGenerator.GetBytes(byteLength);
            return Base64UrlEncode(bytes);
        }

        private static string GenerateCodeChallenge(string codeVerifier)
        {
            var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
            return Base64UrlEncode(hash);
        }

        private static string Base64UrlEncode(byte[] bytes) =>
            Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        private sealed class SberTokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = string.Empty;

            [JsonPropertyName("refresh_token")]
            public string? RefreshToken { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }
    }
}
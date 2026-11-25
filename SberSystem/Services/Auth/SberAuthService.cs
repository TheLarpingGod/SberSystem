using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
            if (!SberAuthConfig.IsConfigured)
                return (false, "Приложение ещё не подключено к Sber API. Укажите ClientId и ClientSecret в SberAuthConfig.");

            if (OperatingSystem.IsWindows())
                return (false, "Вход через СберБизнес ID доступен только в Android-версии приложения.");

            if (!_certService.HasCertificate)
                return (false, "Сначала загрузите клиентский сертификат (.p12 / .pfx).");

            var codeVerifier = SberAuthConfig.UsePkce ? GenerateRandomHex(32) : null;
            var state = GenerateRandomHex(32);

            WebAuthenticatorResult result;
            try
            {
                result = await WebAuthenticator.Default.AuthenticateAsync(
                    BuildAuthorizeUrl(state, codeVerifier),
                    new Uri(SberAuthConfig.CallbackUri));
            }
            catch (TaskCanceledException)
            {
                return (false, "Авторизация отменена пользователем.");
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка открытия браузера: {ex.Message}");
            }

            return await FinishLoginAsync(result, state, codeVerifier);
        }

        private async Task<(bool Success, string? Error)> FinishLoginAsync(
            WebAuthenticatorResult result, string state, string? codeVerifier)
        {
            // При ошибке Сбер возвращает error и error_description, а state, по спецификации, не гарантирует.
            // Причину отказа показываем без проверки state: на этом пути ничего не обменивается и не сохраняется.
            if (result.Properties.TryGetValue("error", out var error))
            {
                var reason = result.Properties.TryGetValue("error_description", out var errorDescription)
                    ? errorDescription
                    : error;
                return (false, $"СберБизнес ID отклонил вход: {reason}");
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

        private async Task<(bool Success, string? Error)> ExchangeCodeForTokensAsync(string code, string? codeVerifier)
        {
            try
            {
                using var client = await CreateMtlsHttpClientAsync();
                using var content = new FormUrlEncodedContent(BuildCodeTokenRequest(code, codeVerifier));

                var response = await client.PostAsync(SberAuthConfig.TokenEndpoint, content);
                if (!response.IsSuccessStatusCode)
                {
                    var reason = await ReadBankErrorAsync(response);
                    return (false, $"Сбер отклонил обмен токена ({(int)response.StatusCode}): {reason}");
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

        // Параметры по спецификации: https://developers.sber.ru/docs/ru/sber-api/specifications/oauth/oauth-token-post
        private static Dictionary<string, string> BuildCodeTokenRequest(string code, string? codeVerifier)
        {
            var formData = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = SberAuthConfig.ClientId,
                ["client_secret"] = SberAuthConfig.ClientSecret,
                ["redirect_uri"] = SberAuthConfig.RedirectUri,
                ["code"] = code
            };

            // code_verifier обязателен, только если в запросе авторизации был code_challenge
            if (codeVerifier is not null)
                formData["code_verifier"] = codeVerifier;

            return formData;
        }

        private static Dictionary<string, string> BuildRefreshTokenRequest(string refreshToken) => new()
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = SberAuthConfig.ClientId,
            ["client_secret"] = SberAuthConfig.ClientSecret,
            ["refresh_token"] = refreshToken
        };

        private async Task<bool> RefreshTokensAsync(string refreshToken)
        {
            try
            {
                using var client = await CreateMtlsHttpClientAsync();
                using var content = new FormUrlEncodedContent(BuildRefreshTokenRequest(refreshToken));

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

        // 64 символа [0-9A-F]: проходят проверки Сбера для state (^[a-zA-Z0-9]{36,}$)
        // и code_verifier (^[a-zA-Z0-9]+$), в отличие от base64url с символами '-' и '_'.
        private static string GenerateRandomHex(int byteLength) =>
            Convert.ToHexString(RandomNumberGenerator.GetBytes(byteLength));

        // Параметры по спецификации: https://developers.sber.ru/docs/ru/sber-api/specifications/oauth/oauth-authorize-get
        private static Uri BuildAuthorizeUrl(string state, string? codeVerifier)
        {
            var query =
                $"?response_type=code" +
                $"&client_id={Uri.EscapeDataString(SberAuthConfig.ClientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(SberAuthConfig.RedirectUri)}" +
                $"&scope={Uri.EscapeDataString(SberAuthConfig.Scopes)}" +
                $"&state={state}";

            if (codeVerifier is not null)
            {
                query +=
                    $"&code_challenge={GenerateCodeChallenge(codeVerifier)}" +
                    $"&code_challenge_method=S256";
            }

            return new Uri(SberAuthConfig.AuthorizeEndpoint + query);
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

        // Банк описывает ошибку в JSON: {"error": "...", "error_description": "..."}
        private static async Task<string> ReadBankErrorAsync(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync() ?? string.Empty;

            try
            {
                var error = JsonSerializer.Deserialize<SberErrorResponse>(body);
                if (!string.IsNullOrEmpty(error?.ErrorDescription))
                    return $"{error.ErrorDescription} ({error.Error})";
                if (!string.IsNullOrEmpty(error?.Error))
                    return error.Error;
            }
            catch (JsonException)
            {
                // Тело ответа не в формате JSON — показываем его как есть.
            }

            return body.Length > 300 ? body[..300] + "…" : body;
        }

        private sealed class SberTokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = string.Empty;

            [JsonPropertyName("refresh_token")]
            public string? RefreshToken { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }

        private sealed class SberErrorResponse
        {
            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("error_description")]
            public string? ErrorDescription { get; set; }
        }
    }
}
namespace SberSystem.Services.Auth
{
    public static class SberAuthConfig
    {
        public const string ClientId = "YOUR_SBER_CLIENT_ID";

        public const string RedirectUri = "sbersystem://auth/callback";

        public const string AuthorizeEndpoint = "https://sbi.sberbank.ru:9443/ic/sso/api/v2/oauth/authorize";
        public const string TokenEndpoint = "https://fintech.sberbank.ru:9443/api/v2/oauth/token";

        public const string Scopes = "openid GET_STATEMENT_TRANSACTIONS";
    }
}
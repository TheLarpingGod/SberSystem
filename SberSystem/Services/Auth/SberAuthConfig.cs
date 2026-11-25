namespace SberSystem.Services.Auth
{
    public static class SberAuthConfig
    {
        // Значения из личного кабинета Sber API (СберБизнес).
        public const string ClientId = "YOUR_SBER_CLIENT_ID";
        public const string ClientSecret = "YOUR_SBER_CLIENT_SECRET";

        /// <summary>
        /// Адрес, который регистрируется в личном кабинете. По спецификации Сбера это https-адрес на домене,
        /// поэтому браузер возвращается на страницу docs/auth/callback, а она передаёт код в приложение.
        /// </summary>
        public const string RedirectUri = "https://thelarpinggod.github.io/SberSystem/auth/callback/";

        /// <summary>
        /// Адрес, по которому приложение перехватывает возврат из браузера.
        /// Должен совпадать с адресом в docs/auth/callback/index.html.
        /// </summary>
        public const string CallbackScheme = "sbersystem";
        public const string CallbackUri = CallbackScheme + "://auth/callback";

        public const string AuthorizeEndpoint = "https://sbi.sberbank.ru:9443/ic/sso/api/v2/oauth/authorize";
        public const string TokenEndpoint = "https://fintech.sberbank.ru:9443/ic/sso/api/v2/oauth/token";

        // openid обязателен. Полный список операций: https://developers.sber.ru/docs/ru/sber-api/start/oauth
        public const string Scopes = "openid GET_STATEMENT_ACCOUNT";

        /// <summary>
        /// PKCE рекомендуется спецификацией, но включается менеджером Сбера при регистрации платформы.
        /// </summary>
        public const bool UsePkce = true;

        public static bool IsConfigured =>
            !ClientId.StartsWith("YOUR_", StringComparison.Ordinal)
            && !ClientSecret.StartsWith("YOUR_", StringComparison.Ordinal);
    }
}
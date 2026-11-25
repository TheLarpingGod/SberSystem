using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;
using SberSystem.Services.Auth;

namespace SberSystem
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseLocalNotification()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            builder.Services.AddSingleton<CertificateService>();
            builder.Services.AddSingleton<SberAuthService>();
            builder.Services.AddTransient<MainPage>();

            return builder.Build();
        }
    }
}

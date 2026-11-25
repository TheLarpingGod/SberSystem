using Android.App;
using Android.Content;
using Android.Content.PM;
using SberSystem.Services.Auth;

namespace SberSystem.Platforms.Android
{
    [Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
    [IntentFilter(
    [Intent.ActionView],
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = SberAuthConfig.CallbackScheme)]
    public class WebAuthenticatorCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity;
}
using SberSystem.Services.Auth;

namespace SberSystem
{
    public partial class MainPage : ContentPage
    {
        private readonly CertificateService _certService;
        private readonly SberAuthService _authService;
        private bool _sessionChecked;

        public MainPage()
        {
            InitializeComponent();
            _certService = new CertificateService();
            _authService = new SberAuthService(_certService);
            ConfigureDebugUi();
        }

        public MainPage(CertificateService certService, SberAuthService authService)
        {
            InitializeComponent();
            _certService = certService;
            _authService = authService;
            ConfigureDebugUi();
        }

        private void ConfigureDebugUi()
        {
#if DEBUG
            DebugButton.IsVisible = true;
#endif
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            UpdateCertUi();

            if (_sessionChecked)
                return;

            _sessionChecked = true;

            try
            {
                if (await _authService.HasValidSessionAsync() && Shell.Current is not null)
                {
                    await Shell.Current.GoToAsync("//Menu");
                }
            }
            catch
            {
                // Остаёмся на экране входа
            }
        }

        private void UpdateCertUi()
        {
            if (_certService.HasCertificate)
            {
                CertStatusLabel.Text = "Сертификат загружен и готов к работе";
                CertStatusLabel.TextColor = Color.FromArgb("#21A038");
                LoginButton.IsEnabled = true;
            }
            else
            {
                CertStatusLabel.Text = "Сертификат не загружен";
                CertStatusLabel.TextColor = Color.FromArgb("#B42318");
                LoginButton.IsEnabled = false;
            }
        }

        private async void OnImportCertClicked(object? sender, EventArgs e)
        {
            var password = CertPasswordEntry.Text;
            if (string.IsNullOrWhiteSpace(password))
            {
                await DisplayAlert("Пароль не указан", "Введите пароль от контейнера сертификата перед выбором файла.", "ОК");
                return;
            }

            try
            {
                var fileResult = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Выберите сертификат СберБизнес (.p12 / .pfx)"
                });

                if (fileResult is null)
                    return;

                using var stream = await fileResult.OpenReadAsync();
                var (success, message) = await _certService.ImportCertificateAsync(stream, password);

                CertPasswordEntry.Text = string.Empty;
                UpdateCertUi();

                await DisplayAlert(success ? "Готово" : "Ошибка", message, "ОК");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ошибка выбора файла", ex.Message, "ОК");
            }
        }

        private async void OnLoginClicked(object? sender, EventArgs e)
        {
            SetBusy(true);
            try
            {
                SberAuthService.IsDebugMode = false;
                var (success, error) = await _authService.LoginAsync();
                if (success && Shell.Current is not null)
                {
                    await Shell.Current.GoToAsync("//Menu");
                }
                else if (!success)
                {
                    await DisplayAlert("Ошибка входа", error ?? "Неизвестная ошибка", "ОК");
                }
            }
            finally
            {
                SetBusy(false);
            }
        }

#if DEBUG
        private async void OnDebugClicked(object? sender, EventArgs e)
        {
            SberAuthService.IsDebugMode = true;
            if (Shell.Current is not null)
            {
                await Shell.Current.GoToAsync("//Menu");
            }
        }
#endif

        private void SetBusy(bool isBusy)
        {
            LoadingIndicator.IsVisible = isBusy;
            LoadingIndicator.IsRunning = isBusy;
            LoginButton.IsEnabled = !isBusy && _certService.HasCertificate;
            ImportCertButton.IsEnabled = !isBusy;
#if DEBUG
            DebugButton.IsEnabled = !isBusy;
#endif
        }
    }
}
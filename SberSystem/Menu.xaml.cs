using System.Collections.ObjectModel;
using SberSystem.Services.Auth;

namespace SberSystem
{
    public partial class Menu : ContentPage
    {
        private readonly SberAuthService _authService;
        private readonly ChargeAnomalyTracker _tracker;

        public ObservableCollection<SupplierItem> Suppliers { get; } = new();

        public Menu()
        {
            InitializeComponent();
            var certService = new CertificateService();
            _authService = new SberAuthService(certService);
            _tracker = new ChargeAnomalyTracker(Suppliers);
            SuppliersCollection.ItemsSource = Suppliers;
            InitDebugDataIfNeeded();
        }

        public Menu(SberAuthService authService)
        {
            InitializeComponent();
            _authService = authService;
            _tracker = new ChargeAnomalyTracker(Suppliers);
            SuppliersCollection.ItemsSource = Suppliers;
            InitDebugDataIfNeeded();
        }

        private void InitDebugDataIfNeeded()
        {
            if (!SberAuthService.IsDebugMode || Suppliers.Count > 0)
                return;

            Suppliers.Add(new SupplierItem
            {
                Inn = "7701234567",
                Name = "ООО ОфисСнаб",
                AverageAmount = 15000m,
                LastChargeAmount = 14800m,
                ChargeHistory = [14500m, 15000m, 15200m]
            });

            Suppliers.Add(new SupplierItem
            {
                Inn = "6670987654",
                Name = "ИП Логистика-Урал",
                AverageAmount = 42000m,
                LastChargeAmount = 42000m,
                ChargeHistory = [41000m, 42000m, 43000m]
            });
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            var isDebug = SberAuthService.IsDebugMode;
            var modeSuffix = isDebug ? " [DEBUG]" : string.Empty;

            ThresholdLabel.Text = $"Порог аномалии: +{ChargeAnomalyTracker.AnomalyThreshold * 100:F0}%{modeSuffix}";
            //SimulateAnomalyButton.IsVisible = isDebug;
        }

        private async void OnSimulateAnomalyClicked(object? sender, EventArgs e)
        {
            //SimulateAnomalyButton.IsEnabled = false;
            try
            {
                await _tracker.SimulateRandomAnomalyAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ошибка симуляции", ex.Message, "ОК");
            }
            finally
            {
                //SimulateAnomalyButton.IsEnabled = true;
            }
        }

        private async void OnSyncClicked(object? sender, EventArgs e)
        {
            SyncButton.IsEnabled = false;
            try
            {
                if (SberAuthService.IsDebugMode)
                {
                    await _tracker.SimulateRandomAnomalyAsync();
                    return;
                }

                var token = await _authService.GetValidAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    await DisplayAlert("Сессия истекла", "Необходимо повторно войти через СберБизнес ID.", "ОК");
                    if (Shell.Current is not null)
                        await Shell.Current.GoToAsync("//MainPage");
                    return;
                }

                await DisplayAlert("Синхронизация", "Токен активен. Сервис загрузки выписок готов к подключению.", "ОК");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ошибка синхронизации", ex.Message, "ОК");
            }
            finally
            {
                SyncButton.IsEnabled = true;
            }
        }

        private async void OnFindAlternativesClicked(object? sender, EventArgs e)
        {
            if (sender is Button { CommandParameter: SupplierItem supplier })
            {
                await DisplayAlert(
                    "Поиск альтернатив",
                    $"Подбор альтернатив для {supplier.Name} (ИНН {supplier.Inn}) через GigaChat.",
                    "ОК");
            }
        }

        private async void OnLogoutClicked(object? sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Выход", "Выйти из аккаунта СберБизнес?", "Выйти", "Отмена");
            if (!confirm)
                return;

            _authService.Logout();

            if (Shell.Current is not null)
                await Shell.Current.GoToAsync("//MainPage");
        }
    }
}
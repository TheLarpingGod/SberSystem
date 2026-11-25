using System.Collections.ObjectModel;
using Plugin.LocalNotification;

namespace SberSystem
{
    public class ChargeAnomalyTracker
    {
        private const string ThresholdPrefKey = "anomaly_threshold";
        private const string ProcessedOpsPrefKey = "processed_operation_ids";

        private readonly HashSet<string> _processedOperationIds = [];

        public ObservableCollection<SupplierItem> Suppliers { get; }

        /// <summary>
        /// Допустимое отклонение от нормы (20%)
        /// </summary>
        public static double AnomalyThreshold
        {
            get => Preferences.Default.Get(ThresholdPrefKey, 0.20);
            set => Preferences.Default.Set(ThresholdPrefKey, value);
        }

        public ChargeAnomalyTracker(ObservableCollection<SupplierItem> suppliers)
        {
            Suppliers = suppliers;
            LoadProcessedOperations();
        }

        public async Task ReceiveRealChargeAsync(
            string inn,
            string supplierName,
            decimal amount,
            string? operationId = null)
        {
            if (amount <= 0) return;

            if (!string.IsNullOrEmpty(operationId))
            {
                if (_processedOperationIds.Contains(operationId))
                    return;

                _processedOperationIds.Add(operationId);
                SaveProcessedOperations();
            }

            var supplier = Suppliers.FirstOrDefault(s => s.Inn == inn);
            if (supplier == null)
            {
                supplier = new SupplierItem
                {
                    Inn = inn,
                    Name = supplierName,
                    AverageAmount = amount
                };
                MainThread.BeginInvokeOnMainThread(() => Suppliers.Add(supplier));
            }
            else
            {
                if (supplier.ChargeHistory.Count > 0)
                {
                    supplier.AverageAmount = CalculateMedian(supplier.ChargeHistory);
                }
            }

            await ProcessChargeVisualAsync(supplier, amount);

            supplier.ChargeHistory.Add(amount);
        }

        public static async Task ProcessChargeVisualAsync(SupplierItem supplier, decimal chargeAmount)
        {
            decimal limit = supplier.AverageAmount * (decimal)(1.0 + AnomalyThreshold);
            bool isAnomaly = supplier.AverageAmount > 0 && chargeAmount > limit;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                supplier.LastChargeAmount = chargeAmount;
                supplier.HasAlert = isAnomaly;
            });

            if (isAnomaly)
            {
                decimal diffPercent = Math.Round((chargeAmount - supplier.AverageAmount) / supplier.AverageAmount * 100, 1);
                string title = $"Аномальное списание: {supplier.Name}";
                string body = $"Списано {chargeAmount:N0} ₽ при норме {supplier.AverageAmount:N0} ₽ (+{diffPercent}%).";

                await SendPushNotificationAsync(title, body);
            }
        }

        public async Task SimulateChargeAsync(string inn, decimal fakeAmount)
        {
            var supplier = Suppliers.FirstOrDefault(s => s.Inn == inn);
            if (supplier == null) return;

            await ProcessChargeVisualAsync(supplier, fakeAmount);
        }

        public async Task SimulateAnomalySpikeAsync(string inn, double multiplier = 1.5)
        {
            var supplier = Suppliers.FirstOrDefault(s => s.Inn == inn);
            if (supplier == null) return;

            decimal fakeAmount = Math.Round(supplier.AverageAmount * (decimal)multiplier, 2);
            await ProcessChargeVisualAsync(supplier, fakeAmount);
        }

        public async Task SimulateNormalChargeAsync(string inn)
        {
            var supplier = Suppliers.FirstOrDefault(s => s.Inn == inn);
            if (supplier == null) return;

            double factor = 0.95 + Random.Shared.NextDouble() * 0.10;
            decimal fakeAmount = Math.Round(supplier.AverageAmount * (decimal)factor, 2);

            await ProcessChargeVisualAsync(supplier, fakeAmount);
        }

        public async Task SimulateRandomAnomalyAsync()
        {
            if (Suppliers.Count == 0) return;

            var supplier = Suppliers[Random.Shared.Next(Suppliers.Count)];
            double multiplier = 1.35 + Random.Shared.NextDouble() * 0.85;
            decimal fakeAmount = Math.Round(supplier.AverageAmount * (decimal)multiplier, 0);

            await ProcessChargeVisualAsync(supplier, fakeAmount);
        }

        private static decimal CalculateMedian(List<decimal> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;

            return sorted.Count % 2 != 0
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2m;
        }

        private static async Task SendPushNotificationAsync(string title, string body)
        {
            try
            {
                if (!await LocalNotificationCenter.Current.AreNotificationsEnabled())
                    await LocalNotificationCenter.Current.RequestNotificationPermission();

                var request = new NotificationRequest
                {
                    NotificationId = Random.Shared.Next(1000, 99999),
                    Title = title,
                    Description = body
                };

                await LocalNotificationCenter.Current.Show(request);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Notification error: {ex.Message}");
            }
        }

        private void LoadProcessedOperations()
        {
            var saved = Preferences.Default.Get(ProcessedOpsPrefKey, string.Empty);
            if (string.IsNullOrWhiteSpace(saved)) return;

            foreach (var id in saved.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                _processedOperationIds.Add(id);
            }
        }

        private void SaveProcessedOperations()
        {
            var lastIds = _processedOperationIds.TakeLast(200);
            Preferences.Default.Set(ProcessedOpsPrefKey, string.Join(';', lastIds));
        }
    }
}
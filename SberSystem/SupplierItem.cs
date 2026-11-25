using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SberSystem
{
    public class SupplierItem : INotifyPropertyChanged
    {
        private decimal _averageAmount;
        private decimal _lastChargeAmount;
        private bool _hasAlert;

        public string Inn { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<decimal> ChargeHistory { get; set; } = [];

        public string InnDisplay => $"ИНН: {Inn}";

        public decimal AverageAmount
        {
            get => _averageAmount;
            set
            {
                if (_averageAmount == value) return;
                _averageAmount = value;
                OnPropertyChanged();
                NotifyVisualProperties();
            }
        }

        public decimal LastChargeAmount
        {
            get => _lastChargeAmount;
            set
            {
                if (_lastChargeAmount == value) return;
                _lastChargeAmount = value;
                OnPropertyChanged();
                NotifyVisualProperties();
            }
        }

        public bool HasAlert
        {
            get => _hasAlert;
            set
            {
                if (_hasAlert == value) return;
                _hasAlert = value;
                OnPropertyChanged();
                NotifyVisualProperties();
            }
        }

        public Color CardBackground => HasAlert
            ? Color.FromArgb("#FFF3E0")
            : Color.FromArgb("#FFFFFF");

        public Color BorderColor => HasAlert
            ? Color.FromArgb("#FF9800")
            : Color.FromArgb("#E0E0E0");

        public Color StatusTextColor => HasAlert
            ? Color.FromArgb("#E65100")
            : Color.FromArgb("#2E7D32");

        public string StatusText
        {
            get
            {
                if (!HasAlert)
                    return "В пределах нормы";

                if (AverageAmount <= 0)
                    return "Аномальное списание";

                var diffPercent = Math.Round((LastChargeAmount - AverageAmount) / AverageAmount * 100, 1);
                return $"Аномалия: +{diffPercent}% к норме";
            }
        }

        private void NotifyVisualProperties()
        {
            OnPropertyChanged(nameof(CardBackground));
            OnPropertyChanged(nameof(BorderColor));
            OnPropertyChanged(nameof(StatusTextColor));
            OnPropertyChanged(nameof(StatusText));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
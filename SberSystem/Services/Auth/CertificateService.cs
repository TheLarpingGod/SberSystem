using System.Security.Cryptography.X509Certificates;

namespace SberSystem.Services.Auth
{
    public sealed class CertificateService
    {
        private const string CertPasswordKey = "sber_mtls_cert_password";
        private readonly string _certPath = Path.Combine(FileSystem.AppDataDirectory, "sber_client.p12");

        public bool HasCertificate => File.Exists(_certPath);

        public async Task<(bool Success, string Message)> ImportCertificateAsync(Stream p12Stream, string password)
        {
            try
            {
                using var ms = new MemoryStream();
                await p12Stream.CopyToAsync(ms);
                var rawBytes = ms.ToArray();

                using var cert = new X509Certificate2(rawBytes, password, X509KeyStorageFlags.DefaultKeySet);

                if (!cert.HasPrivateKey)
                    return (false, "Сертификат не содержит закрытый ключ (Private Key).");

                if (cert.NotAfter < DateTime.UtcNow)
                    return (false, $"Срок действия сертификата истёк {cert.NotAfter:dd.MM.yyyy}.");

                var tmpPath = _certPath + ".tmp";
                await File.WriteAllBytesAsync(tmpPath, rawBytes);
                File.Move(tmpPath, _certPath, overwrite: true);

                await SecureStorage.Default.SetAsync(CertPasswordKey, password);

                return (true, $"Сертификат загружен (действует до {cert.NotAfter:dd.MM.yyyy})");
            }
            catch (Exception ex)
            {
                return (false, $"Не удалось открыть сертификат. Проверьте пароль. ({ex.Message})");
            }
        }

        public async Task<X509Certificate2?> LoadCertificateAsync()
        {
            if (!HasCertificate)
                return null;

            var password = await SecureStorage.Default.GetAsync(CertPasswordKey);
            if (string.IsNullOrEmpty(password))
                return null;

            var rawBytes = await File.ReadAllBytesAsync(_certPath);
            return new X509Certificate2(rawBytes, password, X509KeyStorageFlags.DefaultKeySet);
        }

        public void RemoveCertificate()
        {
            if (File.Exists(_certPath))
                File.Delete(_certPath);

            SecureStorage.Default.Remove(CertPasswordKey);
        }
    }
}
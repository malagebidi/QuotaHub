using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage.Streams;

namespace QuotaHub.Services
{
    public sealed class AppSettings
    {
        public string? OpenRouterApiKey { get; set; }

        public string? DeepSeekApiKey { get; set; }

        public int AutoRefreshMinutes { get; set; } = 5;

        public string Shortcut { get; set; } = "Alt+Q";
    }

    public sealed class SettingsService
    {
        private const string SettingsFileName = "settings.json";
        private const string ProtectionDescriptor = "LOCAL=user";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private sealed class StoredSettings
        {
            public string? OpenRouterApiKeyProtected { get; set; }

            public string? DeepSeekApiKeyProtected { get; set; }

            public int? AutoRefreshMinutes { get; set; }

            public string? Shortcut { get; set; }
        }

        public async Task<AppSettings> LoadAsync()
        {
            try
            {
                string path = Path.Combine(AppPaths.GetDataFolderPath(), SettingsFileName);
                if (!File.Exists(path))
                {
                    return new AppSettings();
                }

                string json = await File.ReadAllTextAsync(path);
                StoredSettings? stored = JsonSerializer.Deserialize<StoredSettings>(json, JsonOptions);
                if (stored == null)
                {
                    return new AppSettings();
                }

                return new AppSettings
                {
                    OpenRouterApiKey = await UnprotectAsync(stored.OpenRouterApiKeyProtected),
                    DeepSeekApiKey = await UnprotectAsync(stored.DeepSeekApiKeyProtected),
                    AutoRefreshMinutes = NormalizeAutoRefreshMinutes(stored.AutoRefreshMinutes),
                    Shortcut = NormalizeShortcut(stored.Shortcut),
                };
            }
            catch
            {
                // Corrupted settings must not crash startup. Start empty.
                return new AppSettings();
            }
        }

        public async Task SaveAsync(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            StoredSettings stored = new()
            {
                OpenRouterApiKeyProtected = await ProtectAsync(settings.OpenRouterApiKey),
                DeepSeekApiKeyProtected = await ProtectAsync(settings.DeepSeekApiKey),
                AutoRefreshMinutes = NormalizeAutoRefreshMinutes(settings.AutoRefreshMinutes),
                Shortcut = NormalizeShortcut(settings.Shortcut),
            };

            string dataFolder = AppPaths.GetDataFolderPath();
            string path = Path.Combine(dataFolder, SettingsFileName);
            string json = JsonSerializer.Serialize(stored, JsonOptions);
            await File.WriteAllTextAsync(path, json);
        }

        private static string NormalizeShortcut(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Alt+Q" : value.Trim();
        }

        private static int NormalizeAutoRefreshMinutes(int? value)
        {
            if (value == null)
            {
                return 5;
            }

            return Math.Clamp(value.Value, 1, 120);
        }

        private static async Task<string?> ProtectAsync(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            DataProtectionProvider provider = new(ProtectionDescriptor);
            IBuffer data = CryptographicBuffer.ConvertStringToBinary(value, BinaryStringEncoding.Utf8);
            IBuffer encrypted = await provider.ProtectAsync(data);
            return CryptographicBuffer.EncodeToBase64String(encrypted);
        }

        private static async Task<string?> UnprotectAsync(string? protectedValue)
        {
            if (string.IsNullOrEmpty(protectedValue))
            {
                return null;
            }

            try
            {
                IBuffer encrypted = CryptographicBuffer.DecodeFromBase64String(protectedValue);
                DataProtectionProvider provider = new();
                IBuffer data = await provider.UnprotectAsync(encrypted);
                return CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, data);
            }
            catch
            {
                // Undecryptable value (for example after a user profile change).
                // Treat as missing instead of failing the whole load.
                return null;
            }
        }
    }
}

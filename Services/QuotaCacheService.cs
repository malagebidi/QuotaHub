using QuotaHub.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace QuotaHub.Services
{
    public sealed class QuotaCacheService
    {
        private const string CacheFileName = "quota-cache.json";

        public async Task<IReadOnlyList<QuotaSnapshot>> LoadAsync()
        {
            try
            {
                StorageFolder folder = ApplicationData.Current.LocalFolder;
                string path = Path.Combine(folder.Path, CacheFileName);
                if (!File.Exists(path))
                {
                    return Array.Empty<QuotaSnapshot>();
                }

                string json = await File.ReadAllTextAsync(path);
                List<QuotaSnapshot>? snapshots = JsonSerializer.Deserialize<List<QuotaSnapshot>>(json);
                if (snapshots == null)
                {
                    return Array.Empty<QuotaSnapshot>();
                }

                return snapshots;
            }
            catch
            {
                // Corrupted cache must not break startup. Refresh will overwrite it.
                return Array.Empty<QuotaSnapshot>();
            }
        }

        public async Task SaveAsync(IReadOnlyList<QuotaSnapshot> snapshots)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(snapshots);

                StorageFolder folder = ApplicationData.Current.LocalFolder;
                string path = Path.Combine(folder.Path, CacheFileName);
                string json = JsonSerializer.Serialize(snapshots);
                await File.WriteAllTextAsync(path, json);
            }
            catch
            {
                // Cache is best-effort only. Never break a successful refresh.
            }
        }
    }
}

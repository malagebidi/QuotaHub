using QuotaHub.Models;
using QuotaHub.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QuotaHub.Services
{
    public sealed class QuotaService
    {
        private readonly IReadOnlyList<IQuotaProvider> _providers;

        public QuotaService()
            : this(new IQuotaProvider[] { new OpenRouterProvider(), new DeepSeekProvider() })
        {
        }

        public QuotaService(IEnumerable<IQuotaProvider> providers)
        {
            ArgumentNullException.ThrowIfNull(providers);
            _providers = providers.ToList();
        }

        public IReadOnlyList<IQuotaProvider> Providers => _providers;

        public async Task<IReadOnlyList<QuotaSnapshot>> RefreshAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);

            Task<QuotaSnapshot>[] tasks = _providers
                .Select(provider => provider.GetQuotaAsync(GetApiKey(settings, provider.ProviderId) ?? string.Empty, cancellationToken))
                .ToArray();

            return await Task.WhenAll(tasks);
        }

        private static string? GetApiKey(AppSettings settings, string providerId)
        {
            return providerId switch
            {
                "openrouter" => settings.OpenRouterApiKey,
                "deepseek" => settings.DeepSeekApiKey,
                _ => null,
            };
        }
    }
}

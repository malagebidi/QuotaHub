using QuotaHub.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QuotaHub.Providers
{
    public interface IQuotaProvider
    {
        string ProviderId { get; }

        string DisplayName { get; }

        Task<IReadOnlyList<QuotaSnapshot>> GetQuotaAsync(string apiKey, CancellationToken cancellationToken);
    }
}

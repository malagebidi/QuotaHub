using QuotaHub.Models;
using System.Threading;
using System.Threading.Tasks;

namespace QuotaHub.Providers
{
    public interface IQuotaProvider
    {
        string ProviderId { get; }

        string DisplayName { get; }

        Task<QuotaSnapshot> GetQuotaAsync(string apiKey, CancellationToken cancellationToken);
    }
}

using QuotaHub.Models;
using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace QuotaHub.Providers
{
    internal sealed class DeepSeekProvider : IQuotaProvider
    {
        private const string BalanceEndpoint = "https://api.deepseek.com/user/balance";

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        public string ProviderId => "deepseek";

        public string DisplayName => "DeepSeek";

        public async Task<QuotaSnapshot> GetQuotaAsync(string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return Fail("API key is missing.", "USD");
            }

            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, BalanceEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

                using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
                    {
                        return Fail($"Invalid API key (HTTP {(int)response.StatusCode}).", "USD");
                    }

                    return Fail($"Request failed (HTTP {(int)response.StatusCode}).", "USD");
                }

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                return Parse(json);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Fail("Request timed out.", "USD");
            }
            catch (HttpRequestException ex)
            {
                return Fail($"Network error: {ex.Message}", "USD");
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is FormatException)
            {
                return Fail("Unexpected response format.", "USD");
            }
        }

        private QuotaSnapshot Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("balance_infos", out JsonElement balances) ||
                balances.ValueKind != JsonValueKind.Array ||
                balances.GetArrayLength() == 0)
            {
                return Fail("No balance information returned.", "USD");
            }

            JsonElement info = balances[0];
            string currency = info.TryGetProperty("currency", out JsonElement currencyElement) &&
                currencyElement.ValueKind == JsonValueKind.String
                ? currencyElement.GetString() ?? "USD"
                : "USD";

            double remaining = ParseAmount(info, "total_balance");

            return new QuotaSnapshot(
                ProviderId,
                DisplayName,
                null,
                null,
                remaining,
                currency,
                DateTimeOffset.UtcNow,
                null);
        }

        private static double ParseAmount(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
            {
                throw new FormatException($"Missing property '{propertyName}'.");
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return double.Parse(value.GetString() ?? "0", CultureInfo.InvariantCulture);
            }

            return value.GetDouble();
        }

        private QuotaSnapshot Fail(string message, string unit)
        {
            return new QuotaSnapshot(
                ProviderId,
                DisplayName,
                null,
                null,
                null,
                unit,
                DateTimeOffset.UtcNow,
                message);
        }
    }
}

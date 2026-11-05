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
    internal sealed class OpenRouterProvider : IQuotaProvider
    {
        private const string KeyEndpoint = "https://openrouter.ai/api/v1/key";
        private const string CreditsEndpoint = "https://openrouter.ai/api/v1/credits";
        private const string Unit = "credits";

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        public string ProviderId => "openrouter";

        public string DisplayName => "OpenRouter";

        public async Task<QuotaSnapshot> GetQuotaAsync(string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return Fail("API key is missing.");
            }

            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, KeyEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

                using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
                    {
                        return Fail($"Invalid API key (HTTP {(int)response.StatusCode}).");
                    }

                    return Fail($"Request failed (HTTP {(int)response.StatusCode}).");
                }

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                QuotaSnapshot snapshot = ParseKey(json);

                // Keys without a limit carry no remaining amount. Fall back to the
                // account balance so unlimited keys still show something useful.
                // A failing credits lookup must not break the key snapshot.
                if (snapshot.Total == null && snapshot.ErrorMessage == null)
                {
                    double? accountRemaining = await TryGetAccountRemainingAsync(apiKey.Trim(), cancellationToken);
                    if (accountRemaining != null)
                    {
                        snapshot = snapshot with { Remaining = accountRemaining };
                    }
                }

                return snapshot;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Fail("Request timed out.");
            }
            catch (HttpRequestException ex)
            {
                return Fail($"Network error: {ex.Message}");
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is FormatException)
            {
                return Fail("Unexpected response format.");
            }
        }

        private QuotaSnapshot ParseKey(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement data = document.RootElement.GetProperty("data");

            double used = data.GetProperty("usage").GetDouble();
            double? total = GetNullableDouble(data, "limit");
            double? remaining = GetNullableDouble(data, "limit_remaining");
            if (remaining == null && total != null)
            {
                remaining = total.Value - used;
            }

            return new QuotaSnapshot(
                ProviderId,
                DisplayName,
                used,
                total,
                remaining,
                Unit,
                DateTimeOffset.UtcNow,
                null);
        }

        private static async Task<double?> TryGetAccountRemainingAsync(string apiKey, CancellationToken cancellationToken)
        {
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, CreditsEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                return ParseCredits(json);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private static double? ParseCredits(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            JsonElement container = root;
            if (root.TryGetProperty("data", out JsonElement data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("total_credits", out _))
            {
                container = data;
            }

            if (!container.TryGetProperty("total_credits", out JsonElement credits) ||
                !container.TryGetProperty("total_usage", out JsonElement usage))
            {
                return null;
            }

            return GetDouble(credits) - GetDouble(usage);
        }

        private static double GetDouble(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                return double.Parse(value.GetString() ?? "0", CultureInfo.InvariantCulture);
            }

            return value.GetDouble();
        }

        private static double? GetNullableDouble(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined)
            {
                return null;
            }

            return GetDouble(value);
        }

        private QuotaSnapshot Fail(string message)
        {
            return new QuotaSnapshot(
                ProviderId,
                DisplayName,
                null,
                null,
                null,
                Unit,
                DateTimeOffset.UtcNow,
                message);
        }
    }
}

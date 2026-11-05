using System;

namespace QuotaHub.Models
{
    /// <summary>
    /// Normalized quota snapshot for one provider. The UI only depends on this type.
    /// OpenRouter reports usage against a limit; DeepSeek reports a remaining
    /// balance. Each provider fills the fields its API supports and leaves
    /// the others null.
    /// </summary>
    public sealed record QuotaSnapshot(
        string ProviderId,
        string DisplayName,
        double? Used,
        double? Total,
        double? Remaining,
        string Unit,
        DateTimeOffset UpdatedAt,
        string? ErrorMessage);
}

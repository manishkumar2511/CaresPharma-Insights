using System;

namespace CaresPharma_Insights.Application.Mcp.Models
{
    /// <summary>
    /// Data Transfer Object representing freshly issued tenant credentials returned to Super Admin.
    /// Contains plain secret for one-time view before cryptographic sealing.
    /// </summary>
    public sealed record GeneratedMcpCredentials(
        string ClientId,
        string ClientSecret,
        string PharmacyOwnerId,
        DateTime? ExpiresAtUtc
    );
}

using System;
using System.Threading;
using System.Threading.Tasks;
using CaresPharma_Insights.Application.Mcp.Models;
using CaresPharma_Insights.Domain.Models;

namespace CaresPharma_Insights.Application.Mcp.Services
{
    /// <summary>
    /// Contract for enterprise cryptographic authentication and lifecycle governance of multi-tenant MCP clients.
    /// </summary>
    public interface ITenantMcpAuthService
    {
        Task<GeneratedMcpCredentials> GenerateCredentialsAsync(
            string pharmacyOwnerId,
            string? clientName = null,
            int? expiryDays = null,
            string? adminId = null,
            CancellationToken cancellationToken = default);

        Task<TenantSecurityContext?> ValidateCredentialsAsync(
            string clientId,
            string clientSecret,
            string? ipAddress = null,
            string? userAgent = null,
            CancellationToken cancellationToken = default);

        Task<bool> RevokeCredentialsAsync(string pharmacyOwnerId, CancellationToken cancellationToken = default);

        Task<bool> ToggleStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
    }
}

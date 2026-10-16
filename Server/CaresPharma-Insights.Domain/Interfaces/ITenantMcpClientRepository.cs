using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CaresPharma_Insights.Domain.Models;

namespace CaresPharma_Insights.Domain.Interfaces
{
    public interface ITenantMcpClientRepository
    {
        Task<TenantMcpClient?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default);
        Task<TenantMcpClient?> GetActiveByOwnerIdAsync(string pharmacyOwnerId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<TenantMcpClient>> GetAllByOwnerIdAsync(string pharmacyOwnerId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<TenantMcpClient>> GetAllAsync(CancellationToken cancellationToken = default);
        Task InsertAsync(TenantMcpClient client, CancellationToken cancellationToken = default);
        Task UpdateStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
        Task UpdateLastUsedAsync(Guid id, DateTime lastUsedAtUtc, CancellationToken cancellationToken = default);
        Task RevokeByOwnerIdAsync(string pharmacyOwnerId, CancellationToken cancellationToken = default);
        Task LogActivityAsync(TenantMcpActivityLog activityLog, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<TenantMcpActivityLog>> GetRecentActivityLogsAsync(int count = 100, CancellationToken cancellationToken = default);
    }
}

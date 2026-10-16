using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace CaresPharma_Insights.Domain.Interfaces
{
    /// <summary>
    /// Contract for read-only analytical querying and schema inspection.
    /// Strictly isolates database querying away from Semantic Kernel plugins.
    /// </summary>
    public interface IAnalyticsDataRepository
    {
        Task<string> GetCachedDatabaseSchemaAsync(CancellationToken cancellationToken = default);
        Task<string> ExecuteReadOnlyQueryAsync(string sqlQuery, string? tenantId = null, string? userRole = null, int maxRows = 50, CancellationToken cancellationToken = default);
        Task<int> GetTotalUsersCountAsync(string? tenantId = null, CancellationToken cancellationToken = default);
        Task<string> GetTenantCurrentStockAsync(string tenantId, CancellationToken cancellationToken = default);
        Task<string> GetTenantExpiringMedicinesAsync(string tenantId, int daysThreshold = 90, CancellationToken cancellationToken = default);
        Task<string> GetTenantSalesSummaryAsync(string tenantId, int days = 30, CancellationToken cancellationToken = default);
        Task<string> GetTenantLowStockMedicinesAsync(string tenantId, int threshold = 10, CancellationToken cancellationToken = default);
    }
}

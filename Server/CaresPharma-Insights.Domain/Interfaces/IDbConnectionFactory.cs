using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace CaresPharma_Insights.Domain.Interfaces
{
    /// <summary>
    /// Thread-safe factory for acquiring open database connections.
    /// Consumers MUST dispose connections after use.
    /// </summary>
    public interface IDbConnectionFactory
    {
        IDbConnection CreateConnection();
        Task<IDbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
    }
}

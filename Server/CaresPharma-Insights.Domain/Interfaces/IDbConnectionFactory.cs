using System.Data;
using System.Threading.Tasks;

namespace CaresPharma_Insights.Domain.Interfaces
{
    public interface IDbConnectionFactory
    {
        IDbConnection CreateConnection();
        Task<IDbConnection> CreateConnectionAsync();
    }
}

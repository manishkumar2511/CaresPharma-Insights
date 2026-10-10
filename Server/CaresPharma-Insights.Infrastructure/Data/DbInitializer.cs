using System.Threading.Tasks;
using Dapper;
using CaresPharma_Insights.Domain.Interfaces;

namespace CaresPharma_Insights.Infrastructure.Data
{
    public class DbInitializer
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public DbInitializer(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task InitializeAsync()
        {
            using System.Data.IDbConnection connection = await _connectionFactory.CreateConnectionAsync();
            
            // Dynamic schema is now handled exclusively by DataSyncWorker.cs
            // You can add logic here for core tables that are NOT synced from SQL Server
        }
    }
}

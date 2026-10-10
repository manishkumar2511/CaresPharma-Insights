using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Npgsql;
using CaresPharma_Insights.Domain.Interfaces;

namespace CaresPharma_Insights.Infrastructure.Data
{
    public class AppDbConnectionFactory : IDbConnectionFactory
    {
        private readonly IConfiguration _configuration;

        public AppDbConnectionFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IDbConnection CreateConnection()
        {
            string? connectionString = _configuration.GetConnectionString("DefaultConnection");
            return new NpgsqlConnection(connectionString);
        }

        public async Task<IDbConnection> CreateConnectionAsync()
        {
            string? connectionString = _configuration.GetConnectionString("DefaultConnection");
            NpgsqlConnection connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            return connection;
        }
    }
}

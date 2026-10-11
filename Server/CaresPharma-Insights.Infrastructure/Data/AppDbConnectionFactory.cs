using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Npgsql;
using CaresPharma_Insights.Domain.Interfaces;

namespace CaresPharma_Insights.Infrastructure.Data
{
    public class AppDbConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;

        public AppDbConnectionFactory(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        public IDbConnection CreateConnection()
        {
            return new NpgsqlConnection(_connectionString);
        }

        public async Task<IDbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
        {
            NpgsqlConnection connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }
}

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using CaresPharma_Insights.Domain.Interfaces;

namespace CaresPharma_Insights.Application.Plugins
{
    public class SqlAnalyticsPlugin
    {
        private readonly IAnalyticsDataRepository _dataRepository;

        public SqlAnalyticsPlugin(IAnalyticsDataRepository dataRepository)
        {
            _dataRepository = dataRepository;
        }

        [KernelFunction("get_database_schema")]
        [Description("Gets the cached schema of all tables and columns available in the insights database. Call this first if you are unsure which tables exist.")]
        public async Task<string> GetDatabaseSchemaAsync(CancellationToken cancellationToken = default)
        {
            return await _dataRepository.GetCachedDatabaseSchemaAsync(cancellationToken);
        }

        [KernelFunction("execute_sql_query")]
        [Description("Executes a strictly READ-ONLY SQL SELECT query against the analytics database and returns the result rows as JSON.")]
        public async Task<string> ExecuteSqlQueryAsync(
            [Description("The SQL SELECT query to execute.")] string sqlQuery,
            CancellationToken cancellationToken = default)
        {
            return await _dataRepository.ExecuteReadOnlyQueryAsync(sqlQuery, tenantId: null, userRole: null, maxRows: 50, cancellationToken);
        }
    }
}

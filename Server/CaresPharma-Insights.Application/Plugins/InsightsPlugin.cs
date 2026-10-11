using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using CaresPharma_Insights.Domain.Interfaces;

namespace CaresPharma_Insights.Application.Plugins
{
    public class InsightsPlugin
    {
        private readonly IAnalyticsDataRepository _dataRepository;

        public InsightsPlugin(IAnalyticsDataRepository dataRepository)
        {
            _dataRepository = dataRepository;
        }

        [KernelFunction("get_total_users_count")]
        [Description("Gets the real total number of users registered on the platform.")]
        public async Task<int> GetTotalUsersCountAsync(CancellationToken cancellationToken = default)
        {
            return await _dataRepository.GetTotalUsersCountAsync(null, cancellationToken);
        }
    }
}

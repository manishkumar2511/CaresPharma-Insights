using System.Threading;
using System.Threading.Tasks;
using CaresPharma_Insights.Shared.Dtos;

namespace CaresPharma_Insights.Application.Services
{
    public interface IChatOrchestratorService
    {
        Task<ChatResponse> ProcessChatAsync(
            ChatRequest request, 
            UserContextDto userContext, 
            CancellationToken cancellationToken = default);
    }
}

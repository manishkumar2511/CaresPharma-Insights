using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CaresPharma_Insights.Shared.Dtos;

namespace CaresPharma_Insights.Domain.Interfaces
{
    public interface IConversationRepository
    {
        Task<List<ConversationSummaryDto>> GetUserConversationsAsync(string userId, string? tenantId, CancellationToken cancellationToken = default);
        Task<ConversationDetailDto?> GetConversationDetailsAsync(Guid conversationId, string userId, string? tenantId, CancellationToken cancellationToken = default);
        Task<Guid> CreateConversationAsync(string userId, string? tenantId, string title, CancellationToken cancellationToken = default);
        Task SaveMessageAsync(Guid conversationId, string role, string content, CancellationToken cancellationToken = default);
        Task DeleteConversationAsync(Guid conversationId, string userId, string? tenantId, CancellationToken cancellationToken = default);
    }
}

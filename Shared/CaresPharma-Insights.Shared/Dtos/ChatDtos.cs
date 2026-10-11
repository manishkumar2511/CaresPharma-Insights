using System;
using System.Collections.Generic;

namespace CaresPharma_Insights.Shared.Dtos
{
    public record ChatMessageDto(
        string Content, 
        bool IsUser, 
        DateTime? CreatedAt = null
    );

    public record ChatRequest(
        string Message,
        Guid? ConversationId = null,
        List<ChatMessageDto>? History = null,
        string? TenantId = null
    );

    public record ChatResponse(
        string Reply,
        Guid? ConversationId = null,
        bool Success = true,
        string? Error = null
    );

    public record ConversationSummaryDto(
        Guid Id,
        string Title,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );

    public record ConversationDetailDto(
        Guid Id,
        string Title,
        List<ChatMessageDto> Messages,
        DateTime CreatedAt
    );

    public record UserContextDto(
        string UserId,
        string Email,
        string Role,
        string? PharmacyOwnerId,
        string? PartnerNetworkId = null
    );
}

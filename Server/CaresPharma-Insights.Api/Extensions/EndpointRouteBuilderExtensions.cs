using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using CaresPharma_Insights.Application.Services;
using CaresPharma_Insights.Domain.Interfaces;
using CaresPharma_Insights.Shared.Dtos;

namespace CaresPharma_Insights.Api.Extensions
{
    public static class EndpointRouteBuilderExtensions
    {
        public static IEndpointRouteBuilder MapInsightsEndpoints(this IEndpointRouteBuilder endpoints)
        {
            // Health Check Endpoint (Public)
            endpoints.MapGet("/api/health", () => 
                Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow })
            )
            .WithName("HealthCheck")
            .WithSummary("Insights API health and readiness check.");

            // 1. Get User Conversations List (Protected)
            endpoints.MapGet("/api/conversations", async (
                HttpContext httpContext, 
                IConversationRepository conversationRepository, 
                CancellationToken cancellationToken) =>
            {
                UserContextDto userContext = ExtractUserContext(httpContext);
                List<ConversationSummaryDto> conversations = await conversationRepository.GetUserConversationsAsync(
                    userContext.UserId, 
                    userContext.PharmacyOwnerId, 
                    cancellationToken);

                return Results.Ok(conversations);
            })
            .WithName("GetUserConversations")
            .WithSummary("Retrieves chat session history for the authenticated CaresPharma user.");

            // 2. Get Single Conversation Messages (Protected)
            endpoints.MapGet("/api/conversations/{id:guid}", async (
                Guid id, 
                HttpContext httpContext, 
                IConversationRepository conversationRepository, 
                CancellationToken cancellationToken) =>
            {
                UserContextDto userContext = ExtractUserContext(httpContext);
                ConversationDetailDto? conversation = await conversationRepository.GetConversationDetailsAsync(
                    id, 
                    userContext.UserId, 
                    userContext.PharmacyOwnerId, 
                    cancellationToken);

                if (conversation == null)
                {
                    return Results.NotFound(new { message = "Conversation not found or unauthorized access." });
                }

                return Results.Ok(conversation);
            })
            .WithName("GetConversationDetails")
            .WithSummary("Retrieves all messages for a specific conversation session.");

            // 3. Delete Conversation (Protected)
            endpoints.MapDelete("/api/conversations/{id:guid}", async (
                Guid id, 
                HttpContext httpContext, 
                IConversationRepository conversationRepository, 
                CancellationToken cancellationToken) =>
            {
                UserContextDto userContext = ExtractUserContext(httpContext);
                await conversationRepository.DeleteConversationAsync(
                    id, 
                    userContext.UserId, 
                    userContext.PharmacyOwnerId, 
                    cancellationToken);

                return Results.NoContent();
            })
            .WithName("DeleteConversation")
            .WithSummary("Deletes a conversation session belonging to the user.");

            // 4. AI Copilot Chat Endpoint (Protected with Tenant Context)
            endpoints.MapPost("/api/chat", async (
                ChatRequest request, 
                HttpContext httpContext,
                IChatOrchestratorService chatOrchestratorService, 
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request?.Message))
                {
                    return Results.BadRequest(new ChatResponse("Request message cannot be empty.", Success: false));
                }

                UserContextDto userContext = ExtractUserContext(httpContext);
                ChatResponse chatResponse = await chatOrchestratorService.ProcessChatAsync(request, userContext, cancellationToken);
                return Results.Ok(chatResponse);
            })
            .WithName("ProcessChat")
            .WithSummary("Processes AI queries using Semantic Kernel with strict tenant-isolation and database chat persistence.")
            .Produces<ChatResponse>(StatusCodes.Status200OK)
            .Produces<ChatResponse>(StatusCodes.Status400BadRequest);

            return endpoints;
        }

        private static UserContextDto ExtractUserContext(HttpContext context)
        {
            ClaimsPrincipal user = context.User;
            string userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                ?? user.FindFirst("sub")?.Value 
                ?? "superadmin-local";
            string email = user.FindFirst(ClaimTypes.Email)?.Value ?? "admin@carespharma.com";
            string role = user.FindFirst(ClaimTypes.Role)?.Value ?? "SuperAdmin";
            
            // Check both ownerId and pharmacyOwnerId claims issued by CaresPharma.Api JwtService
            string? pharmacyOwnerId = user.FindFirst("pharmacyOwnerId")?.Value 
                ?? user.FindFirst("ownerId")?.Value;

            if (string.IsNullOrWhiteSpace(pharmacyOwnerId))
            {
                pharmacyOwnerId = null;
            }

            // Check partnerNetworkId claims for PartnerNetwork channel partners
            string? partnerNetworkId = user.FindFirst("partnerNetworkId")?.Value
                ?? user.FindFirst("partnerId")?.Value
                ?? user.FindFirst("channelPartnerId")?.Value;

            if (string.IsNullOrWhiteSpace(partnerNetworkId))
            {
                partnerNetworkId = null;
            }

            return new UserContextDto(userId, email, role, pharmacyOwnerId, partnerNetworkId);
        }
    }
}

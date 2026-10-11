using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using CaresPharma_Insights.Application.Plugins;
using CaresPharma_Insights.Application.Services;
using CaresPharma_Insights.Domain.Interfaces;
using CaresPharma_Insights.Shared.Dtos;

namespace CaresPharma_Insights.Application.Services
{
    public class ChatOrchestratorService : IChatOrchestratorService
    {
        private readonly Kernel _kernel;
        private readonly IConversationRepository _conversationRepository;
        private readonly ILogger<ChatOrchestratorService> _logger;

        public ChatOrchestratorService(
            Kernel kernel, 
            IConversationRepository conversationRepository,
            ILogger<ChatOrchestratorService> logger)
        {
            _kernel = kernel;
            _conversationRepository = conversationRepository;
            _logger = logger;
        }

        public async Task<ChatResponse> ProcessChatAsync(
            ChatRequest request, 
            UserContextDto userContext, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return new ChatResponse("Query cannot be empty.", Success: false);
            }

            // Build dynamic tenant-aware system prompt
            StringBuilder promptBuilder = new StringBuilder();
            promptBuilder.AppendLine("You are CaresPharma Insights Copilot, an enterprise-grade AI analytics assistant for pharmacy.");
            promptBuilder.AppendLine("CRITICAL OPERATING RULES:");
            promptBuilder.AppendLine("1. Always be concise, factual, and helpful.");
            promptBuilder.AppendLine("2. If asked what data or tables exist, call 'get_database_schema' and summarize briefly without dumping columns unless asked.");
            promptBuilder.AppendLine("3. When formulating SQL queries, generate strictly valid PostgreSQL SELECT queries. Never perform modifications.");
            promptBuilder.AppendLine("4. If a query produces results, provide an executive summary followed by clean markdown tables or bullets.");
            promptBuilder.AppendLine("5. If the user asks in Hindi or Hinglish, respond naturally in Hinglish/Hindi while maintaining professional clarity.");

            // Enforce Tenant & User Role Boundary in LLM instructions
            if (string.Equals(userContext.Role, "PartnerNetwork", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(userContext.Role, "ChannelPartner", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(userContext.PartnerNetworkId))
            {
                promptBuilder.AppendLine($"SECURITY RESTRICTION: The current user is a Channel Partner (Partner Network) with PartnerNetworkId = '{userContext.PartnerNetworkId}'.");
                promptBuilder.AppendLine("You MUST strictly filter all data queries to ONLY include tables/records relating to this Partner Network's referrals and leads (e.g., 'PartnerNetworks', 'PartnerNetworkLeads').");
                promptBuilder.AppendLine("DO NOT query or disclose platform pharmacy inventory, other partner leads, or global admin transactions.");
            }
            else if (!string.Equals(userContext.Role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                promptBuilder.AppendLine($"SECURITY RESTRICTION: The current user is a '{userContext.Role}' with PharmacyOwnerId/TenantId = '{userContext.PharmacyOwnerId}'.");
                promptBuilder.AppendLine("You MUST strictly filter all data queries to ONLY include records belonging to this user's PharmacyOwnerId.");
                promptBuilder.AppendLine("DO NOT query or disclose information belonging to other pharmacy owners or global administrators.");
            }
            else
            {
                promptBuilder.AppendLine("The current user is a 'SuperAdmin' with platform-wide analytical access.");
            }

            IChatCompletionService chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>();
            ChatHistory chatHistory = new ChatHistory(promptBuilder.ToString());

            // Ensure conversation exists in database or create new one
            Guid conversationId = request.ConversationId ?? Guid.Empty;
            if (conversationId == Guid.Empty)
            {
                string autoTitle = request.Message.Length > 40 ? request.Message.Substring(0, 37) + "..." : request.Message;
                conversationId = await _conversationRepository.CreateConversationAsync(
                    userContext.UserId, 
                    userContext.PharmacyOwnerId, 
                    autoTitle, 
                    cancellationToken);
            }

            // Persist the user's message in the database
            await _conversationRepository.SaveMessageAsync(conversationId, "user", request.Message, cancellationToken);

            // Reconstruct multi-turn conversation memory if provided
            if (request.History != null && request.History.Count > 0)
            {
                int startIdx = Math.Max(0, request.History.Count - 10);
                for (int i = startIdx; i < request.History.Count; i++)
                {
                    ChatMessageDto historicalMessage = request.History[i];
                    if (historicalMessage.IsUser)
                    {
                        chatHistory.AddUserMessage(historicalMessage.Content);
                    }
                    else
                    {
                        chatHistory.AddAssistantMessage(historicalMessage.Content);
                    }
                }
            }

            // Add current prompt
            chatHistory.AddUserMessage(request.Message);

            PromptExecutionSettings executionSettings = new()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            ChatMessageContent response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings: executionSettings,
                kernel: _kernel,
                cancellationToken: cancellationToken
            );

            string reply = response.Content ?? "No answer could be determined from the available data.";

            // Persist the assistant's response in the database
            await _conversationRepository.SaveMessageAsync(conversationId, "assistant", reply, cancellationToken);

            return new ChatResponse(reply, ConversationId: conversationId, Success: true);
        }
    }
}

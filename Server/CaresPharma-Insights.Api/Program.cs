using CaresPharma_Insights.Domain.Interfaces;
using CaresPharma_Insights.Infrastructure.Data;
using System;
using System.Net.Http;
using CaresPharma_Insights.Api.Infrastructure;
using CaresPharma_Insights.Application.Plugins;
using CaresPharma_Insights.Domain.Interfaces;
using CaresPharma_Insights.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazor", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// Semantic Kernel Setup with OpenRouter
string? openRouterKey = builder.Configuration["OpenRouter:ApiKey"] 
    ?? throw new InvalidOperationException("OpenRouter API key is missing. Ensure it's set in User Secrets.");

HttpClient httpClient = new HttpClient(new OpenRouterHttpClientHandler());

builder.Services.AddKernel()
    .AddOpenAIChatCompletion(
        modelId: "openai/gpt-4o-mini", // Free/fast model on OpenRouter
        apiKey: openRouterKey,
        httpClient: httpClient
    );

builder.Services.AddSingleton<IDbConnectionFactory, PostgresConnectionFactory>();
builder.Services.AddTransient<DbInitializer>();
builder.Services.AddHostedService<DataSyncWorker>();
builder.Services.AddSingleton<InsightsPlugin>();
builder.Services.AddSingleton<SqlAnalyticsPlugin>(); // New Plugin
builder.WebHost.UseUrls("http://localhost:5500", "https://localhost:5501");

WebApplication app = builder.Build();

// Initialize Database
using (IServiceScope scope = app.Services.CreateScope())
{
    DbInitializer dbInit = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    await dbInit.InitializeAsync();
}

app.UseCors("AllowBlazor");

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "DevServer")
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CaresPharma Insights API v1");
        c.RoutePrefix = "swagger"; 
    });
}
app.UseHttpsRedirection();

// Chat Endpoint
app.MapPost("/api/chat", async (ChatRequest request, InsightsPlugin plugin, SqlAnalyticsPlugin sqlPlugin, Kernel kernel) =>
{
    IChatCompletionService chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
    kernel.Plugins.AddFromObject(plugin, "InsightsPlugin");
    kernel.Plugins.AddFromObject(sqlPlugin, "SqlAnalyticsPlugin"); // Add new Plugin

    OpenAIPromptExecutionSettings settings = new()
    {
        ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
        Temperature = 0.3
    };

    try
    {
        string systemPrompt = @"You are CaresPharma Insights Copilot. You assist superadmins and managers by giving them insights about their platform. 
CRITICAL RULES:
1. Always answer in a concise, natural, and helpful way.
2. If the user asks 'What tables do we have?' or similar, ONLY list the table names. DO NOT list columns and data types unless specifically asked.
3. Keep your answers brief and user-friendly. Never dump raw JSON or raw schema dumps to the user.";
        
        ChatHistory chatHistory = new ChatHistory(systemPrompt);
        chatHistory.AddUserMessage(request.Message);
        
        ChatMessageContent response = await chatCompletionService.GetChatMessageContentAsync(
            chatHistory,
            executionSettings: settings,
            kernel: kernel
        );

        return Results.Ok(new ChatResponse(response.Content ?? "I could not generate a response."));
    }
    catch (Exception ex)
    {
        return Results.Ok(new ChatResponse("Error communicating with AI: " + ex.Message));
    }
});

app.Run();

public record ChatRequest(string Message);
public record ChatResponse(string Reply);


using Microsoft.AspNetCore.Builder;
using CaresPharma_Insights.Api.Endpoints;
using CaresPharma_Insights.Api.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Register application services via modular extension methods
builder.Services
    .AddApiDocumentationServices()
    .AddExceptionHandlingServices()
    .AddCorsPolicyServices(builder.Configuration)
    .AddAuthenticationAndAuthorizationServices(builder.Configuration)
    .AddPersistenceAndRepositoryServices(builder.Configuration)
    .AddSemanticKernelAiServices(builder.Configuration)
    .AddMcpServerServices();

WebApplication app = builder.Build();

// Configure the HTTP request pipeline
app.UseExceptionHandler();
app.UseCors("InsightsCorsPolicy");
app.UseInsightsSwagger();
app.UseHttpsRedirection();

// Authentication & Authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Map route endpoints
app.MapInsightsEndpoints();
app.MapMcpEndpoints();

// Initialize database connection and schemas
await app.InitializeDatabaseAsync();

app.Run();

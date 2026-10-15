using System;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;
using CaresPharma_Insights.Api.Middlewares;
using CaresPharma_Insights.Application.Plugins;
using CaresPharma_Insights.Application.Services;
using CaresPharma_Insights.Domain.Interfaces;
using CaresPharma_Insights.Infrastructure.Data;
using CaresPharma_Insights.Infrastructure.Repositories;
using CaresPharma_Insights.Shared.Configurations;

namespace CaresPharma_Insights.Api.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApiDocumentationServices(this IServiceCollection services)
        {
            services.AddOpenApi();
            services.AddSwaggerGen();
            return services;
        }

        public static IServiceCollection AddExceptionHandlingServices(this IServiceCollection services)
        {
            services.AddExceptionHandler<GlobalExceptionHandler>();
            services.AddProblemDetails();
            return services;
        }

        public static IServiceCollection AddAuthenticationAndAuthorizationServices(this IServiceCollection services, IConfiguration configuration)
        {
            string jwtSecret = configuration["JwtSettings:Secret"] ?? "CaresPharmaAuthSuperSecretKeyForDevelopmentOnly";
            string jwtIssuer = configuration["JwtSettings:Issuer"] ?? "CaresPharmaApi";
            string jwtAudience = configuration["JwtSettings:Audience"] ?? "CaresPharmaApp";

            List<SecurityKey> signingKeys = new List<SecurityKey>
            {
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtSecret)),
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("d9f4K!2mQ8v@5xT1zR7y#P0wC3sqscgy")),
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes("d9f4K!2mQ8v@5xT1zR7y#P0wC3sqscgy"))
            };

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = signingKeys,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = false,
                    ClockSkew = TimeSpan.FromMinutes(5)
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        Console.WriteLine($"[JWT Auth Failed]: {context.Exception.Message}");
                        return System.Threading.Tasks.Task.CompletedTask;
                    }
                };
            });

            services.AddAuthorization();
            return services;
        }

        public static IServiceCollection AddCorsPolicyServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddCors(options =>
            {
                options.AddPolicy("InsightsCorsPolicy", policy =>
                {
                    string[] defaultOrigins = new string[] 
                    { 
                        "https://localhost:7189", 
                        "http://localhost:5145", 
                        "http://localhost:5000", 
                        "https://localhost:5001", 
                        "http://localhost:4200",
                        "https://dev-carespharma-insights.netlify.app", 
                        "https://carespharma-insights.netlify.app",
                        "https://carespharma.com",
                        "https://claude.ai",
                        "https://*.claude.ai"
                    };

                    string[]? configOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
                    string[] allowedOrigins = configOrigins != null && configOrigins.Length > 0 ? configOrigins : defaultOrigins;

                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                });
            });

            return services;
        }

        public static IServiceCollection AddPersistenceAndRepositoryServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddMemoryCache();

            // Bind strongly-typed SyncSettings from appsettings.json
            services.Configure<SyncSettings>(
                configuration.GetSection(SyncSettings.SectionName));

            services.AddSingleton<IDbConnectionFactory, AppDbConnectionFactory>();
            services.AddSingleton<IAnalyticsDataRepository, AnalyticsDataRepository>();
            services.AddSingleton<IConversationRepository, ConversationRepository>();
            services.AddTransient<DbInitializer>();
            services.AddHostedService<DataSyncWorker>();
            return services;
        }

        public static IServiceCollection AddSemanticKernelAiServices(this IServiceCollection services, IConfiguration configuration)
        {
            string rawGeminiKey = configuration["Google:ApiKey"] 
                ?? Environment.GetEnvironmentVariable("GOOGLE_AI_API_KEY")
                ?? "demo-key";

            string geminiKey = (rawGeminiKey ?? string.Empty).Trim().Replace("\r", "").Replace("\n", "").Replace("\0", "");

            string rawModelId = configuration["Google:ModelId"] ?? "gemini-3.8-flash";
            string modelId = (rawModelId ?? "gemini-3.8-flash").Trim().Replace("\r", "").Replace("\n", "").Replace("\0", "");

            services.AddScoped<SqlAnalyticsPlugin>();
            services.AddScoped<InsightsPlugin>();

            services.AddScoped<Kernel>(serviceProvider =>
            {
                IKernelBuilder kernelBuilder = Kernel.CreateBuilder();

                kernelBuilder.AddGoogleAIGeminiChatCompletion(
                    modelId: modelId,
                    apiKey: geminiKey
                );

                kernelBuilder.Plugins.AddFromObject(serviceProvider.GetRequiredService<SqlAnalyticsPlugin>(), nameof(SqlAnalyticsPlugin));
                kernelBuilder.Plugins.AddFromObject(serviceProvider.GetRequiredService<InsightsPlugin>(), nameof(InsightsPlugin));

                return kernelBuilder.Build();
            });

            services.AddScoped<IChatOrchestratorService, ChatOrchestratorService>();

            return services;
        }
    }
}

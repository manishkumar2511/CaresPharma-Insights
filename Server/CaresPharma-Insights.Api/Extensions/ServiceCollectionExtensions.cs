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

            byte[] keyBytes = Encoding.UTF8.GetBytes(jwtSecret);

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
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ValidateIssuer = true,
                    ValidIssuer = jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = jwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
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
                    string[] allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
                        ?? new string[] { "http://localhost:5000", "https://localhost:5001", "http://localhost:5173", "https://carespharma-insights.netlify.app" };

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
            string geminiKey = configuration["Google:ApiKey"] 
                ?? Environment.GetEnvironmentVariable("GOOGLE_AI_API_KEY")
                ?? "demo-key";

            services.AddScoped<SqlAnalyticsPlugin>();
            services.AddScoped<InsightsPlugin>();

            services.AddScoped<Kernel>(serviceProvider =>
            {
                IKernelBuilder kernelBuilder = Kernel.CreateBuilder();

                kernelBuilder.AddGoogleAIGeminiChatCompletion(
                    modelId: "gemini-robotics-er-2-preview",
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

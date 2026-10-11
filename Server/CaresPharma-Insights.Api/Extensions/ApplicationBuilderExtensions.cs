using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CaresPharma_Insights.Infrastructure.Data;

namespace CaresPharma_Insights.Api.Extensions
{
    public static class ApplicationBuilderExtensions
    {
        public static WebApplication UseInsightsSwagger(this WebApplication app)
        {
            if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "DevServer")
            {
                app.MapOpenApi();
                app.UseSwagger();
                app.UseSwaggerUI(swaggerOptions =>
                {
                    swaggerOptions.SwaggerEndpoint("/swagger/v1/swagger.json", "CaresPharma Insights API v1");
                    swaggerOptions.RoutePrefix = "swagger";
                });
            }

            return app;
        }

        public static async Task InitializeDatabaseAsync(this WebApplication app)
        {
            using IServiceScope serviceScope = app.Services.CreateScope();
            DbInitializer databaseInitializer = serviceScope.ServiceProvider.GetRequiredService<DbInitializer>();
            await databaseInitializer.InitializeAsync();
        }
    }
}

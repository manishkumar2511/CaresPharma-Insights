using System;
using System.Net.Http;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CaresPharma_Insights.UI;
using CaresPharma_Insights.UI.Services;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// If running in development (localhost), use localhost:5501; otherwise use configured ApiBaseUrl
string configUrl = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;
string baseAddress = builder.HostEnvironment.BaseAddress;

string apiBaseUrl = configUrl;
if (baseAddress.Contains("localhost") || baseAddress.Contains("127.0.0.1"))
{
    apiBaseUrl = "https://localhost:5501/";
}

builder.Services.AddScoped(serviceProvider => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
builder.Services.AddScoped<AuthService>();

WebAssemblyHost host = builder.Build();

// Initialize auth token and profile on startup if present
AuthService authService = host.Services.GetRequiredService<AuthService>();
await authService.InitializeAsync();

await host.RunAsync();

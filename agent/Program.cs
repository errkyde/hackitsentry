using HITSight.Agent;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AgentConfig>(builder.Configuration.GetSection("HITSightAgent"));

builder.Services.AddHttpClient("HITSightServer", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var serverUrl = SecureStore.LoadServerUrl()
        ?? RegistryConfig.GetServerUrl()
        ?? config["HITSightAgent:ServerUrl"]
        ?? "";
    if (!string.IsNullOrWhiteSpace(serverUrl))
        client.BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(sp =>
{
    var ignoreCert = sp.GetRequiredService<IConfiguration>()
        .GetValue<bool>("HITSightAgent:IgnoreCertificateErrors");
    var handler = new HttpClientHandler();
    if (ignoreCert)
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    return handler;
});

// Dedicated client for file downloads — no BaseAddress, no X-Api-Key, long timeout
builder.Services.AddHttpClient("Download", (_, client) =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
}).ConfigurePrimaryHttpMessageHandler(sp =>
{
    var ignoreCert = sp.GetRequiredService<IConfiguration>()
        .GetValue<bool>("HITSightAgent:IgnoreCertificateErrors");
    var handler = new HttpClientHandler();
    if (ignoreCert)
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    return handler;
});

// Separate client with a longer timeout for long-poll requests
builder.Services.AddHttpClient("HITSightServerLongPoll", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var serverUrl = SecureStore.LoadServerUrl()
        ?? RegistryConfig.GetServerUrl()
        ?? config["HITSightAgent:ServerUrl"]
        ?? "";
    if (!string.IsNullOrWhiteSpace(serverUrl))
        client.BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(35);
}).ConfigurePrimaryHttpMessageHandler(sp =>
{
    var ignoreCert = sp.GetRequiredService<IConfiguration>()
        .GetValue<bool>("HITSightAgent:IgnoreCertificateErrors");
    var handler = new HttpClientHandler();
    if (ignoreCert)
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    return handler;
});

builder.Services.AddSingleton<SystemInfoCollector>();
builder.Services.AddSingleton<LicenseCollector>();
builder.Services.AddSingleton<AgentHttpClient>();
builder.Services.AddHostedService<SightAgent>();

// Run as Windows Service when not in development
builder.Services.AddWindowsService();

var host = builder.Build();
await host.RunAsync();

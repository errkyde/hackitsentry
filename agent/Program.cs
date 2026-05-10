using HackITSentry.Agent;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AgentConfig>(builder.Configuration.GetSection("SentryAgent"));

builder.Services.AddHttpClient("SentryServer", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var serverUrl = SecureStore.LoadServerUrl()
        ?? RegistryConfig.GetServerUrl()
        ?? config["SentryAgent:ServerUrl"]
        ?? "";
    if (!string.IsNullOrWhiteSpace(serverUrl))
        client.BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(sp =>
{
    var ignoreCert = sp.GetRequiredService<IConfiguration>()
        .GetValue<bool>("SentryAgent:IgnoreCertificateErrors");
    var handler = new HttpClientHandler();
    if (ignoreCert)
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    return handler;
});

// Separate client with a longer timeout for long-poll requests
builder.Services.AddHttpClient("SentryServerLongPoll", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var serverUrl = SecureStore.LoadServerUrl()
        ?? RegistryConfig.GetServerUrl()
        ?? config["SentryAgent:ServerUrl"]
        ?? "";
    if (!string.IsNullOrWhiteSpace(serverUrl))
        client.BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(35);
}).ConfigurePrimaryHttpMessageHandler(sp =>
{
    var ignoreCert = sp.GetRequiredService<IConfiguration>()
        .GetValue<bool>("SentryAgent:IgnoreCertificateErrors");
    var handler = new HttpClientHandler();
    if (ignoreCert)
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    return handler;
});

builder.Services.AddSingleton<SystemInfoCollector>();
builder.Services.AddSingleton<LicenseCollector>();
builder.Services.AddSingleton<AgentHttpClient>();
builder.Services.AddHostedService<SentryAgent>();

// Run as Windows Service when not in development
builder.Services.AddWindowsService();

var host = builder.Build();
await host.RunAsync();

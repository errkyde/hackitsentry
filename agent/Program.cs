using HackITSentry.Agent;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AgentConfig>(builder.Configuration.GetSection("SentryAgent"));

builder.Services.AddHttpClient("SentryServer", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var serverUrl = config["SentryAgent:ServerUrl"]!;
    client.BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton<SystemInfoCollector>();
builder.Services.AddSingleton<LicenseCollector>();
builder.Services.AddSingleton<AgentHttpClient>();
builder.Services.AddHostedService<SentryAgent>();

// Run as Windows Service when not in development
if (WindowsServiceHelpers.IsWindowsService())
    builder.Host.UseWindowsService();

var host = builder.Build();
await host.RunAsync();

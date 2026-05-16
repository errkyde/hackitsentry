using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace HITSight.Agent;

public class AgentHttpClient
{
    private readonly IHttpClientFactory _factory;
    private readonly IOptionsMonitor<AgentConfig> _config;
    private readonly ILogger<AgentHttpClient> _logger;

    public AgentHttpClient(
        IHttpClientFactory factory,
        IOptionsMonitor<AgentConfig> config,
        ILogger<AgentHttpClient> logger)
    {
        _factory = factory;
        _config = config;
        _logger = logger;
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient("HITSightServer");
        var apiKey = SecureStore.LoadApiKey();
        if (!string.IsNullOrEmpty(apiKey))
        {
            client.DefaultRequestHeaders.Remove("X-Api-Key");
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }
        return client;
    }

    public async Task<RegisterResponse?> RegisterAsync(object payload)
    {
        try
        {
            var client = _factory.CreateClient("HITSightServer");
            var response = await client.PostAsJsonAsync("api/agent/register", payload);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<RegisterResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed");
            return null;
        }
    }

    public async Task<RegistrationStatusResponse?> GetRegistrationStatusAsync(string token)
    {
        try
        {
            var client = _factory.CreateClient("HITSightServer");
            var response = await client.GetAsync($"api/agent/register/{token}/status");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new RegistrationStatusResponse("NotFound", null);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<RegistrationStatusResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Status check failed");
            return null;
        }
    }

    public async Task<CheckinResponse?> CheckinAsync(object payload)
    {
        try
        {
            var client = CreateClient();
            var response = await client.PostAsJsonAsync("api/agent/checkin", payload);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Check-in rejected with 401 – API key invalid or revoked. Clearing credentials.");
                SecureStore.Delete();
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<CheckinResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Checkin failed");
            return null;
        }
    }

    public async Task SubmitLicenseKeyAsync(object payload)
    {
        try
        {
            var client = CreateClient();
            var response = await client.PostAsJsonAsync("api/agent/request-key", payload);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "License key submission failed");
        }
    }

    public async Task<List<PendingCommandDto>?> GetPendingCommandsAsync()
    {
        try
        {
            var client = CreateClient();
            var response = await client.GetAsync("api/agent/commands/pending");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<PendingCommandDto>>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch pending commands");
            return null;
        }
    }

    public async Task ReportCommandResultAsync(Guid commandId, bool success, string? message)
    {
        try
        {
            var client = CreateClient();
            var response = await client.PostAsJsonAsync(
                $"api/agent/commands/{commandId}/result",
                new { success, message });
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report command result for {CommandId}", commandId);
        }
    }

    public async Task UninstallAsync()
    {
        try
        {
            var client = CreateClient();
            var response = await client.PostAsync("api/agent/uninstall", null);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Uninstall notification failed");
        }
    }

    /// <summary>
    /// Long-polls the server for up to 29 seconds. Returns when a command is signalled
    /// or the server times out. Always returns immediately — caller should then fetch pending commands.
    /// </summary>
    public async Task WaitForCommandAsync(CancellationToken ct)
    {
        try
        {
            var client = _factory.CreateClient("HITSightServerLongPoll");
            var apiKey = SecureStore.LoadApiKey();
            if (!string.IsNullOrEmpty(apiKey))
            {
                client.DefaultRequestHeaders.Remove("X-Api-Key");
                client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            }
            await client.GetAsync("api/agent/commands/wait", ct);
        }
        catch (OperationCanceledException) { /* stoppingToken fired — normal */ }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Long poll interrupted (server unreachable or restarting)");
            // Brief pause before reconnect to avoid hammering an unreachable server
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }
    }

    public async Task<byte[]?> DownloadFileAsync(string url)
    {
        try
        {
            using var client = new HttpClient();
            return await client.GetByteArrayAsync(url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file from {Url}", url);
            return null;
        }
    }
}

public record RegisterResponse(string Status, Guid Id);
public record RegistrationStatusResponse(string Status, string? ApiKey);
public record CheckinResponse(
    bool LicenseRequested,
    bool HasPendingCommands,
    string? LatestAgentVersion,
    string? AgentDownloadUrl,
    string? RustDeskRelayServer = null,
    string? RustDeskPublicKey = null,
    bool RustDeskAutoInstall = false,
    string? RustDeskDownloadUrl = null,
    int? CheckinIntervalMinutes = null,
    Dictionary<string, string>? RustDeskDeviceOptions = null,
    int? RustDeskForceApplyVersion = null
);
public record PendingCommandDto(Guid Id, string CommandType, string? Parameters);

using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace HackITSentry.Agent;

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
        var client = _factory.CreateClient("SentryServer");
        var apiKey = _config.CurrentValue.ApiKey;
        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Remove("X-Api-Key");
        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        return client;
    }

    public async Task<RegisterResponse?> RegisterAsync(object payload)
    {
        try
        {
            var client = _factory.CreateClient("SentryServer");
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
            var client = _factory.CreateClient("SentryServer");
            var response = await client.GetAsync($"api/agent/register/{token}/status");
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
}

public record RegisterResponse(string Status, Guid Id);
public record RegistrationStatusResponse(string Status, string? ApiKey);
public record CheckinResponse(bool LicenseRequested);

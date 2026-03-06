namespace HackITSentry.Agent;

public class AgentConfig
{
    public string ServerUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int CheckinIntervalMinutes { get; set; } = 30;
}

namespace HITSight.Agent;

public class AgentConfig
{
    public string ServerUrl { get; set; } = "";
    public string InstallToken { get; set; } = "";
    public int CheckinIntervalMinutes { get; set; } = 15;
    public bool IgnoreCertificateErrors { get; set; } = false;
}

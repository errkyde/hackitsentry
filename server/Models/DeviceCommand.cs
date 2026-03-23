namespace HackITSentry.Server.Models;

public enum CommandType
{
    Restart,
    Shutdown,
    RunScript,
    ForceCheckin,
    Uninstall,
    UpdateServerUrl,
    CollectLicense,
    InitRustDesk
}

public enum CommandStatus
{
    Pending,
    Sent,
    Executed,
    Failed
}

public class DeviceCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public CommandType CommandType { get; set; }
    public string? Parameters { get; set; }
    public CommandStatus Status { get; set; } = CommandStatus.Pending;
    public string IssuedByUsername { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExecutedAt { get; set; }
    public string? Result { get; set; }
}

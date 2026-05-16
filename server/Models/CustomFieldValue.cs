namespace HITSight.Server.Models;

public class CustomFieldValue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DefinitionId { get; set; }
    public CustomFieldDefinition Definition { get; set; } = null!;
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public string Value { get; set; } = "";
}

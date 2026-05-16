namespace HITSight.Server.Models;

public class CustomFieldDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int SortOrder { get; set; } = 0;
    public ICollection<CustomFieldValue> Values { get; set; } = [];
}

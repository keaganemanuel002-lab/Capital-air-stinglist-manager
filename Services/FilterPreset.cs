namespace StingListManager.Services;

public class FilterPreset
{
    public string Name { get; set; } = "";
    public bool ShowArchived { get; set; }
    public string? CompanyContains { get; set; }

    public override string ToString() => Name;
}

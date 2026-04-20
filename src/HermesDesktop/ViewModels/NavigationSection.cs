namespace HermesDesktop.ViewModels;

public enum NavigationSection
{
    Connections,
    Overview,
    Files,
    Sessions,
    Usage,
    Skills,
    CronJobs,
    Terminal
}

public class NavigationItem
{
    public NavigationSection Section { get; init; }
    public string Label { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = string.Empty;
    public bool RequiresConnection { get; init; }
}

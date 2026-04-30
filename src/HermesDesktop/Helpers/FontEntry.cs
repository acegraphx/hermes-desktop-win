using System.Windows.Media;

namespace HermesDesktop.Helpers;

public enum FontSource
{
    System,
    Bundled,
    Downloadable,
    Downloaded
}

public sealed record FontEntry(
    string Family,
    string DisplayName,
    FontSource Source,
    FontFamily WpfFamily,
    string? FileName,
    string? CatalogId,
    bool IsInstalled);

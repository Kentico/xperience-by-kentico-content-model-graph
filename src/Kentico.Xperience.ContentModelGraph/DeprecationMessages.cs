namespace Kentico.Xperience.ContentModelGraph;

internal static class DeprecationMessages
{
    public const string PackageRenamed =
        "Kentico.Xperience.ContentModelGraph packages are deprecated. Use Kentico.Xperience.Labs.ContentModelGraph packages (namespace Kentico.Xperience.Labs.ContentModelGraph). See https://github.com/Kentico/xperience-by-kentico-content-model-graph";
    public const string DiagnosticId = "KXCR0001";

#pragma warning disable S1075 // URI is intentionally fixed
    public const string Url = "https://github.com/Kentico/xperience-by-kentico-content-model-graph";
#pragma warning restore S1075
}

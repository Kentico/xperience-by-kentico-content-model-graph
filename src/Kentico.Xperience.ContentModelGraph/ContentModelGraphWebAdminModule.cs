using Kentico.Xperience.Admin.Base;

[assembly: CMS.AssemblyDiscoverable]
[assembly: CMS.RegisterModule(typeof(Kentico.Xperience.ContentModelGraph.ContentModelGraphWebAdminModule))]

namespace Kentico.Xperience.ContentModelGraph;

internal sealed class ContentModelGraphWebAdminModule : AdminModule
{
    public ContentModelGraphWebAdminModule()
        : base("Kentico.Xperience.ContentModelGraph.Admin")
    {
    }

    protected override void OnInit()
    {
        base.OnInit();

        RegisterClientModule("kentico", "xperience-content-model-graph");
    }
}

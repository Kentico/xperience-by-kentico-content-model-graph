using Kentico.Xperience.Admin.Base;

[assembly: CMS.AssemblyDiscoverable]
[assembly: CMS.RegisterModule(typeof(Kentico.Xperience.Labs.ContentModelGraph.ContentModelGraphWebAdminModule))]

namespace Kentico.Xperience.Labs.ContentModelGraph;

internal sealed class ContentModelGraphWebAdminModule : AdminModule
{
    public ContentModelGraphWebAdminModule()
        : base("Kentico.Xperience.Labs.ContentModelGraph.Admin")
    {
    }

    protected override void OnInit()
    {
        base.OnInit();

        RegisterClientModule("kentico", "xperience-labs-content-model-graph");
    }
}

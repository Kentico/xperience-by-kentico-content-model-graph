using Kentico.Xperience.Admin.Base;

[assembly: CMS.AssemblyDiscoverable]
[assembly: CMS.RegisterModule(typeof(Kentico.Xperience.Labs.ContentModelGraph.Admin.ContentModelGraphWebAdminModule))]

namespace Kentico.Xperience.Labs.ContentModelGraph.Admin;

internal sealed class ContentModelGraphWebAdminModule : AdminModule
{
    public ContentModelGraphWebAdminModule()
        : base("Kentico.Xperience.ContentModelGraph.Admin")
    {
    }

    protected override void OnInit()
    {
        base.OnInit();

        RegisterClientModule("kentico", "xperience-labs-content-model-graph-admin");
    }
}

# Usage Guide

## Setup

Add the package to the Xperience ASP.NET Core application:

```powershell
dotnet add package Kentico.Xperience.ContentModelGraph --version 1.0.0-prerelease-1
```

Register the library services in `Program.cs` before `builder.Build()`:

```csharp
using Kentico.Xperience.ContentModelGraph;

builder.Services.AddContentModelGraph();
```

The referenced assembly registers the administration application and embedded client assets automatically. The application appears as **Content model graph** in the **Development** category. The category is only where it is listed - opening it needs the application's **View** permission, granted to a role in _Role management_, which administrators have implicitly.

## Graph Data

The graph reads Xperience metadata for content types, reusable field schemas, taxonomies, forms, and object types. It displays these relationship types:

- Reusable schema assignments
- Content item references constrained by content type
- Content item references constrained by reusable schema
- Object type references
- Taxonomy references

System object types are hidden by default. Enable the **System object types** filter to include them, and use the system group sub-filters to narrow which ones appear.

## Controls

The left toolbar changes layout direction, toggles field labels, fits the graph to the viewport, resets client-side graph state, exports JSON, and clears the server cache.

The filters panel in the top right corner is collapsed by default. Expanded, it holds the name search and the node and relationship type filters. Search dims nodes whose display name and code name do not match the entered text. A minimap toggle sits in the bottom right corner.

Graph data is cached for 60 minutes. **Clear cache** invalidates the shared cache key and rebuilds the graph. **Reset graph** reloads the current cached graph and restores default client filters.

## Contextual Graphs

Content types, reusable field schemas, and taxonomies each have their own **Content model graph** tab. The tab shows the same graph filtered to the selected object and the nodes it is directly connected to, with the selected node marked as the current item. Field names are shown by default there.

Forms have no such tab - a form takes part in no schema-level relationships - and use the **Content relationships** page described below instead.

## Content Item Relationships

Open a content item and select **Content relationships** to inspect item-level references for the current language. The page is available for reusable content items in the Content hub, web pages, emails, headless items, and forms.

The page draws the current item, the items that reference it, and the items it references. Every content item node can be expanded in either direction to walk further out, and the canvas keeps what has been expanded. The toolbar changes layout direction, hides or shows reference labels, fits the view, resets back to the initial one-hop graph, and exports the current canvas as JSON - including the language the graph was requested in and the language each item was actually read in. The search box highlights nodes by name, and a minimap toggle sits in the bottom right corner.

Relationships are read from:

- Content item reference fields on each item's latest version. When multiple language variants contain the same incoming reference, the current language is preferred.
- Page Builder configuration - widget, section, and page template properties - including personalization variants, which are otherwise invisible until that variant is selected in the builder. Edge labels name the source kind (`Widget:`, `Section:`, `Template:`) and the variant; the label tooltip shows the full Page Builder path. Each reference is placed by the field or builder property Xperience recorded it for, so references from values the graph cannot parse - rich text links, or custom components with their own reference extractor - are labelled too. Email Builder configuration is read the same way.
- Taxonomy fields. Selected tags are drawn as terminal nodes that link to the tag in the administration.
- Forms embedded by the Form Widget. Forms are drawn as terminal nodes that link to the form.

A form's own **Content relationships** page reverses the last of these: it shows the content items whose Page Builder configuration embeds the form, read in the default content language. The graph is capped, and reports the total when it shows only part of it. Forms rendered directly from view code, or referenced by submission notifications, campaigns, and automation processes, are not tracked and do not appear.

Items deleted after the graph was drawn render as **missing** nodes: the metadata already loaded for them is kept so the broken reference stays visible, but they cannot be expanded.

Each item in the graph is checked against the surface it lives on: reusable items use the Content hub workspace `View` permission, web pages use the website channel application permissions together with the web page `Read` ACL, and emails and headless items use their channel application `View` permission. Administrators see everything. Items the current user cannot view are still drawn, so the relationship stays visible, but they offer no links and cannot be expanded. Display names prefer the current language and fall back to another available display name, then the item code name.

## Client Development

Install dependencies and run the development server:

```powershell
npm install --prefix .\src\Kentico.Xperience.ContentModelGraph\Client
npm run start --prefix .\src\Kentico.Xperience.ContentModelGraph\Client
```

Configure the host application's user secrets:

```json
{
  "CMSAdminClientModuleSettings": {
    "kentico-xperience-content-model-graph": {
      "Mode": "Proxy",
      "Port": 3009
    }
  }
}
```

For normal builds and deployments, use embedded mode:

```json
{
  "CMSAdminClientModuleSettings": {
    "kentico-xperience-content-model-graph": {
      "Mode": "Embedded"
    }
  }
}
```

Build the production client before building or packing the library:

```powershell
npm run build --prefix .\src\Kentico.Xperience.ContentModelGraph\Client
dotnet build .\Kentico.Xperience.ContentModelGraph.slnx
```

# Usage Guide

## Setup

Add the package to the Xperience ASP.NET Core application:

```powershell
dotnet add package Kentico.Xperience.Labs.ContentModelGraph --version 1.0.0-prerelease-1
```

Register the library services in `Program.cs` before `builder.Build()`:

```csharp
using Kentico.Xperience.Labs.ContentModelGraph;

builder.Services.AddContentModelGraph();
```

The referenced assembly registers the administration application and embedded client assets automatically. The application appears as **Content model graph** in the **Development** category.

## Graph Data

The graph reads Xperience metadata for content types, reusable field schemas, taxonomies, forms, and object types. It displays these relationship types:

- Reusable schema assignments
- Content item references constrained by content type
- Content item references constrained by reusable schema
- Object type references
- Taxonomy references

System object types are hidden by default. Enable the **System object types** filter and select the relevant system groups to include them.

## Controls

The left toolbar changes layout direction, toggles field labels, fits the graph to the viewport, resets client-side graph state, exports JSON, and clears the server cache.

The right panel filters node and relationship types. Search dims nodes whose display name and code name do not match the entered text.

Graph data is cached for 60 minutes. **Clear cache** invalidates the shared cache key and rebuilds the graph. **Reset graph** reloads the current cached graph and restores default client filters.

## Client Development

Install dependencies and run the development server:

```powershell
npm install --prefix .\src\Kentico.Xperience.Labs.ContentModelGraph\Client
npm run start --prefix .\src\Kentico.Xperience.Labs.ContentModelGraph\Client
```

Configure the host application's user secrets:

```json
{
  "CMSAdminClientModuleSettings": {
    "kentico-xperience-labs-content-model-graph": {
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
    "kentico-xperience-labs-content-model-graph": {
      "Mode": "Embedded"
    }
  }
}
```

Build the production client before building or packing the library:

```powershell
npm run build --prefix .\src\Kentico.Xperience.Labs.ContentModelGraph\Client
dotnet build .\Kentico.Xperience.Labs.ContentModelGraph.slnx
```

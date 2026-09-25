# Xperience by Kentico: Content Model Graph

[![Kentico Labs](https://img.shields.io/badge/Kentico_Labs-grey?labelColor=orange)](https://github.com/Kentico)
[![CI](https://github.com/Kentico/xperience-by-kentico-content-model-graph/actions/workflows/ci.yml/badge.svg)](https://github.com/Kentico/xperience-by-kentico-content-model-graph/actions/workflows/ci.yml)

## Description

Content Model Graph adds an interactive application to the Xperience by Kentico administration. It visualizes relationships among:

- Page, reusable, email, and headless content types
- Reusable field schemas
- Taxonomies and forms
- Custom and system object types

The graph supports filtering, search highlighting, horizontal and vertical layouts, field-name labels, JSON export, a minimap, and cache refresh.

A **Content model graph** tab is also added to content types, reusable field schemas, and taxonomies. Each tab shows the same graph filtered to that object and its immediate neighbors.

The package also adds a **Content relationships** page to reusable content items, web pages, emails, headless items, and forms. This page draws the current item together with the items that reference it and the items it references, and each node can be expanded to walk further out. It surfaces references stored in Page Builder widget, section, and page template properties - including personalization variants, which are otherwise invisible until the variant is selected in the builder - as well as taxonomy tags and forms embedded through the Form Widget. A form's page reverses the relationship and shows the content items that embed it.

The graph is generated from Xperience metadata. It does not inspect application code or external data sources. Content model graph data is cached for 60 minutes and can be refreshed from the application toolbar.

### Screenshots

<a href="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/dancing-goat-content-model-graph.jpg">
  <img src="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/dancing-goat-content-model-graph.jpg" width="800" alt="Dancing Goat content model graph in the Xperience administration">
</a>

<a href="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/xperience-by-kentico-commerce-model-graph.jpg">
  <img src="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/xperience-by-kentico-commerce-model-graph.jpg" width="800" alt="Xperience by Kentico commerce model graph in the Xperience administration">
</a>

<a href="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/dancing-goat-taxonomy-content-model-graph.jpg">
  <img src="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/dancing-goat-taxonomy-content-model-graph.jpg" width="800" alt="Content model graph tab of a Dancing Goat taxonomy, filtered to the content types that reference it">
</a>

<a href="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/dancing-goat-content-item-graph.jpg">
  <img src="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/dancing-goat-content-item-graph.jpg" width="800" alt="Content relationships page for a Dancing Goat content item in the Xperience administration">
</a>

<a href="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/dancing-goat-email-item-graph.jpg">
  <img src="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/dancing-goat-email-item-graph.jpg" width="800" alt="Content relationships page for a Dancing Goat email channel item in the Xperience administration">
</a>

### Videos

Watch and see how you can use this library to explore the entire data model of your Xperience by Kentico project.

<a href="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/xperience-by-kentico-labs-content-model-graph-exploration.webm">
  <img src="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/xperience-by-kentico-labs-content-model-graph-exploration.png" width="800" alt="Content Model Graph exploration demonstration">
</a>

## Requirements

| Xperience version | Library version    |
| ----------------- | ------------------ |
| >= 31.7.3         | 1.0.0-prerelease-1 |

- .NET 10.0 or newer
- Xperience by Kentico 31.7.3 or newer

## Package Installation

```powershell
dotnet add package Kentico.Xperience.ContentModelGraph --version 1.0.0-prerelease-1
```

## Quick Start

Register the graph builder before building the application:

```csharp
using Kentico.Xperience.ContentModelGraph;

builder.Services.AddContentModelGraph();
```

The package registers the **Content model graph** application and its embedded Admin client module automatically. Grant a role the **View** permission for the **Content model graph** application in _Role management_ to let its members open it; administrators always can.

## Full Instructions

See the [Usage Guide](./docs/Usage-Guide.md) for graph behavior and client development setup.

## Contributing

See [Contributing Setup](./docs/Contributing-Setup.md) and Kentico's [contribution guidelines](https://github.com/Kentico/.github/blob/main/CONTRIBUTING.md).

## License

Distributed under the MIT License. See [LICENSE.md](./LICENSE.md).

## Support

This project has Kentico Labs limited support. See [SUPPORT.md](https://github.com/Kentico/.github/blob/main/SUPPORT.md#labs-limited-support).

For security issues, see [SECURITY.md](https://github.com/Kentico/.github/blob/main/SECURITY.md).

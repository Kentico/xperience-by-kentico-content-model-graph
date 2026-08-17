# Xperience by Kentico: Content Model Graph

[![Kentico Labs](https://img.shields.io/badge/Kentico_Labs-grey?labelColor=orange)](https://github.com/Kentico)
[![CI](https://github.com/Kentico/xperience-by-kentico-content-model-graph/actions/workflows/ci.yml/badge.svg)](https://github.com/Kentico/xperience-by-kentico-content-model-graph/actions/workflows/ci.yml)

## Description

Content Model Graph adds an interactive application to the Xperience by Kentico administration. It visualizes relationships among:

- Page, reusable, email, and headless content types
- Reusable field schemas
- Taxonomies and forms
- Custom and system object types

The graph supports filtering, search highlighting, horizontal and vertical layouts, field-name labels, JSON export, and cache refresh.

The graph is generated from Xperience metadata. It does not inspect application code or external data sources. Graph data is cached for 60 minutes and can be refreshed from the application toolbar.

### Screenshots

<a href="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/dancing-goat-content-model-graph.jpg">
  <img src="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/dancing-goat-content-model-graph.jpg" width="800" alt="Dancing Goat content model graph in the Xperience administration">
</a>

<a href="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/xperience-by-kentico-commerce-model-graph.jpg">
  <img src="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/xperience-by-kentico-commerce-model-graph.jpg" width="800" alt="Xperience by Kentico commerce model graph in the Xperience administration">
</a>

### Videos

Watch and see how you can use this library to explore the entire data model of your Xperience by Kentico project.

<a href="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/xperience-by-kentico-content-model-graph-exploration.webm">
  <img src="https://raw.githubusercontent.com/Kentico/xperience-by-kentico-content-model-graph/refs/heads/main/docs/images/xperience-by-kentico-content-model-graph-exploration.png" width="800" alt="Content Model Graph exploration demonstration">
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

The package registers the **Content model graph** application and its embedded Admin client module automatically. Users need access to applications in the **Development** category.

## Full Instructions

See the [Usage Guide](./docs/Usage-Guide.md) for graph behavior and client development setup.

## Contributing

See [Contributing Setup](./docs/Contributing-Setup.md) and Kentico's [contribution guidelines](https://github.com/Kentico/.github/blob/main/CONTRIBUTING.md).

## License

Distributed under the MIT License. See [LICENSE.md](./LICENSE.md).

## Support

This project has Kentico Labs limited support. See [SUPPORT.md](https://github.com/Kentico/.github/blob/main/SUPPORT.md#labs-limited-support).

For security issues, see [SECURITY.md](https://github.com/Kentico/.github/blob/main/SECURITY.md).

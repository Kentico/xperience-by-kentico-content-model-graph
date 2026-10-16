# Contributing Setup

## Required Software

- .NET SDK 10.0 or newer; see `global.json`
- Node.js 24 or newer
- SQL Server 2019 or newer
- Visual Studio, VS Code, Cursor, or Rider

## Restore and Build

```powershell
npm install --prefix .\src\Kentico.Xperience.Labs.ContentModelGraph.Admin\Client
npm run build --prefix .\src\Kentico.Xperience.Labs.ContentModelGraph.Admin\Client
dotnet restore .\Kentico.Xperience.Labs.ContentModelGraph.slnx
dotnet build .\Kentico.Xperience.Labs.ContentModelGraph.slnx --no-restore
dotnet test .\Kentico.Xperience.Labs.ContentModelGraph.slnx --no-build --no-restore
```

## DancingGoat

Create an Xperience database for `examples/DancingGoat` by following the [Xperience installation documentation](https://docs.kentico.com/documentation/developers-and-admins/installation).

The example references the library project, registers `AddContentModelGraph()`, and uses the embedded client bundle by default for production builds and proxy mode for development.

Start both processes:

```powershell
npm run start --prefix .\src\Kentico.Xperience.Labs.ContentModelGraph.Admin\Client
dotnet watch run --project .\examples\DancingGoat\DancingGoat.csproj
```

Open the administration at the URL configured in `examples/DancingGoat/Properties/launchSettings.json`, then select **Content model graph (Labs)** from the **Development** category.

## MCP Servers

`.mcp.json` at the repository root registers two Kentico MCP servers for agent-assisted development. Agents should verify behavior against the official documentation and a running instance instead of inspecting compiled assemblies.

| Server                 | Transport | Source                                                                                                            |
| ---------------------- | --------- | ----------------------------------------------------------------------------------------------------------------- |
| `kentico-docs-mcp`     | HTTP      | [Documentation MCP server](https://docs.kentico.com/documentation/developers-and-admins/installation/mcp-server)     |
| `xperience-management-mcp` | stdio | [`@kentico/management-api-mcp`](https://www.npmjs.com/package/@kentico/management-api-mcp) (requires Node.js 22 or newer) |

`kentico-docs-mcp` needs no configuration. If the same server is already registered at the user or CLI level, remove one of the registrations; duplicate instances double token usage.

`xperience-management-mcp` connects to the management API of a locally running Xperience application. The configured URL matches the Dancing Goat port in `examples/DancingGoat/appsettings.Development.json`. Change it in `.mcp.json` if the example runs elsewhere.

The management API is configured by `examples/DancingGoat/ManagementApiConfiguration.cs` per [Configure the Management MCP server](https://docs.kentico.com/documentation/developers-and-admins/api/management-api/configure-management-mcp-server): it registers `AddKenticoManagementApi()` and `UseKenticoManagementApi()` only for local development.

Both sides share a throwaway secret committed to the repository: `ManagementApi:Secret` in `examples/DancingGoat/appsettings.Development.json`, and `MANAGEMENT_API_SECRET` in `.mcp.json`. This is deliberate — the example app holds no data worth protecting and the API is reachable only from localhost. **Change both values if you ever expose the app beyond your machine**, and never use this pattern in a real project.

Dancing Goat enables the API only when the environment is Development and `ManagementApi:Port` is configured; otherwise the app starts as usual and the endpoints do not exist. Never enable it outside local development.

Run the app, then point your agent at it:

```powershell
dotnet watch run --project .\examples\DancingGoat\DancingGoat.csproj
```

Verify by requesting `http://localhost:6893/kentico-api/management/v1/openapi.json` while the app runs; a 404 means the API is not enabled. The two secrets must match, so change them together. The Management MCP package uses the version specified by the generated template configuration; Xperience NuGet and admin npm dependencies are pinned to 31.9.2.

## Formatting

```powershell
dotnet format .\Kentico.Xperience.Labs.ContentModelGraph.slnx --exclude .\examples\**
```

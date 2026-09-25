# Contributing Setup

## Required Software

- .NET SDK 10.0 or newer; see `global.json`
- Node.js 24 or newer
- SQL Server 2019 or newer
- Visual Studio, VS Code, Cursor, or Rider

## Restore and Build

```powershell
npm install --prefix .\src\Kentico.Xperience.ContentModelGraph\Client
npm run build --prefix .\src\Kentico.Xperience.ContentModelGraph\Client
dotnet restore .\Kentico.Xperience.ContentModelGraph.slnx
dotnet build .\Kentico.Xperience.ContentModelGraph.slnx --no-restore
dotnet test .\Kentico.Xperience.ContentModelGraph.slnx --no-build --no-restore
```

## DancingGoat

Create an Xperience database for `examples/DancingGoat` by following the [Xperience installation documentation](https://docs.kentico.com/documentation/developers-and-admins/installation).

The example references the library project, registers `AddContentModelGraph()`, and uses the embedded client bundle by default for production builds and proxy mode for development.

Start both processes:

```powershell
npm run start --prefix .\src\Kentico.Xperience.ContentModelGraph\Client
dotnet watch run --project .\examples\DancingGoat\DancingGoat.csproj
```

Open the administration at the URL configured in `examples/DancingGoat/Properties/launchSettings.json`, then select **Content model graph** from the **Development** category.

## MCP Servers

`.mcp.json` at the repository root registers two Kentico MCP servers for agent-assisted development. Agents should verify behavior against the official documentation and a running instance instead of inspecting compiled assemblies.

| Server                 | Transport | Source                                                                                                            |
| ---------------------- | --------- | ----------------------------------------------------------------------------------------------------------------- |
| `kentico-docs`         | HTTP      | [Documentation MCP server](https://docs.kentico.com/documentation/developers-and-admins/installation/mcp-server)     |
| `xperience-management` | stdio     | [`@kentico/management-api-mcp`](https://www.npmjs.com/package/@kentico/management-api-mcp) (requires Node.js 22 or newer) |

`kentico-docs` needs no configuration. If the same server is already registered at the user or CLI level, remove one of the registrations; duplicate instances double token usage.

`xperience-management` connects to the management API of a locally running Xperience application. The configured URL matches the DancingGoat port in `examples/DancingGoat/Properties/launchSettings.json`. Change it in `.mcp.json` if the example runs elsewhere.

The management API is wired up in `examples/DancingGoat/Program.cs` per [Configure the Management MCP server](https://docs.kentico.com/documentation/developers-and-admins/api/management-api/configure-management-mcp-server): the `Kentico.Xperience.ManagementApi` package, `AddKenticoManagementApi()`, and `UseKenticoManagementApi()` between `UseAuthentication()` and `UseKentico()`.

Both sides share a throwaway secret committed to the repository: `XPERIENCE_MANAGEMENT_API_SECRET` in `examples/DancingGoat/appsettings.Development.json`, and `MANAGEMENT_API_SECRET` in `.mcp.json`. This is deliberate — the example app holds no data worth protecting and the API is reachable only from localhost. **Change both values if you ever expose the app beyond your machine**, and never use this pattern in a real project.

DancingGoat enables the API only when the environment is Development **and** the secret holds at least 32 characters; otherwise the app starts as usual and the endpoints do not exist. Never enable it outside local development.

Run the app, then point your agent at it:

```powershell
dotnet watch run --project .\examples\DancingGoat\DancingGoat.csproj
```

Verify by requesting `http://localhost:19794/kentico-api/management/v1/openapi.json` while the app runs; a 404 means the secret is missing or too short. The two values must match, so change them together.

The npm package is pinned to `@kentico/management-api-mcp@31.7.3-preview` to match the Xperience version in `Directory.Packages.props`. `Kentico.Xperience.ManagementApi` has no stable 31.7.3 release, so the NuGet pin is `31.7.3-preview` as well; it depends on `Kentico.Xperience.Admin` 31.7.3. Update both pins together with the rest of the Xperience packages.

## Formatting

```powershell
dotnet format .\Kentico.Xperience.ContentModelGraph.slnx --exclude .\examples\**
```

# Contributing Setup

## Required Software

- .NET SDK 10.0 or newer; see `global.json`
- Node.js 24 or newer
- SQL Server 2019 or newer
- Visual Studio, VS Code, Cursor, or Rider

## Restore and Build

```powershell
npm install --prefix .\src\Kentico.Xperience.Labs.ContentModelGraph\Client
npm run build --prefix .\src\Kentico.Xperience.Labs.ContentModelGraph\Client
dotnet restore .\Kentico.Xperience.Labs.ContentModelGraph.slnx
dotnet build .\Kentico.Xperience.Labs.ContentModelGraph.slnx --no-restore
dotnet test .\Kentico.Xperience.Labs.ContentModelGraph.slnx --no-build --no-restore
```

## DancingGoat

Create an Xperience database for `examples/DancingGoat` by following the [Xperience installation documentation](https://docs.kentico.com/documentation/developers-and-admins/installation).

The example references the library project, registers `AddContentModelGraph()`, and uses the embedded client bundle by default for production builds and proxy mode for development.

Start both processes:

```powershell
npm run start --prefix .\src\Kentico.Xperience.Labs.ContentModelGraph\Client
dotnet watch run --project .\examples\DancingGoat\DancingGoat.csproj
```

Open the administration at the URL configured in `examples/DancingGoat/Properties/launchSettings.json`, then select **Content model graph** from the **Development** category.

## Formatting

```powershell
dotnet format .\Kentico.Xperience.Labs.ContentModelGraph.slnx --exclude .\examples\**
```

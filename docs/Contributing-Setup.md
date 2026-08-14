# Contributing Setup

---This documents the steps a maintainer or developer would follow to work on the library in their development environment---

---Update the details for this project, replacing "repotemplate" and anything else that needs changed---

## Required Software

The requirements to setup, develop, and build this project are listed below.

### .NET Runtime

.NET SDK 10.0 or newer

- <https://dotnet.microsoft.com/en-us/download/dotnet/10.0>
- See `global.json` file for specific SDK requirements

### Node.js Runtime

- [Node.js](https://nodejs.org/en/download) v24 or newer
- [NVM for Windows](https://github.com/coreybutler/nvm-windows) to manage multiple installed versions of Node.js
- See `engines` in the solution `package.json` for specific version requirements

### C# Editor

- VS Code/VS
- Cursor
- Rider

### Database

SQL Server 2019 or newer compatible database

- [SQL Server Linux](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-setup?view=sql-server-ver15)

### SQL Editor

- VS Code with official [MSSQL extension](https://marketplace.visualstudio.com/items?itemName=ms-mssql.mssql)
- MS SQL Server Management Studio

## Sample Project

### Database Setup

Running the sample project requires creating a new Xperience by Kentico database using the included template.

Change directory in your console to `./examples/DancingGoat` and follow the instructions in the Xperience
documentation on [creating a new database](https://docs.kentico.com/documentation/developers-and-admins/installation#create-the-project-database).

### Admin Customization

To run the Sample app Admin customization in development mode, add the following to your [User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0&tabs=windows#secret-manager) for the application.

```json
"CMSAdminClientModuleSettings": {
  "kentico-xperience-integrations-repotemplate": {
    "Mode": "Proxy",
    "Port": 3009
  }
}
```

## Development Workflow

1. Create a new branch with one of the following prefixes
   - `feat/` - for new functionality
   - `refactor/` - for restructuring of existing features
   - `fix/` - for bugfixes

1. Run `dotnet format` against the `Kentico.Xperience.RepoTemplate` solution

   > use `dotnet: format` VS Code task.

1. Commit changes, with a commit message preferably following the [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/#summary) convention.

1. Once ready, create a PR on GitHub. The PR will need to have all comments resolved and all tests passing before it will be merged.
   - The PR should have a helpful description of the scope of changes being contributed.
   - Include screenshots or video to reflect UX or UI updates
   - Indicate if new settings need to be applied when the changes are merged - locally or in other environments

1. This repository is stored with `lf` line endings. If you are developing on Windows you can set your Git config to automatically checkout as `crlf` and commit as `lf`.

   ```powershell
   # git config --global core.autocrlf true
   ```

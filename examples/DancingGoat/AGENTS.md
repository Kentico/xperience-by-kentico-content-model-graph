# AGENTS.md

## Project overview

- **Name:** Dancing Goat
- **Stack:** ASP.NET Core MVC, Xperience by Kentico, LESS + Grunt frontend pipeline
- **Purpose:** Sample specialty-coffee website and store demonstrating Xperience by Kentico content management, Page Builder, email marketing, and digital commerce.
- **Running app URL:** See `Properties/launchSettings.json`; the administration UI is at `/admin`.

## Repository layout

| Path                        | What lives there                                                                 |
| --------------------------- | -------------------------------------------------------------------------------- |
| `Components/`               | Page Builder widgets, sections, inline editors, form sections, view components   |
| `AdminComponents/`          | Admin UI customizations (excluded from live-site-only deployments)               |
| `Controllers/`              | MVC controllers (content pages, store, account, checkout)                        |
| `Models/`                   | Content type models — `*.generated.cs` files are generated, do not edit by hand  |
| `Views/`                    | Razor views and layouts                                                          |
| `EmailComponents/`          | Email Builder components                                                         |
| `Helpers/`                  | Tag helpers, generators, utilities                                               |
| `wwwroot/Content/Styles/`   | LESS sources + compiled `Site.css` / `Landing-page.css` (never edit compiled CSS) |
| `wwwroot/Content/Fonts/`    | Self-hosted fonts (never link font CDNs)                                         |
| `Gruntfile.js`              | Frontend build (LESS compile, bundling, minification)                            |

## Useful commands

| Task                      | Command                                     |
| ------------------------- | ------------------------------------------- |
| Run site                  | `dotnet run`                                |
| Build                     | `dotnet build`                              |
| Install frontend deps     | `npm install`                               |
| Recompile styles          | `npx grunt less`                            |

## Content changes

If you change the site's content model (add or remove fields, define new content types or schemas, etc.), you must run the following commands to regenerate the code files.

`dotnet run -- --kxp-codegen` (see [docs](https://docs.kentico.com/documentation/developers-and-admins/api/generate-code-files-for-system-objects))

## Coding conventions

- Never hand-edit `Models/**/*.generated.cs` — change the content type in the admin UI and regenerate.
- File names must match class names exactly.
- One empty line at the end of every file.
- Avoid unnecessary inline comments.
- Use `CMS.IO` instead of `System.IO` for all file system operations.
- Avoid regions — they signal a class is doing too much.
- Do not use the ternary operator for complex multi-line or nested expressions, or when passing parameters — use a variable instead.
- No abbreviations or contractions in identifiers (`GetWindow`, not `GetWin`); no underscores or non-alphanumeric characters except in constants.
- Constants: ALL_CAPS with underscore separators. Non-public fields: camelCase noun/adjective, no `m` prefix.
- Collection properties: plural noun, never a `List`/`Collection` suffix (`Items`, not `ItemList`).

## Kentico MCP Servers

Both servers are configured in `.mcp.json` (Claude Code) and `.vscode/mcp.json` (VS Code / Copilot).

- **Kentico Docs MCP** (`kentico-docs-mcp`) is the **primary source** for any question about Xperience by Kentico APIs, configuration, and usage patterns. Prefer it over web search and over prior knowledge.
- **Kentico Management MCP** (`xperience-management-mcp`) is used to work with content in the **running local** instance (content types, content items, pages, Page Builder). Prefer it over manual changes in the admin UI.

## Validation of changes

- Always build the project after making changes.
- After LESS changes, recompile styles and verify the served CSS updated.
- Always validate user-facing changes in the browser for content, layout, styling, and localization correctness before committing.

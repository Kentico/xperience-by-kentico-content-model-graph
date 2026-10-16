# Agent instructions

## Development

- Before a commit, format with: `dotnet format --no-restore`
- `examples/` is excluded from formatting by `examples/.editorconfig` (`generated_code = true`)
- The Dancing Goat connection string and management API secret can be committed to source control because they are for local development only.

## Planning docs (`.agent-resources/`)

- `STATUS.md` – single source for current state (components, open checks, pending decisions). Update after each slice or when an item closes.
- `PLAN.md` – research, design rules, component catalog, decisions, append-only slice log. Don't track status there.
- `implementation-spec-*.md` – scope of each slice.

## Dancing Goat sample

These instructions apply to `examples/DancingGoat/`.

### Project overview

- **Name:** Dancing Goat
- **Stack:** ASP.NET Core MVC, Xperience by Kentico, Tailwind CSS + esbuild frontend pipeline
- **Purpose:** Sample specialty-coffee website and store demonstrating content management, Page Builder, email marketing, and digital commerce.
- The site URL is in `examples/DancingGoat/Properties/launchSettings.json`; the administration UI is at `/admin`.

### Repository layout

| Path | Contents |
| --- | --- |
| `examples/DancingGoat/Components/` | Page Builder widgets, sections, inline editors, form sections, and view components |
| `examples/DancingGoat/AdminComponents/` | Admin UI customizations, excluded from live-site-only deployments |
| `examples/DancingGoat/Controllers/` | MVC controllers for content pages, store, account, and checkout |
| `examples/DancingGoat/Models/` | Content type models; `*.generated.cs` files are generated and must not be edited |
| `examples/DancingGoat/Views/` | Razor views and layouts |
| `examples/DancingGoat/EmailComponents/` | Email Builder components |
| `examples/DancingGoat/Helpers/` | Tag helpers, generators, and utilities |
| `examples/DancingGoat/Styles/` | Stylesheet sources; top-level files are bundle entry points and shared building blocks are in `partials/` |
| `examples/DancingGoat/wwwroot/Content/Styles/` | Compiled CSS; never edit directly |
| `examples/DancingGoat/wwwroot/Content/Fonts/` | Self-hosted fonts; do not use font CDNs |
| `examples/DancingGoat/Tools/` | Tailwind CSS and component bundle build scripts |

### Useful commands

Run commands from `examples/DancingGoat/`.

| Task | Command |
| --- | --- |
| Run site | `dotnet run` |
| Build | `dotnet build` |
| Install frontend dependencies | `npm install` |
| Recompile styles | `npm run build:css` |
| Recompile styles on change | `npm run watch:css` |
| Rebuild component bundles | `npm run build:bundles` |
| Recompile styles and bundles | `npm run build` |
| Accessibility scan | `npm run a11y -- <url> [<url> ...]` |

### Content changes

When changing the Dancing Goat content model, regenerate code files with `dotnet run -- --kxp-codegen` (see [Xperience code generation docs](https://docs.kentico.com/documentation/developers-and-admins/api/generate-code-files-for-system-objects)).

### Coding conventions

- Never hand-edit `Models/**/*.generated.cs`; change the content type using the Management MCP and regenerate.
- File names must match class names exactly, and files must end with one empty line.
- Avoid unnecessary comments and regions.
- Use `CMS.IO` instead of `System.IO` for file system operations.
- Avoid complex, nested, or parameter-passing ternary expressions; use a variable instead.
- Do not abbreviate identifiers or use contractions. Use underscores only in constants.
- Constants use `ALL_CAPS_WITH_UNDERSCORES`; non-public fields use camelCase nouns or adjectives without an `m` prefix.
- Collection properties use plural nouns, not `List` or `Collection` suffixes.

### Kentico MCP servers

Both servers are configured in the repository-root `.mcp.json`. The Management MCP URL and secret mirror the `ManagementApi` section of `examples/DancingGoat/appsettings.Development.json`.

- **Kentico Docs MCP** (`kentico-docs-mcp`) is the primary source for Xperience APIs, configuration, and usage.
- **Kentico Management MCP** (`xperience-management-mcp`) works with content in the running local instance.

### Validation

- Build the project after changes.
- After changes under `Styles/`, rebuild styles, verify the compiled CSS, and commit the updated CSS.
- Validate user-facing changes in a browser for content, layout, styling, and localization.
- After view, layout, or widget changes, run `npm run a11y -- <url>` against affected pages; the site must be running.

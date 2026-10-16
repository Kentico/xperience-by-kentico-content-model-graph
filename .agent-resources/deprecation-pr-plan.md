# Spec: deprecate old package IDs and rename to Kentico.Xperience.Labs.ContentModelGraph.Admin

## Progress

- [ ] PR A (deprecation) - merged (#10)
- [ ] Run ADO pipeline on `main` -> 1.1.0 on nuget.org
- [ ] Tag `v1.1.0` + GH release
- [ ] PR B (rename, 2.0.0) - opened
- [ ] nuget.org deprecation of old IDs
- [ ] External link updates

## Decisions

- 1.1.0 = last release of `Kentico.Xperience.ContentModelGraph` (single package)
- `[Obsolete]` on public APIs consumers most likely use (documented in README / Usage Guide)
- Search/popularity not a concern -> no unlist, no popularity transfer
- New ID starting version: **2.0.0** (decided; keeps repo tags/releases continuous)

## Current state (verified)

- nuget.org old IDs: 1.0.0-prerelease-1 (repo tags: v1.0.0-v1.1.0). `main` = 1.1.0 (`Directory.Build.props:8`), unpublished
- New `Kentico.Xperience.Labs.ContentModelGraph.Admin` ID: not on nuget.org
- ADO pipeline `.azuredevops/pipelines/build-and-release.yml`: manual run, push only from `refs/heads/main`
  -> PR A merge + publish, then PR B merge + publish. No pipeline change.
- Root `README.md` packed into the nupkg (`Directory.Build.props:21`)

## Order

1. PR A (deprecation) -> merge -> run pipeline -> 1.1.0 on nuget.org -> tag `v1.1.0` + GH release
2. PR B (rename IDs + namespaces) -> merge -> run pipeline -> 2.0.0 new IDs
3. nuget.org deprecation of old IDs (manual)
4. Update external links

## PR A scope

PR A adds `S1133` to `NoWarn` and `#pragma warning disable S1075` around the `Url` const in the `DeprecationMessages.cs` file (not verified as required). PR B must remove these.

### 1. `[Obsolete]` targets

Consumer-facing APIs per `README.md` and `docs/Usage-Guide.md`:

Attribute shape:

```csharp
internal static class DeprecationMessages
{
    public const string PackageRenamed =
        "Kentico.Xperience.ContentModelGraph packages are deprecated. Use Kentico.Xperience.Labs.ContentModelGraph.Admin packages (namespace Kentico.Xperience.Labs.ContentModelGraph.Admin). See https://github.com/Kentico/xperience-by-kentico-content-model-graph";
    public const string DiagnosticId = "KXCR0001";
}

[Obsolete(DeprecationMessages.PackageRenamed, DiagnosticId = DeprecationMessages.DiagnosticId, UrlFormat = "https://github.com/Kentico/xperience-by-kentico-content-model-graph")]
```

- `DiagnosticId` lets consumers suppress only this warning (`<NoWarn>KXCR0001</NoWarn>`) instead of all CS0618
- Const sharing: single internal class (removed in PR B)

Internal warning suppression (lib, tests, Dancing Goat all call these):

- Add `<NoWarn>$(NoWarn);KXCR0001</NoWarn>` to `Directory.Build.props` (covers `src/` + `tests/`)
- `examples/DancingGoat`: check whether it inherits root `Directory.Build.props`; if not, add same `NoWarn` to its csproj
- Consumers still see the warning (suppression is compile-time per project)
- Remove all of this in PR B

### 2. Package metadata

- Keep `VersionPrefix` 1.1.0, empty `VersionSuffix`
- Prefix csproj `Description` with `DEPRECATED: replaced by Kentico.Xperience.Labs.ContentModelGraph.Admin`
  - `src/Kentico.Xperience.ContentModelGraph/Kentico.Xperience.ContentModelGraph.csproj`

### 3. Docs

- `README.md`: `> [!WARNING]` callout under badges: 1.1.0 is final release under these IDs; future releases ship as `Kentico.Xperience.Labs.ContentModelGraph.Admin*` with new namespaces; `dotnet list package --deprecated` finds old refs
- `README.md` version matrix: note 1.1.0 is final for old IDs
- `docs/Usage-Guide.md`: same callout at top
- Keep install commands as-is (new IDs don't exist yet; PR B rewrites)
- Point callout at the GitHub repo, not nuget.org new-ID pages (404 until PR B publishes)
- Draft v1.1.0 GH release notes: 1.1.0 changes + "final release under these IDs" + migration (swap package refs, update `using`s, 2.0.0 is functionally equal to 1.1.0)

### 4. Validation

- `dotnet format --no-restore`
- `dotnet build -c Release` -> no new warnings/errors in repo
- `dotnet test -c Release --no-build`
- `dotnet pack src/Kentico.Xperience.ContentModelGraph.Libs.slnx -c Release -o <tmp>` -> inspect nupkgs: version 1.1.0, description, README callout
- Quick consumer check: scratch project referencing packed 1.1.0 nupkg -> calling `AddContentModelGraph()` shows `KXCR0001` warning with message

### 5. PR description

Use `.github/PULL_REQUEST_TEMPLATE.md`. Breaking change: No (warnings only). Risk: Low.

## New ID version: 2.0.0 (recommended)

- Same functionality as old 1.1.0, but namespace change is breaking -> major bump fits SemVer intent
- One continuous history in README version matrix (1.0.0, 1.1.0, ..., 1.1.0 old IDs -> 2.0.0+ new IDs); no "which 1.0.0?" confusion
- Users arrive from repo, so continuous numbering in README/releases/tags matters more than "fresh ID starts at 1.0.0"
- Git tags stay unique (`v2.0.0`), no clash with existing `v1.0.0`-`v1.3.0`
- Alternative 1.0.0: clean slate but duplicates tag/version names already used, needs prefixed tags or matrix split

## PR B (rename) notes, for later

- `PackageId`, assembly names, `RootNamespace` (`Directory.Build.props`), folders, `.slnx`, `using`s
- ADO artifact names/paths (`build-and-release.yml`), `ci.yml` Node cache path + typecheck dir
- README/Usage Guide install commands, badges, matrix
- Admin client `ProjectName` `kentico-xperience-content-model-graph-admin` / `AdminOrgName`: changing affects admin module identity; decide deliberately
- Verify no persisted type names / code names depend on namespaces
- Remove `[Obsolete]`, `DeprecationMessages.cs`, `KXCR0001`/`S1133` from `NoWarn`, deprecation callouts in README/Usage Guide/package descriptions
- Bump `VersionPrefix` to 2.0.0
- Confirm `Kentico.*` prefix reservation + same nuget.org owner

## nuget.org deprecation (after PR B publish)

Old ID: Manage package -> Deprecation -> Select all versions -> Reason: Legacy -> Alternate package -> custom message ("Renamed; namespaces changed; see repo README"). No unlist.

| Old ID                              | Alternate                                |
| ----------------------------------- | ---------------------------------------- |
| Kentico.Xperience.ContentModelGraph | Kentico.Xperience.Labs.ContentModelGraph.Admin |

## External updates

Community portal blog post, Kentico integrations/marketplace listing, any samples referencing old IDs.

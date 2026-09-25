# Code Review — `feat/usage-mapping`

Reviewed: diff vs `main`, 39 files. Status legend: `[ ]` open · `[~]` in progress · `[x]` done ·
`[-]` skipped (deliberate, no changes made).

**Runtime verification, 2026-09-24:** the repo owner confirmed the admin behaves correctly in the live
app and the solution builds. That clears the three standing inferences that had only been verified by
compilation — the channel application name format (#4), the generated admin URLs (#9, #10, #18), and
the form tab's route parameter (#19). The individual ⚠ notes below are kept for provenance, but those
three are no longer open risks.

Item numbers are stable IDs assigned in discovery order, not priority — they are not sequential within
a section.

---

## Blockers — crashes

### [x] 1. `TryGetPropertyIgnoreCase` throws instead of returning `false`

**Resolved.** Widget-JSON parsing extracted to `internal static class WidgetReferenceReader`
(`src/Kentico.Xperience.ContentModelGraph/WidgetReferenceReader.cs`), helper rewritten as an explicit
loop, 20 tests added in `tests/.../WidgetReferenceReaderTests.cs`. Suite: 48 passed, 0 failed.

`src/Kentico.Xperience.ContentModelGraph/ContentItemRelationshipGraphBuilder.cs:759`

`EnumerateObject().FirstOrDefault(...)` returns `default(JsonProperty)` on a miss. `JsonProperty.Name` is `_name ?? Value.GetPropertyName()`, and `GetPropertyName()` calls `CheckValidInstance()`, which throws on a default `JsonElement`. So `property.Name is not null` throws `InvalidOperationException` on every missing key. Verified empirically against `{"somethingElse":1}`.

The only catch in the chain is `catch (JsonException)` in `ReadWidgetReferences`, so it escapes to `Build` and 500s the page. Crash sites: lines 369, 376, 394, 401, 408, 412, 419.

**Fix:** use `element.TryGetProperty`, or enumerate without `FirstOrDefault`.

### [x] 2. Wrong Page Builder JSON key — `"TypeIdentifier"` vs `"type"`

**Resolved.** Key changed to `"type"` in `WidgetReferenceReader.cs:63`; the C# property name
`WidgetReference.TypeIdentifier` kept, since it mirrors the platform's own. Existing sample-based test
strengthened to assert the types (`DancingGoat.LandingPage.HeroImage`,
`DancingGoat.LandingPage.ProductCardWidget` ×2) and a case added for a widget with no `type` key.
Suite 54 passed, 0 failed.

Deferred to #15: the label is still the raw codename (`Widget: DancingGoat.LandingPage.HeroImage`)
rather than the widget's display name, and the property name appears only in the codename.

`src/Kentico.Xperience.ContentModelGraph/ContentItemRelationshipGraphBuilder.cs:408`

Reflection over `Kentico.PageBuilder.Web.Mvc.WidgetConfiguration` (and the Email Builder equivalent) shows `TypeIdentifier` carries `[JsonProperty("type")]`. Neighbouring keys (`editableAreas`, `sections`, `zones`, `widgets`, `variants`, `properties`) happen to match case-insensitively; this one never does.

Combined with #1, **any** content item holding a Page/Email Builder widget crashes its relationships tab. Even with #1 fixed, every widget edge renders as `Widget: ` with codename `widget::<prop>`.

### [x] 16. Website channel root 500s — `WebPageItemID` 0 has no content item

**Resolved.** `ValidatePage()` overridden on `ContentItemRelationshipsPageBase<T>`; both abstract
resolvers now return `Task<int?>` and all four `?? throw ... "was not found"` sites return the nullable
value instead. Resolution is memoized in two per-request task fields so `ValidatePage` and
`ConfigureTemplateProperties` share one lookup per identifier (the web page path actually loses a
redundant language query). 5 tests added; suite 53 passed, 0 failed.

Verified against `Kentico.Xperience.Admin.Base.dll` 31.7.3 (cache at `D:\.package-cache\nuget`):
`Page<T>.ValidatePage` is public virtual; `PageValidationResult` is public sealed with `IsValid` /
`ErrorMessageKey` / `ErrorMessageParams`; `base.forms.error.objectnotinitialized` is a real key used by
the platform's own pages in `Admin.Base`, `Admin.DigitalMarketing` and `Admin.Websites`.

`src/Kentico.Xperience.ContentModelGraph/WebPageRelationshipsPage.cs:41`

Reproduced by the user at `/admin/webpages-1/en_0/relationships`:

```
Message: Web page 0 was not found.
Exception type: System.InvalidOperationException
   at ...WebPageRelationshipsPage.ResolveContentItemId() in WebPageRelationshipsPage.cs:line 41
   at ...ContentItemRelationshipsPageBase`1.ConfigureTemplateProperties(TClientProperties properties) in ContentItemRelationshipsPageBase.cs:line 33
```

The website channel root is a synthetic node with `WebPageItemID` 0 — there is no `WebPageItemInfo`
row and no content item behind it. `ResolveContentItemId` treats the lookup miss as a hard error.

**Use the platform mechanism, not a custom empty state.** The native "Something went wrong! / This
object doesn't exist." screen comes from `ValidatePage()` on the base admin UI page types:

```csharp
public override async Task<PageValidationResult> ValidatePage()
{
    var info = await GetInfoObject();

    return new PageValidationResult
    {
        IsValid = info is not null,
        ErrorMessageKey = "base.forms.error.objectnotinitialized"
    };
}
```

`ContentItemRelationshipsPageBase<T>` should override `ValidatePage()` to return `IsValid = false`
when the item cannot be resolved, so the page never reaches `ConfigureTemplateProperties`.

The platform invoker confirms the ordering and the contract:

```csharp
var page = await pageActivator.CreateInstance(node, routeValues);

var validationResult = await page.ValidatePage();
if (!validationResult.IsValid)
{
    var errorMessage = localizationService.GetString(validationResult.ErrorMessageKey);
    if (validationResult.ErrorMessageParams != null)
    {
        errorMessage = string.Format(errorMessage, validationResult.ErrorMessageParams);
    }

    throw new InvalidPageException(errorMessage);
}
```

So: page parameters are bound before validation runs; a failure short-circuits everything downstream;
`ErrorMessageKey` goes through `localizationService.GetString`, so it must be a real resource key
(`ErrorMessageParams` is available for `string.Format` substitution). `ResolveContentItemId` does not
need a nullable signature for the page-load path.

The client fallback already exists as a backstop regardless:
`ContentItemRelationshipsTemplate.tsx:415` renders "Relationship information is unavailable for this
item." when `graph?.rootItem` is falsy, and `ContentItemRelationshipsClientPropertiesBase.Graph` is
already nullable.

Same throw pattern exists in the sibling pages and should be handled together:

- `WebPageRelationshipsPage.cs:42` (web page), `:54` (content language)
- `EmailRelationshipsPage.cs:43`
- `HeadlessRelationshipsPage.cs:43`
- `ContentItemRelationshipGraphBuilder.cs:74` — throws `Content item {itemId} was not found`, a second
  500 path reachable even once the page resolves an id (notably from `ExpandRelationships`, whose
  `ItemId` is client-supplied)

**Message:** use the native screen via `ValidatePage()`. No custom text, no new client property.

**Linked items are handled separately — see #17.** The page-load path and the expand path differ:

- _Current item_ — resolved from the URL during page load. `ValidatePage()` covers this.
- _Linked item_ — `ExpandRelationships` is a `[PageCommand]`, an XHR after render, so the native
  not-found screen cannot apply. `ContentItemRelationshipGraphBuilder.cs:74` throws and the client
  just sees a failed command. Normally the id came from the server's own graph, so it exists; it goes
  missing if the item is deleted between page load and the click.

This compounds with #5: a failed expand leaves `pendingExpansions` permanently desynced, so after one
failure every later expand merges under the wrong node. Fixing #5 makes an expand failure recoverable
rather than session-poisoning.

### [x] 17. Expanding a deleted linked item 500s — render a "missing item" end node instead

**Resolved.** `IsMissing` added to `ContentItemRelationshipItem`; the throw replaced by an early
`CreateMissingItemGraph(itemId)` return (made `internal static` so it is testable). Client: `isMissing`
on the DTO, a merge branch that flags the existing node while preserving every other field,
`canExpand` gated on `!item.isMissing`, a rose `Missing` `Tag` on its own row (+28px in
`estimateRelationshipNodeHeight`), dashed node border, and grey dashed edges with italic hint-coloured
labels. 2 tests added; suite 56 passed, 0 failed. Client `tsc --noEmit` and webpack production build
both clean.

Note: the merge branch deliberately skips the edge-record rewrite — that block deletes all edges whose
`origin` is the expand key, which would erase the very edges meant to render as broken.

`src/Kentico.Xperience.ContentModelGraph/ContentItemRelationshipGraphBuilder.cs:74`

```csharp
if (!items.TryGetValue(itemId, out var rootItem))
{
    throw new InvalidOperationException($"Content item {itemId} was not found.");
}
```

`ExpandRelationships` is a `[PageCommand]` — an XHR after the page has rendered — so #16's
`ValidatePage()` cannot cover it. The id normally comes from the server's own graph, so it exists;
it goes missing if the item is deleted between page load and the click.

**Decision (repo owner):** do not throw and do not fail the command. Mark the node as missing and treat
it as terminal — no reference retrieval, no expand affordance. **Keep the node's original metadata**
(it helps diagnose why the item went missing), add a label indicating the missing status, and
de-emphasize its edges.

### Design

Because the original metadata must be preserved, a new `GraphNodeKind` is the _wrong_ mechanism —
`Kind` carries the content-type semantics (website / reusable / email …) that we want to keep. Use a
boolean flag instead, following the existing `IsDefaultLanguageFallback` precedent:

- **Server** — add `public bool IsMissing { get; init; }` to `ContentItemRelationshipItem`
  (`ContentItemRelationshipGraphModel.cs:25`). At `ContentItemRelationshipGraphBuilder.cs:74`, instead
  of throwing, return a graph whose `RootItem` has `IsMissing = true` and `ItemId = itemId`, with empty
  `Incoming` / `Outgoing`. Required members still need values — placeholders are fine, the client
  ignores them (see below).
- **Client model** — add `isMissing?: boolean` to `ContentItemRelationshipItemDto` (`model.ts`).
- **Merge** — _this is the required change._ `mergeExpansion`
  (`ContentItemRelationshipsTemplate.tsx:193`) only merges `relationship.relatedItem` entries into the
  items map; it **never reads `response.rootItem`**, so today a missing root is silently ignored and
  the node keeps its stale state. It must handle `response.rootItem?.isMissing` by updating the
  existing `items` entry for `nodeId` — setting `isMissing: true` while preserving every other field
  the node already has.
- **Terminality** — `ContentItemRelationshipsTemplate.tsx:331` currently computes
  `canExpand: Boolean(item.itemId)`; extend to `&& !item.isMissing`.
- **Node label** — `RelationshipNode.tsx` renders a badge/tag when `item.isMissing`, and
  `estimateRelationshipNodeHeight` must account for it.
- **Edges** — de-emphasize (dashed / muted) any edge whose source or target is a missing node.

Also note this compounds with #5 today: a failed expand leaves `pendingExpansions` permanently
desynced. Returning a valid response instead of an error sidesteps that, but #5 still needs its own fix.

Also note this compounds with #5: a failed expand currently leaves `pendingExpansions` permanently
desynced, so after one failure every later expand merges under the wrong node. Returning a valid
response instead of an error sidesteps that, but #5 should still be fixed on its own merits.

---

## Correctness & security

### [x] 3. `ForPreview` unset — references from latest version, field values from published

**Resolved.** `ForPreview = true` added alongside `IncludeSecuredItems = true` at
`ContentItemRelationshipGraphBuilder.cs:337`, with a comment recording why it is unconditional. Both
reference reads (lines 241, 575) use `ContentItemCommonDataIsLatest`, so the two halves now agree, and
the taxonomy symptom is fixed by the same line.

This is the library's **only** content-query call site — one executor injection, one execution — so
there is no other mismatch. The other `ContentQueryExecutionOptions` uses are all in the stock
DancingGoat sample and are deliberate (`SiteMapController` published-only; `ProductSkuValidator` runs
both on purpose; `ProductLinkedOnceRule` already pairs `ForPreview` + `IncludeSecuredItems` exactly as
here). Suite 56 passed, 0 failed.

No test added: the only observable behaviour is the options instance passed to
`IContentQueryExecutor.GetResult`, and asserting it would require mocking the executor, the data
container, ~6 info providers and a static `DataClassInfoProvider` call — a test of the mock, not the
code.

`src/Kentico.Xperience.ContentModelGraph/ContentItemRelationshipGraphBuilder.cs:305`

References come from `ContentItemCommonDataIsLatest = true` (lines 216, 679); `GetFieldValues` passes only `new ContentQueryExecutionOptions { IncludeSecuredItems = true }`, and `ForPreview` defaults to `false` (latest **published**).

Scenario: editor adds a reference to item X in an unpublished draft. The reference row exists so the edge is drawn, but the content query returns published field values (or nothing for a never-published item), `MatchFields` finds no match, and the edge renders with an empty field label. For a never-published root item `GetTaxonomyRelationships` returns `[]` — all taxonomy edges vanish.

### [x] 4. Permission checked against the wrong application

**Resolved.** New `IContentItemGraphPermissionEvaluator` /
`ContentItemGraphPermissionEvaluator.cs` — `GetViewableItemIds(IReadOnlyCollection<int>)` evaluates the
whole set in batch, routing per surface as designed. `CheckPermission` now uses it for the single
root/expanded item (closing hole (b)); the graph builder calls it once per graph and marks denied nodes
`IsRestricted` with `AdminUrl`/`LiveUrl` nulled. Client: `isRestricted` on the DTO, `canExpand` extended
to `Boolean(item.itemId) && !item.isMissing && !item.isRestricted`. The four
page subclasses dropped `IWorkspacePermissionEvaluator` and `IInfoProvider<ContentItemInfo>` from their
constructors. `Usage-Guide.md:48` rewritten. Suite 59 passed, 0 failed (was 56).

**Aligned with the platform, 2026-09-25.** The repo owner built the case (a restricted item referencing
an accessible one) and read Content hub → **Used in**: the platform _does_ list the restricted item —
name, content type, language — with only the link disabled and the tooltip "You don't have permission to
read this item". Its Channel column reads **"Content hub"**, the kind, never the workspace, and the list
has no code-name column at all. The node therefore stays, and now withholds exactly what that tab
withholds: `GetDisclosableDetails` blanks `CodeName` and substitutes the generic `LocationKind` for the
workspace in `LocationName`. `DisplayName`, content type, edges and layout position are unchanged.

**Narrowed, 2026-09-25.** That substitution applies to the content hub branch only. A second pass through
the same tab found restricted **email** and **headless** items showing their real channel names
("Dancing Goat Emails", "Dancing Goat Mobile"): the Channel column is not permission-redacted at all, it
simply shows the channel, and "Content hub" is the literal value on every content-hub row because those
items have no channel. `GetDisclosableLocationName` now replaces the name for
`ItemLocationKind.CONTENT_HUB` only; website, headless and email nodes keep the channel name. `CodeName`
suppression is unchanged — the tab has no such column either way, so it is a deliberate choice rather
than a platform match.

The earlier "no
badge" decision is reversed for the same reason: the client now shows a **Restricted** tag plus a
disabled "Open in admin" stand-in, both carrying the platform's wording, so "no link exists" and "you may
not open this" no longer look alike. `estimateRelationshipNodeHeight` reserves the extra 28px (tag) and
40px (stand-in row).

**Query count: 3–7 per graph, independent of node count.** 1 items + 1 channels + up to 3 (one per
channel _type_ present, not per channel) + 2 (web page items + ACL join, only when website items are
ACL-gated). Administrator path is 1 query. Application permission checks are in-memory. `IProgressiveCache`
was not used — the target is met without it and per-user permission caching adds invalidation risk.

**Where the compiler corrected the design:**

- `IApplicationPermissionEvaluator.Evaluate(ApplicationPermissionEvaluationContext)` returns `bool`
  **synchronously** — not an awaitable with `.Succeeded`. (`IWorkspacePermissionEvaluator.Evaluate` _is_
  async-with-`.Succeeded`, as the old code had it.)
- `ContentItemInfo` lives in `CMS.ContentEngine.Internal`, not `CMS.ContentEngine`.
- `GetListResult<int>()` trips Sonar S6966 here → used `GetListResultAsync<int>()`.

Everything else compiled as specified.

**Guard test — no live CMS needed.** `ContentItemGraphPermissionEvaluatorTests.cs` pins the three
platform constants against literals, so a rename in a future package fails loudly:
`WebsiteConstants.WEBSITE_CHANNEL_APPLICATION_PREFIX` == `"Kentico.Xperience.Application.WebPages"`,
`EmailChannelApplication.IDENTIFIER` == `"Kentico.Xperience.Application.EmailChannel"`,
`HeadlessChannelApplication.IDENTIFIER` == `"Kentico.Xperience.Application.HeadlessChannel"`. Plus tests
for the `<identifier>_<guid>` composition and the empty-input short circuit.

**⚠ Unverified — check this first if channel permissions misbehave in manual testing.** Which GUID the
channel application name uses (`WebsiteChannelGUID` / `EmailChannelGUID` / `HeadlessChannelGUID` vs
`ChannelInfo.ChannelGUID`) came from the decompiled design, not from a live `CMS_ApplicationPermission`
table — reading the DancingGoat connection string from user secrets was blocked. The identifier
_prefixes_ are proven by the passing guard test; the GUID choice is not.

**DI lifetimes changed:** `IContentItemRelationshipGraphBuilder` and the new evaluator moved
Singleton → **Scoped**, because `IAuthenticatedUserAccessor` is per-request and a singleton capturing it
would fail ASP.NET Core scope validation. `IContentModelGraphBuilder` untouched (still singleton). This
also settles the original review's open question about captive dependencies.

`src/Kentico.Xperience.ContentModelGraph/ContentItemRelationshipsPageBase.cs:61`

`CheckPermission` always evaluates `View` on `ContentHubApplication` for `item.ContentItemWorkspaceID`, but the page also mounts on `WebPageLayout`, `EmailEditLayout` and `HeadlessEditLayout`. Workspaces scope the Content hub only — website channel access is role-based.

- (a) A channel editor without Content hub permission opens the tab on a page → `ForbiddenAccessException`.
- (b) `ExpandRelationships` accepts an arbitrary `ItemId` gated only on Content hub workspace access, so a Content-hub-only user can enumerate page/email content in channels they cannot open.

**Confirmed by the repo owner: `ContentItemWorkspaceID` is null for channel items.** So case (a) is not
hypothetical — the check is failing closed for every web page, email and headless item.

### The permission model (from the docs)

| Surface                  | What governs access                                                                                                                                                                                       |
| ------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Content hub (reusable)   | Workspace permissions — `IWorkspacePermissionEvaluator.Evaluate(WorkspaceDataPermissions.VIEW, typeof(ContentHubApplication), workspaceId)`                                                               |
| Website channel          | **Two layers:** the _Access channel_ application permission, **plus** page-level ACL (_Display_, _Read_, _Create_, _Update_, _Delete_, _Synchronize_) inherited down the content tree, with break/restore |
| Email / headless channel | Not yet pinned down — likely application-level per channel, no ACL (no content tree)                                                                                                                      |

`Read` is the relevant page permission: _"view the content of pages and link to them through the page
selector."_ `Display` is weaker — see the name in the tree and selectors only.

Two related notes:

- The code passes the literal string `"View"`; the docs use the constant `WorkspaceDataPermissions.VIEW`.
  Almost certainly the same value, but unverified — if they differ, the Content hub path is broken too.
- `HasAccess` is **not** applicable: it evaluates live-site visitors and member roles, not admin users.
- `IWebPageAclManager.GetPermissions(pageId)` returns the ACL _configuration_
  (`WebPageAclConfigurationDescriptor` — roles and their permissions), not an effective per-user
  verdict. No batch API is documented.

### Decision: what to show for an inaccessible node

**Show the node, with no admin link and no expand button.** The relationship stays visible — you can
tell that something references the item — without exposing the content. Reuses the terminal-node
machinery from #17.

### The platform's own evaluator is `internal`

- `Kentico.Xperience.Admin.Base.IWebPageAclPermissionEvaluator` — **internal**
- `Kentico.Xperience.Admin.Websites.WebPageAclPermissionEvaluator` — **internal** implementation

A third-party module cannot call either. Under assessment: whether their logic can be reproduced on
public APIs, or whether it bottoms out in enough internal machinery to be not worth attempting. The
risk to weigh is a reproduction that _silently diverges_ from the platform (global admin bypass,
_Access channel_ interaction, ACL inheritance resolution, deny-over-allow precedence) — worse than no
reproduction at all.

### Verdict: reproducible, batched, ~3–6 queries regardless of node count

Decompiled from 31.7.3. The platform ships a **batched** version of this logic —
`ContentItemAclPermissionsEvaluator` (internal, `Admin.Websites`) evaluates a whole result set in one
query. Mirror that, not the per-page `IWebPageAclPermissionEvaluator`, which takes a single page id
with no collection overload.

**The rule for `READ`:** allowed iff `user.IsAdministrator()`, **or** the user has `ManagePermissions`
on the channel application, **or** they have `View` on that application **and** an ACL row grants
`Read`.

Two things that looked hard and are not:

- **Inheritance** — nothing to reproduce. It is _materialized_ in `CMS_WebPageAclMapping`; each page
  row points at exactly one effective `WebPageAclID`. No tree walk.
- **Deny-over-allow** — does not exist. `WebPageAclRolePermissionInfo` has no `Allowed` bit. Grants
  only; absence is denial.

### Per-surface model

| Surface                | Check                                                                                                                                                                     |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Website channel        | Batched ACL (`IObjectQueryAclPermissionExtender.JoinAclPermissions`) **plus** `View`/`ManagePermissions` on `Kentico.Xperience.Application.WebPages_<WebsiteChannelGUID>` |
| Email channel          | Application permission only — `EmailChannelApplication.IDENTIFIER` (public constant)                                                                                      |
| Headless channel       | Application permission only — `HeadlessChannelApplication.IDENTIFIER` (public constant)                                                                                   |
| Reusable (Content hub) | Existing `IWorkspacePermissionEvaluator` workspace check                                                                                                                  |

Route by `ContentItemInfo.ContentItemChannelID` → `ChannelInfo.ChannelType`. **Email and headless have
no ACL layer at all** (exhaustive type scan for `*Acl*` found matches only in `CMS.Websites`).

`IWebPageAclManager.GetPermissions` is the **wrong** tool — intersecting it by hand would miss the
administrator bypass, the channel-manager bypass, and the `View` gate, which is exactly the hole this
item closes.

### Decisions taken

- **`.Internal` accepted here.** The required types live in `CMS.Websites.Internal` /
  `CMS.Membership.Internal` — public-but-unsupported namespaces, not CLR-internal. There is no
  non-`.Internal` path to a correct check, so #8 should be narrowed to the `.Internal` uses that _do_
  have public alternatives.
- **App-name format gets a guard test.** `<publicPrefix>_<channelGUID>` is read from decompiled
  internal code and is the one genuine fragility. A test asserting an `ApplicationPermissionInfo` row
  of that shape exists for a known channel makes an upgrade that changes the convention fail loudly
  rather than silently deny everyone.
- **Inaccessible nodes:** shown, with no admin link and no expand button.

Still to confirm:

- the real permission model for each of the four layouts this page mounts on (Content hub, website
  channel, email channel, headless channel), and the concrete evaluator services for each;
- `Kentico.Xperience.Admin.Websites.UIPages.Internal.UsageTab` — the platform's own "where is this
  used" page, which must already solve this exact problem; how it checks permission, what it does with
  items in channels the user cannot access, and whether it batches;
- whether a batch permission API exists. The graph renders many linked items at once and
  `ExpandRelationships` pulls more on demand, so a per-item check is an N+1. If there is no batch API,
  the fallback shape is to resolve each item's channel once and evaluate per distinct channel rather
  than per item.

### [x] 5. Expansion queue desyncs

**Resolved.** `pendingExpansions` and the `usePageCommand(...{after})` block deleted entirely;
`handleExpand` now awaits `usePageCommandProvider().executeCommand<ContentItemRelationshipGraphDto,
ExpandRelationshipsArgs>(...)` and merges with `nodeId`/`direction` still lexically in scope. Both
failure modes are structurally impossible now. Typecheck and production webpack build both clean.

Signature confirmed in `entry.d.ts:250`: returns `Promise<TCommandResult | undefined>` — so the
`if (response)` guard is required, not defensive padding.

**Gotcha worth remembering:** `usePageCommand` internally calls `registerCommand(name, execute)`, and
the provider's `executeCommand` short-circuits to a locally registered handler when one exists for that
name — returning `undefined`. Keeping both for the same command name would silently never reach the
server. Removing the `usePageCommand` call is what makes the direct call work. (Read from the minified
bundle, so inferred rather than vendor-documented, but it matches the admin's own usage pattern.)

`src/Kentico.Xperience.ContentModelGraph/Client/src/content-item-relationships/ContentItemRelationshipsTemplate.tsx:254`

`pendingExpansions` is a FIFO consumed by `shift()` in the `after` callback. `usePageCommand`'s compiled implementation runs `after` only after a successful awaited request (skipped when cancelled/aborted).

- (a) One failing expand (e.g. #4's `ForbiddenAccessException`) leaves its entry in the queue forever, so every later expand merges under the wrong node/direction.
- (b) Buttons are disabled per-key only — "Outgoing" on node A then "Incoming" on node B issues two concurrent requests; if B returns first it merges as A's outgoing set.

**Fix:** pass context through the call (e.g. `usePageCommandProvider().executeCommand`) rather than a shared queue.

---

## PoC → Kentico best practice

### [x] 6. ~~`ContentLanguageModelBinder` duplicates a built-in~~ — **REVIEW FINDING WAS WRONG**

**Not actionable. The local copy is necessary and stays.**

`Kentico.Xperience.Admin.Base.UIPages.ContentLanguageModelBinder` exists but is **`internal` to
`Kentico.Xperience.Admin.Base`**, with no `InternalsVisibleTo` for this assembly. Deleting the local
copy produces `error CS0122: 'ContentLanguageModelBinder' is inaccessible due to its protection level`
— one per usage. Fully qualifying the type reproduced CS0122 at the new column, ruling out a
name-resolution artifact (not CS0104 ambiguity, not CS0246 missing).

**Why the original review got this wrong:** Kentico's shipped XML documentation
(`Kentico.Xperience.Admin.Base.xml`) documents the type, its `(string)` constructor, `Bind(PageRouteValues)`
and `ContentLanguageUrlIdentifier` output — because those docs include **internal** types. Reading the
XML docs alone makes an inaccessible type look like public API. Worth remembering for the rest of this
review: **XML doc presence is not evidence of accessibility.**

The "shadows the platform type by simple name" observation was accurate but is unavoidable, and is now
recorded in a 10-line XML doc comment on the class explaining why the copy exists.

**Residual, deliberately not fixed:** the binder still resolves `IInfoProvider<ContentLanguageInfo>`
via `Service.Resolve<T>()`. Because the platform instantiates it from a `typeof()` in an attribute,
converting to constructor DI is a separate question about `PageModelBinder` activation. Left alone
rather than guessed at — a follow-up if wanted.

`src/Kentico.Xperience.ContentModelGraph/ContentLanguageModelBinder.cs:10`

`Kentico.Xperience.Admin.Base.UIPages.ContentLanguageModelBinder` already exists ("Binds URL parameters into content language url identifier properties"), same `(string parameterName)` ctor, same `ContentLanguageUrlIdentifier` output. The new type shadows it by simple name in files that `using Kentico.Xperience.Admin.Base.UIPages` — it compiles because same-namespace wins, a silent trap. It also resolves `IInfoProvider<ContentLanguageInfo>` via `Service.Resolve<T>()`.

**Fix:** delete the file, use the platform binder.

### [-] 7. Hand-rolled usage queries instead of the public API — **SKIPPED, no changes**

`src/Kentico.Xperience.ContentModelGraph/ContentItemRelationshipGraphBuilder.cs:40`

`IContentItemUsageRetriever.Retrieve(contentItemId, languageName)` is public in `CMS.ContentEngine` and documented as "the items directly referencing the `languageName` language variant of the content item" — exactly what lines 40–47 + 665–701 reconstruct from `ContentItemReferenceInfo`. Likewise `RelationshipFieldMatcher` (lines 819–872) reimplements `IFormFieldContentItemReferenceExtractor` / `IContentItemReferenceFieldsProvider`.

This is the main PoC→production swap expected before merge.

### Verified accessibility (decompiled against 31.7.3) — the review was partly wrong

| API                                       | Review claimed | Actual                                                                        |
| ----------------------------------------- | -------------- | ----------------------------------------------------------------------------- |
| `IContentItemUsageRetriever`              | public         | ✅ **public**, `CMS.ContentEngine`                                            |
| `IFormFieldContentItemReferenceExtractor` | public         | ✅ **public**, `CMS.ContentEngine` (extends `IContentItemReferenceExtractor`) |
| `IContentItemReferenceFieldsProvider`     | public         | ❌ **`internal`** — unusable, drop from scope                                 |

### Two signature constraints that matter more than accessibility

```csharp
Task<IEnumerable<ContentItemLanguageMetadata>> Retrieve(
    int contentItemId, string languageName, CancellationToken cancellationToken = default);
```

1. **One call per item — no batch overload.** The builder currently batches across many items. A
   wholesale swap would reintroduce exactly the N+1 that #4 just eliminated.
2. **Incoming references only** — _"the items directly referencing the `languageName` language variant"_.
   The graph needs both directions, so this replaces at most half the hand-rolled logic.

**So this item is not a straight swap.** Decide first whether the correctness/support benefit of the
platform API outweighs losing batch retrieval. Options worth weighing: use it only on the
single-item paths (root item, `ExpandRelationships`) where N=1 and keep the batched queries for
multi-item graph builds; or keep the hand-rolled queries and close the gap with tests instead.

### [-] 8. New dependency on `.Internal` namespaces — **SKIPPED, no changes**

`src/Kentico.Xperience.ContentModelGraph/ContentItemRelationshipGraphBuilder.cs:1`

`CMS.ContentEngine.Internal`, `CMS.Headless.Internal`, `CMS.Websites.Internal` (and `IReusableFieldSchemaManager`, `ContentItemCommonDataInfo`, `ContentItemReferenceInfo`, `ContentItemLanguageMetadataInfo`). Kentico's stable-customization guidance: internal-namespace API "should be considered unstable, and may be subject to breaking changes during **any** update". `main` had no such dependency.

**Scope narrowed by the decision taken in #4.** The original framing — avoid `.Internal` generally —
is no longer the project's position. `.Internal` is accepted where **no public alternative exists**,
which is the case for the permission layer (`CMS.Websites.Internal`, `CMS.Membership.Internal`: there
is no non-`.Internal` path to a correct permission check, and getting the check wrong is a security
bug). Note these are _public-but-unsupported namespaces_, not CLR-internal types.

So this item is now: **replace `.Internal` usage that has a public alternative.** Still in scope:

- `IContentItemUsageRetriever` in place of the hand-rolled reference queries — this is #7.
- `IContentLanguageFallbackChainProvider` in place of the manual fallback-chain walk in
  `GetLanguageContext` (lines 185–204).
- Any remaining `.Internal` type where a public equivalent can be identified.

Out of scope: the permission-layer `.Internal` usage introduced by #4.

### [x] 9. Hardcoded admin URLs in the relationship builder

**Resolved — 2 of 5 generated, 3 deliberately left hardcoded with TODOs.**

Generated: `/admin/content-types/list/{ClassID}/fields` → `GetPath<ContentTypeFields>`;
`/admin/content-hub/{workspaceId}/{lang}/all/list/{itemId}/content` → `GetPath<ContentItemEdit>` with
`ContentHubWorkspace` / `ContentHubContentLanguage` / `ContentHubFolder` / `ContentItemEditSection` —
exactly the location-segment migration the changelog asks for.

**Left hardcoded (lines 533, 546, 558), and this is the right call:** the web page, headless and email
URLs target **dynamic applications**. `WebsiteChannelDynamicApplicationProvider` and friends compose
the application root slug at runtime as `webpages-{channelId}` — it does not come from a parameterized
`UIPage` slug. `IPageLinkGenerator.GetPath` takes page types plus `PageParameterValues`; there is no
public way to hand it a runtime-composed application slug. The child page types are public, but the
app-root segment cannot be produced, so generating these would have been a guess. Each site carries a
comment naming the provider.

`IContentItemNavigationPathRetriever` (`GetNavigationPath` / `GetReusableItemEditNavigationPath`)
looked like the purpose-built API for this — the compiler says it is **internal**. Another instance of
the #6 lesson.

**No tests added, and this one is structural:** `IPageLinkGenerator` carries a
`NonPubliclyImplementableInterface` sealing member, so it cannot be faked outside Kentico. The URL
builders are thin private wrappers with no branching left to test; `AdminUrlHelperTests.cs` already
covers `EnsureAdminPrefix`, which every new call site goes through.

**⚠ Not exercised against a running instance.** The page-type hierarchies were read from `UIPage`
assembly-attribute metadata in the 31.7.3 DLLs and every type resolved under compilation, and the
generated URLs match the previous strings segment-for-segment — but no one has clicked them.

`src/Kentico.Xperience.ContentModelGraph/ContentItemRelationshipGraphBuilder.cs:130`, `:571`, `:580`, `:589`, `:597`

Five `/admin/...` strings built by hand, while this same PR introduces the correct approach (`IPageLinkGenerator.GetPath<T>(parameters)`) in `ContentModelGraphBuilder.GetClassAdminUrl`. The Kentico changelog calls out content-item admin URLs gaining a location segment and instructs customizations to migrate to generated links — hardcoded ones silently rot into 404s on upgrade.

### [x] 10. Same, in the file that already injects `pageLinkGenerator`

**Resolved — both generated.** Taxonomy group → `GetPath<TaxonomyEdit>` with `TaxonomyEditSection`;
reusable field schema fields → `GetPath<ReusableFieldSchemaFields>` with `ReusableFieldSchemaEditSection`.
`AddSchemaNodes` had to stop being `static` to reach `pageLinkGenerator`.

`src/Kentico.Xperience.ContentModelGraph/ContentModelGraphBuilder.cs:121`, `:162`

Taxonomy and reusable-field-schema node URLs are hardcoded (`/admin/taxonomy/list/{id}/tags/group`, `/admin/content-types/reusable-field-schemas/{guid}/fields`) two dozen lines from `GetClassAdminUrl`, which does it properly.

### [x] 11. ~~Five~~ **Six** unbounded, uncached table scans per request

**Resolved.** The review undercounted — `languageInfoProvider.Get()` was a sixth unfiltered scan.

All six now go through a `LoadReference<TValue>(name, objectType, load)` helper wrapping
`progressiveCache.LoadAsync` with a 60-minute `CacheSettings` and
`CacheHelper.GetCacheDependency([$"{objectType}|all"])` — mirroring the `ContentModelGraphPageBase`
pattern. Each also gained a narrow `Columns(...)` list derived by reading every downstream consumer.

Dependency keys are built from each Info class's `OBJECT_TYPE` constant rather than hardcoded strings,
so the compiler verifies them. The `<objecttype>|all` format is confirmed by the Kentico docs
("Reference — Cache dependency keys").

**Two deliberate design choices worth recording:**

- **No `WhereIn` on any of the six.** The needed ids are only knowable per request, so keying cache
  entries by that id set would fragment the cache into a near-unique entry per request — defeating the
  caching, which is the bigger win. Whole-table + narrow columns + a correct `|all` dependency is
  strictly better for tiny reference tables.
- **Cached projections, not `Info` objects.** Lookups project into immutable records (`LanguageRef`,
  `WebsiteChannelRef`) or plain dictionaries, so partially-loaded mutable `AbstractInfo` instances are
  never shared across requests. `LanguageContext` now carries `LanguageRef`; mechanical renames only,
  no behaviour change.

**Effect:** a page visit with 12 expands went from 13 × 6 = **78 full-table scans to 6** — and those 6
are then shared across later requests and other users until eviction.

**⚠ Residual risk:** the `|all` key is only touched if the object type sets `TouchCacheDependencies =
true` in its `ObjectTypeInfo`. That is the norm for built-in types, but was not confirmed per-type
(confirming it needs decompilation, which was ruled out for this task). The 60-minute expiry is the
bound if any one of them does not touch.

`src/Kentico.Xperience.ContentModelGraph/ContentItemRelationshipGraphBuilder.cs:550`

`channelInfoProvider.Get()`, `workspaceInfoProvider`, `websiteChannelInfoProvider`, `headlessChannelInfoProvider`, `emailChannelInfoProvider` each fetched in full (no `Columns`, no `WhereIn`) on every page load _and_ every `ExpandRelationships` click. `ContentModelGraphPage` caches its graph for 60 minutes; this path caches nothing. Expanding a dozen nodes replays all five scans a dozen times.

### [x] 12. Docs contradict shipped behaviour

**Resolved.** The stale paragraph deleted outright (−2 lines), nothing written in its place, per the
"keep minimal, remove stale info" instruction. Field resolution verified as working via `MatchFields`,
`RelationshipFieldMatcher` and `ContentModelGraphFieldLabel.Resolve`, all test-covered.

Residual truth deliberately not preserved: `AddRelationships` still emits an empty `FieldLabel` when
`MatchFields` finds no source, so "field names are omitted when unresolvable" survives as an edge case.
Deleted rather than rewritten by instruction; a one-line caveat could go back if wanted.

Everything else in the guide was checked and is accurate (version, 60-minute cache, toolbar buttons,
relationship types, search behaviour).

**Follow-up — `Usage-Guide.md:48` will go stale when #4 lands.** It currently reads: _"The page uses
the current item's workspace `View` permission."_ That is accurate today and is exactly the behaviour
#4 replaces, so that line needs updating as part of #4, not before.

`docs/Usage-Guide.md:50`

"the public metadata used by this package does not reliably map that identifier to a field definition. The page therefore omits field names when they cannot be resolved." The PR implements field-name resolution (`MatchFields`, `ContentModelGraphFieldLabel`), so this is leftover PoC text. The guide also omits taxonomy-tag edges and Page Builder widget references, both now rendered.

---

## Gaps found while reviewing the sample Page Builder JSON

Sample: `CODE-REVIEW-sample-page-builder.json` (repo root). Neither of these is a regression — both are
coverage the graph does not yet have.

### [x] 13. Form references from the Form Widget are not graphed

**Resolved.** `WidgetReferenceReader.ExtractWidgetPropertyObjectCodeNames` reads the `objectCodeName` of an
object reference (the `objectGuid` is null in real data, so the code name is the only usable key), and
`ReadPageBuilderReferences` returns them as `WidgetObjectReference` alongside the content references.
`ContentItemRelationshipGraphBuilder.GetFormRelationships` resolves the distinct code names against
`BizFormInfo` in **one batched query** (`WhereIn(FormName, codeNames)`) and emits an outgoing relationship
per form.

**Terminal by construction, not by a new flag.** The node carries `Kind = GraphNodeKind.FORMS`,
`Identifier = FormGUID`, `ContentTypeDisplayName = "Form"` and an `AdminUrl`, but **no `ItemId`** — the same
choice made for taxonomy tags in #18. `canExpand` is `Boolean(item.itemId) && …`, so the node cannot be
expanded, and no form identifier ever reaches a content item query or the permission evaluator's content
item path. Client side only `kindLabels` needed a `forms: "Form"` entry; `NodeKind`/`nodeColor` already
carried `"forms"` (`#b35c00`) from the content model graph.

Admin URL generated, not hardcoded: `GetPath<FormBuilderTab>` with `{ typeof(FormEditSection), formId }`,
both confirmed public **by compiling** (the #6 lesson). The Digital Marketing UI pages namespace is
imported under an alias rather than wholesale.

An object reference carries no object type, so a code name that matches no form simply contributes no node
— which is also what keeps contact groups out. `conditionTypeParameters` is not walked at all, so the
`selectedContactGroups` references noted as out of scope in #15 stay out.

`src/Kentico.Xperience.ContentModelGraph/ContentItemRelationshipGraphBuilder.cs:440` (`ExtractWidgetPropertyIdentifiers`)

`Kentico.FormWidget` stores its form reference as an object reference, not a content item reference:

```json
{
  "type": "Kentico.FormWidget",
  "variants": [
    {
      "properties": {
        "selectedForm": [
          {
            "objectGuid": null,
            "objectCodeName": "DancingGoatCoffeeSampleList"
          }
        ]
      }
    }
  ]
}
```

`ExtractWidgetPropertyIdentifiers` only matches member names `Identifier` and `WebPageGuid` and only
yields `Guid`s, so this reference is dropped entirely. Note `objectGuid` is `null` in the sample —
resolution has to go through `objectCodeName` against the form (`BizFormInfo`), so this cannot reuse
the existing GUID-only path.

A page with a Form Widget therefore shows no edge to the form it embeds, which is exactly the kind of
usage mapping this feature exists to surface.

**Scope decision needed:** forms are a new node kind in the graph (not content items), so this needs a
node type, an icon/colour, and an admin URL alongside the existing kinds.

### [x] 14. Section properties are never walked

**Resolved.** The walk now reads the section's own `properties` object before descending into its zones.
The `properties` handling is shared by all three surfaces (widget variant, section, page template) as
`CollectPropertyReferences`, because the value shape is identical — confirmed against both sample files,
not assumed.

The resulting edge labels as `Section: {type}` rather than `Widget:` — see the `SourceKind` note under #21.

`src/Kentico.Xperience.ContentModelGraph/ContentItemRelationshipGraphBuilder.cs:388` (`CollectWidgetReferences`)

The walk goes `sections → zones → widgets → variants → properties`. Sections carry their own
`properties` object (visible in the sample on `DancingGoat.SingleColumnSection`), and a section
property can hold a content item reference the same way a widget property can. Those references are
missed.

### [x] 21. Page template configuration JSON is never read

**Resolved.** `ContentItemCommonDataVisualBuilderTemplateConfiguration` added to both common data queries
(`GetRootCommonData` and `GetLatestCommonData`) and passed into the reader alongside the widgets column on
both call paths, so outgoing _and_ incoming edges see template references.

The template blob is read flat: `identifier` → `properties` → the same extraction the other two surfaces
use. **The `"identifier"` key is covered by a test that would fail if `"type"` were used instead** — the
#2 class of bug.

### `SourceKind` as implemented (shared by 13/14/15/21)

`PageBuilderSourceKind { Widget, Section, Template }` sits on a new `PageBuilderReferencePath` record that
also carries `AreaIdentifier`, `SectionTypeIdentifier`, `VariantName` and `IsPersonalizationVariant`. Both
`WidgetReference` (GUID) and `WidgetObjectReference` (code name) hold one, so the path travels with every
reference regardless of what it points at.

| Kind     | Label                    | Code name                                                                     |
| -------- | ------------------------ | ----------------------------------------------------------------------------- |
| Widget   | `Widget: {type}`         | `widget:{type}:{property}` — unchanged, so existing edges keep their identity |
| Section  | `Section: {type}`        | `section:{type}:{property}`                                                   |
| Template | `Template: {identifier}` | `template:{identifier}:{property}`                                            |

`src/Kentico.Xperience.ContentModelGraph/ContentItemRelationshipGraphBuilder.cs`

**Verified:** `ContentItemCommonDataInfo` has two builder JSON columns —

- `ContentItemCommonDataVisualBuilderWidgets` — read by the builder
- `ContentItemCommonDataVisualBuilderTemplateConfiguration` — **never read**

`GetRootCommonData` selects only the widgets column, and both call sites
(`rootCommonData?.ContentItemCommonDataVisualBuilderWidgets`,
`sourceCommonData.ContentItemCommonDataVisualBuilderWidgets`) pass only that one into
`WidgetReferenceReader.ReadWidgetReferences`.

A page template's configured properties can hold content item and web page references exactly the way
widget and section properties can — a template with a "featured article" or "hero image" selector
produces a reference that the graph silently omits.

**Shape confirmed** by a real sample the repo owner captured at
`CODE_REVIEW-sample-builder-template.json` (note the underscore — the other samples use a hyphen):

```json
{
  "identifier": "KenticoCommunity.BlogPostPage_Components",
  "properties": { "showTableOfContents": true },
  "fieldIdentifiers": {
    "showTableOfContents": "c2816d6f-1fc4-4331-a289-8cb10b5be472"
  }
}
```

Flat — no `editableAreas` / `sections` / `zones` / `widgets` / `variants`. Reading it is just
`properties` → iterate → `ExtractWidgetPropertyIdentifiers` on each value.

**⚠ The template's type identifier key is `"identifier"`, NOT `"type"`.** Widgets use `"type"`
(see #2, which was exactly this class of bug). Do not copy the widget key across.

Same class of gap as #14 (unwalked JSON holding references), and it needs the same `SourceKind`
treatment so the edge labels as `Template: {identifier}` rather than `Widget:`. **Belongs in the
13/14/15 bundle.**

### [x] 15. Widget edges lose the Page Builder path — personalization variants are invisible

**Resolved.** The full path is captured on `PageBuilderReferencePath` and surfaced two ways:

- **Edge label** stays short. When _every_ occurrence of a reference sits on a personalization variant the
  label says so — `Widget: DancingGoat.LandingPage.HeroImage · variant "Sample Requests"` for one variant,
  `· personalized` for several. A reference that also exists on the default variant gets no marker, because
  it is not hidden.
- **Edge tooltip** carries the whole path, one line per occurrence, on a new
  `ContentItemRelationship.FieldPath`:
  `top › DancingGoat.SingleColumnSection › DancingGoat.LandingPage.HeroImage › variant "Sample Requests" › image`

**The variant deliberately does not enter the code name.** Splitting one reference into one edge per variant
would draw several edges between the same pair of nodes; ReactFlow computes those from the handle positions
alone, so they would land on top of each other with their labels stacked. References are grouped by code
name instead and the variants are listed in the tooltip.

**Client:** edge labels moved off ReactFlow's SVG `<text>` label onto a small custom edge
(`RelationshipEdge.tsx`, `BaseEdge` + `EdgeLabelRenderer`), because an SVG text label has nowhere to hang a
tooltip. Passing a `ReactNode` label instead would have been a trap — `EdgeText` keys its bbox effect on the
label identity, so a fresh element each render loops. The missing edge styling moved from
`.react-flow__edge-text` to the new label class.

Zone level omitted as suggested (GUID only). `fieldIdentifiers` not used: property names resolve the
reference unambiguously here, so it would add a lookup without adding information.

**Still out of scope:** `conditionTypeParameters.selectedContactGroups`. Those are not walked at all.

`src/Kentico.Xperience.ContentModelGraph/WidgetReferenceReader.cs` (`WidgetReference`)

`WidgetReference` carries only `(TypeIdentifier, PropertyName, Identifier)`. The walk descends
**editable area → section → zone → widget → variant → property** and discards the first three levels
and the variant entirely.

The variant is the one that matters most. When a content item is referenced only from a _personalized_
widget variant, an editor looking at the page cannot see the reference — the variant has to be selected
in the builder before the reference appears. That is precisely the hidden usage this feature exists to
surface, and right now the edge gives no hint that the reference is conditional.

**Proposal:** capture the full path on the reference and surface it on the edge label / tooltip, e.g.

> `top` › `DancingGoat.SingleColumnSection` › `Kentico.FormWidget` › variant _Returning visitors_ › `selectedProducts`

What the JSON gives us at each level (confirmed against `CODE-REVIEW-sample-page-builder.json`):

| Level         | `identifier`                         | `type`                                  | Notes                          |
| ------------- | ------------------------------------ | --------------------------------------- | ------------------------------ |
| editable area | human-readable (`"top"`, `"bottom"`) | —                                       | usable as-is in a label        |
| section       | GUID                                 | yes (`DancingGoat.SingleColumnSection`) | use `type` for display         |
| zone          | GUID                                 | —                                       | probably not worth showing     |
| widget        | GUID                                 | yes (`Kentico.FormWidget`)              | resolve to widget display name |
| variant       | GUID                                 | —                                       | see below                      |
| property      | name (`selectedProducts`)            | —                                       | already captured               |

### Personalization keys — CONFIRMED from real data

The repo owner extended `CODE-REVIEW-sample-page-builder.json` with real personalized variants. The
shape is settled; nothing here needs inferring:

**`conditionType` sits on the WIDGET, not the variant:**

```json
{
  "identifier": "63718636-...",
  "type": "DancingGoat.LandingPage.HeroImage",
  "conditionType": "DancingGoat.Personalization.IsInContactGroup",
  "variants": [ ... ]
}
```

**Each personalized VARIANT carries `name` plus `conditionTypeParameters`:**

```json
{
  "identifier": "3cfad4b2-...",
  "name": "Sample Requests",
  "properties": { "image": [ { "identifier": "8a528627-..." } ], ... },
  "conditionTypeParameters": {
    "selectedContactGroups": [ { "objectGuid": null, "objectCodeName": "SampleRequestCustomer" } ],
    "variantName": null
  },
  "fieldIdentifiers": { ... }
}
```

**The default variant has neither `name` nor `conditionTypeParameters`** — so presence of either is a
reliable discriminator for "this reference lives in a personalization variant", and `name` is the
label to display. No bare GUIDs needed.

Suggested edge label:
`top › DancingGoat.SingleColumnSection › DancingGoat.LandingPage.HeroImage › variant "Sample Requests" › image`

**Out of scope but noted:** `conditionTypeParameters.selectedContactGroups` holds _object_ references
(`objectCodeName`) to contact groups — the same shape as the Form Widget reference in #13. Graphing
those would be a further extension, not part of this item.

Each variant also carries a `fieldIdentifiers` map (property name → field GUID), which may be a more
robust way to resolve the widget property to its field definition than matching on property name.

### [x] 18. Taxonomy tag nodes have no admin link

**Resolved.** URL generated with `GetPath<TagEdit>` + `{ TaxonomyEditSection: taxonomyId }`,
`{ TagEditLayout: tagId }`, matching the observed `/admin/taxonomy/list/{taxonomyId}/tags/{tagId}/edit`.
`Kind = "taxonomy"` → `GraphNodeKind.TAXONOMY`.

**The numeric-id question resolved better than expected.** `Tag` from `ITaxonomyRetriever.RetrieveTags`
is GUID-keyed only, as suspected — but only **one** supplementary provider is needed, not two:
`TagInfo.TagTaxonomyID` carries the parent taxonomy id, so no `IInfoProvider<TaxonomyInfo>` lookup at
all. New `GetTagAdminUrls(Guid[])` helper does a **single batched** query
(`.Columns(TagID, TagGUID, TagTaxonomyID).WhereIn(TagGUID, identifiers)`) covering every tag on the
item — one query per graph build, not per tag.

**`ItemId` deliberately left unset**, against the original suggestion — and correctly. `itemId` is what
the client uses to decide expandability and what it sends to `ExpandRelationships`; putting a `TagID`
there would make tag nodes expandable and feed a tag id into a content-item lookup. `AdminUrl` alone
delivers the jump-into-admin this item asked for. A code comment records the reasoning.

`src/Kentico.Xperience.ContentModelGraph/ContentItemRelationshipGraphBuilder.cs:419`

The tag node is built with neither `AdminUrl` nor `ItemId`:

```csharp
new ContentItemRelationshipItem
{
    Identifier = tag.Identifier.ToString("D"),
    DisplayName = tag.Title,
    CodeName = tag.Name,
    ContentTypeDisplayName = "Taxonomy tag",
    ContentTypeCodeName = "taxonomy",
    Kind = "taxonomy"
}
```

so every other node kind offers a jump into the admin and taxonomy tags dead-end.

Target URL pattern (from the repo owner):
`/admin/taxonomy/list/{taxonomyId}/tags/{tagId}/edit`

**Open question — the numeric ids may not be available.** The node is built from
`ITaxonomyRetriever.RetrieveTags(identifiers, languageName)`, whose `Tag` model is keyed by `Guid`
`Identifier`. Whether it also exposes the numeric `TagID` and its parent `TaxonomyID` needs checking;
if not, this needs a supplementary lookup through `IInfoProvider<TagInfo>` / `IInfoProvider<TaxonomyInfo>`
— and that lookup should be batched, not per tag.

**Do this together with #9/#10.** Those items are about replacing hardcoded `/admin/...` strings with
`IPageLinkGenerator`; adding a sixth hardcoded URL here would just widen the problem. Note
`ContentModelGraphBuilder.cs:121` already builds the taxonomy _group_ URL as
`/admin/taxonomy/list/{TaxonomyID}/tags/group`, so both belong in the same change.

Minor, same line: `Kind = "taxonomy"` is a string literal where `GraphNodeKind.TAXONOMY` exists.

### [~] 20. Relationship results are unbounded — no cap on how many referencing items are returned (20a skipped, 20b open)

`src/Kentico.Xperience.ContentModelGraph/ContentItemRelationshipGraphBuilder.cs`

There is no `TopN`, `Take`, or any other limit anywhere in the builder (verified by search). The graph
is one hop — root plus its direct incoming and outgoing references — so `N` is **the number of items
directly referencing this one**, not the graph's total size.

For a widely-reused item that is large and unbounded: a logo, a shared author record, a product image
used on 500 pages. Every one of those comes back, gets a node, gets laid out by Dagre, and renders.
Consequences, in rough order of severity:

1. The permission evaluator, the field-value queries and the location lookups all run over that full
   set on every page load.
2. The client lays out and renders hundreds of nodes — the graph becomes unreadable well before it
   becomes slow.
3. Nothing tells the user the view is incomplete, because nothing truncates it.

The example project's data is small enough that this never shows. Production content hubs are not.

### Split into two halves — they have very different risk

**20a — server-side guard. SKIPPED for now** — deferred until there is a concrete example to scope the
cap against. Picking a limit without real data on how many references a heavily-reused item actually
has would be guesswork, and a badly-chosen cap silently truncates real results.
Cap the number of relationships returned per direction, and report the true total alongside the
truncated set (e.g. `IncomingTotal` / `OutgoingTotal` on `ContentItemRelationshipGraph`). This bounds
every downstream per-request cost — permission evaluation, field-value queries, location lookups —
regardless of what the UI eventually does with it. The client can ignore the totals until 20b exists.
Pick a generous default cap so nothing real gets truncated in normal use.

**The reporting half already exists.** #22 added `ContentItemRelationshipGraph.Truncations` — a collection
of `{ Direction, ShownItemCount, TotalItemCount }` — and the client panel that renders it. 20a only has to
populate an entry per capped direction in `Build`; no model or client change is needed.

**20b — presentation. Needs UX experimentation, do not delegate blind.**
How a high-degree node should read and behave: "showing first N of M", per-direction pagination,
opt-in expansion, grouping by content type, collapsing sets of identical edges. The existing
`canExpand`/expand-on-demand machinery is the natural place to hang it, but the right interaction is
not obvious from the code — it wants trying out against a realistically large graph.

Worth noting the example project cannot exercise this: DancingGoat's data is too small. Testing 20b
needs either seeded data or a synthetic graph fixture.

Related: #11 (uncached table scans) compounds this — both are per-request costs that scale with the
same N.

---

## Feature requests

### [ ] 35. The content model graph application declares no permissions

`ContentModelGraphPage` is registered with `[assembly: UIApplication(...)]` but carries **no
`[UIPermission]` attribute**, so the application cannot be granted or denied in _Role management_ — it
has no permission for an administrator to assign.

**Wanted: at least a built-in View permission.**

### The platform's pattern — verified against the internal source and 31.7.3

`ContentTypesApplication` and `TaxonomyApplication` both declare four:

```csharp
[UIPermission(SystemPermissions.VIEW, "{$base.roles.permissions.view$}")]
[UIPermission(SystemPermissions.CREATE, "{$base.roles.permissions.create$}")]
[UIPermission(SystemPermissions.UPDATE, "{$base.roles.permissions.update$}")]
[UIPermission(SystemPermissions.DELETE, "{$base.roles.permissions.delete$}")]
public sealed class ContentTypesApplication : ApplicationPage
```

`UIPermissionAttribute` is **public** in `Kentico.Xperience.Admin.Base` (confirmed by decompiling 31.7.3,
not from XML docs — see #6). `SystemPermissions` lives in `CMS.Membership` and is already used by
`ContentItemGraphPermissionEvaluator`. `{$base.roles.permissions.view$}` is a platform resource string,
so the label localises for free.

**Only `VIEW` applies here** — the graph is read-only. Declaring Create/Update/Delete would offer
administrators switches that govern nothing.

### Two things to settle before implementing

1. **Does declaring the permission also enforce it?** For built-in templates the docs say listing pages
   require `VIEW` to access, but this is a custom `Page<TClientProperties>`. If declaration alone does
   not gate entry, the page must evaluate it — `IUIPermissionEvaluator.Evaluate(...)` is the documented
   route, and `ValidatePage()` (see #16) runs before `ConfigurePage`, so it is a natural place. Verify
   rather than assume.
2. **This changes who can get in.** Today the application has no permission, so access is governed only
   by reaching the Development category. Once `VIEW` exists, roles presumably need it granted, and an
   existing install could lose access until an administrator assigns it. That is the correct behaviour,
   but it is a breaking change for deployments and belongs in the README/Usage-Guide and release notes.

### Not affected

The relationship pages and contextual graph tabs are hosted **inside** other applications (Content hub,
Pages, Emails, Headless, Forms, Content types, Taxonomy), so they are governed by those applications'
permissions plus the per-surface checks from #4. Only the standalone application needs its own.

### [x] 34. Form classes link to Modules, and Modules access is unguarded

**Resolved.** `GetClassAdminUrl` now has three branches, not two: content type → Content types, form class →
Forms, everything left → Modules. The form branch is chosen by the `GraphNodeKind.FORMS` the loop already
resolved, so the link and the node kind can never disagree about what a form is.

`ClassID → FormID` is one `BizFormInfo` query per graph build, not per node - `WhereIn(FormClassID, …)` over
the form-kind classes, run before the class loop and skipped entirely when the Forms application is denied,
the way `GetTagAdminUrls` is. A class the query finds no form for gets **no link**, not a fallback into
Modules.

`ContentItemRelationshipGraphBuilder.GetFormAdminUrl` became `internal static` over an
`IPageLinkGenerator` and is now called from both graphs - shared rather than copied, as #33 did with
`GetApplicationLink`.

Modules is the fourth flag on `GraphApplicationAccess`, checked against `ModulesApplication.IDENTIFIER`
(`Kentico.Xperience.Application.Modules`, `Kentico.Xperience.Admin.Base.UIPages`). Confirmed against the
31.7.3 assembly, and the guard test compiling is itself the proof of usability. `ClassFields` is pinned to
Modules and `FormBuilderTab` to Forms by walking the page registrations - the walk now reads both the Base
and Digital Marketing assemblies, since the form builder is registered in the latter.

Cache combinations rose from 8 to 16, asserted by count as well as by uniqueness. `GetClassAdminUrl` takes
the three class values it needs rather than a `DataClassInfo`, which cannot be constructed outside a CMS
application context. Suite 144 passed, 0 failed.

Two separate problems, both in `ContentModelGraphBuilder.GetClassAdminUrl`, found while completing #33.

**a. Form classes link to the wrong application — a correctness bug, not a permission gap.**
`GetClassAdminUrl` branches on `ClassType`: content classes go to `ContentTypeFields` (Content types
app), **everything else** to `ClassFields` under `ModuleEditSection` (Modules app). Form classes take the
"everything else" branch, so a `forms`-kind node links into **Modules**. Forms are authored in the
**Forms** application, which is where the link should go.

Note `ResolveNodeKind` already identifies these — it returns `GraphNodeKind.FORMS` for classes in the
`BizForm` group — so the branch has the information it needs.

**Target URL, given by the owner:** the _Contact Us_ form should link to
`/admin/forms/list/2/builder` — the form builder — not anywhere under `/admin/modules/`.

**The generator already exists.** `ContentItemRelationshipGraphBuilder.GetFormAdminUrl` produces exactly
that shape:

```csharp
pageLinkGenerator.GetPath<DigitalMarketingUIPages.FormBuilderTab>(
    new PageParameterValues { { typeof(DigitalMarketingUIPages.FormEditSection), formId } })
```

So the content model graph needs the same generator, reached from a `ClassID` rather than a `FormID`.

⚠ **The lookup is what is missing.** This graph holds form _classes_ (`DataClassInfo`); the Forms
application addresses _forms_ (`BizFormInfo`). #19 went `FormID → FormClassID → ClassName`; this needs
the inverse, `ClassID → FormID`. Do it as **one batched query** over the form-kind classes, in the style
of #18's `GetTagAdminUrls`, not per node.

**b. Modules access is unguarded.** Whatever genuinely belongs in Modules after (a) still links there
with no application check — the same exposure #32 and #33 closed for Content types, Taxonomy and Forms.
Guard **access to the Modules application only**; individual class definitions within it are not
separately gated.

`ModulesApplication` / `Kentico.Xperience.Application.Modules` — confirm the identifier against the
31.7.3 assembly, as #32 did for the other three, rather than trusting XML docs (see #6).

### Scope

This adds a fourth flag to `GraphApplicationAccess` — a **public record shared with #32** — so it
touches the relationships graph's evaluator and guard tests, which are currently green. Both graphs'
cache keys include the record, so the content model graph's cache-name test rises from 8 combinations
to 16.

**Breaking API changes are acceptable** (owner's direction): design the correct API rather than
preserving the current shape. The package is `1.0.0-prerelease-1`.

### [x] 33. The content model graph links to applications the user cannot open

**Resolved.** `IContentModelGraphBuilder.Build(GraphApplicationAccess)` takes access as a parameter, so
the `TryAddSingleton` builder stays stateless and captures no scoped service. The 60-minute cache is
keyed by the access record — at most 8 entries however many users — with the shared dependency key
preserved so "Clear cache" still clears every variant in one touch.

Option 1 (key the cache) was chosen over suppress-after-cache for a reason neither of us anticipated:
**the governing application depends on how the URL was built, not on the node's `Kind`.**
`GetClassAdminUrl` branches on `ClassType`, and `Kind` cannot recover that branch (`OBJECT_TYPE` comes
from either side), so suppressing afterwards would have meant re-deriving the mapping _and_ cloning
every node to avoid mutating the cached graph.

`ContentModelGraphApplicationLinks` delegates to #32's `GetApplicationLink` rather than copying it, so
the "never invoke the URL factory when denied" guarantee is shared. Reusable field schemas are governed
by `ContentTypesApplication` — there is no separate application, verified by walking the registration
chain at runtime. Client already degraded to text; no client change. Suite 135 passed, 0 failed.

The same exposure #32 fixed on the relationships graph, in the other graph. `ContentModelGraphBuilder`
emits an `AdminUrl` on its nodes — content types, reusable field schemas, taxonomies — and
`ClassNode.tsx` renders it as a link with no application-permission check. Found while completing #32,
which was deliberately scoped to the relationships graph.

**Everything needed already exists after #32:**

- `IContentItemGraphPermissionEvaluator.GetApplicationAccess()` returns a
  `GraphApplicationAccess(bool ContentTypes, bool Taxonomy, bool Forms)` record — a record rather than
  an enum, to stay clear of the `IDE0072` trap — memoized per request on a `Scoped` service.
- `GetApplicationLink(bool isApplicationAccessible, Func<string> getAdminUrl)` suppresses the link
  **without invoking the URL factory**, so `IPageLinkGenerator.GetPath` does not run per node for a URL
  nothing consumes.
- The identifiers are pinned by guard tests in `ContentItemGraphPermissionEvaluatorTests.cs`.

### Differences from #32 worth planning for

- `ContentModelGraphBuilder` is registered **Singleton**, not Scoped (`ServiceCollectionExtensions.cs`),
  and permission evaluation is per-user — so the access record must not be captured in a field there.
  Resolve it per call, or reconsider the lifetime. **This is the main trap.**
- Its graph is **cached for 60 minutes** (`ContentModelGraphPage`, `CACHE_MINUTES = 60`). A cached graph
  must not carry one user's link visibility to another. Either apply suppression after the cache, or key
  the cache by access — the former is almost certainly right.
- Reusable field schemas link into the Content types application; confirm which identifier governs them
  rather than assuming a separate one exists.

Client: `ClassNode.tsx` renders `adminUrl` as a link; confirm it already degrades to plain text when the
URL is null, as `RelationshipNode.tsx` does.

### [x] 32. Links render to applications the user cannot open

**Resolved.** `GetApplicationAccess()` returns a `GraphApplicationAccess` record (not an enum, avoiding
the `IDE0072` trap), memoized per request on the Scoped evaluator — three checks total, never per node.
`GetApplicationLink` suppresses the link **without invoking the URL factory**, so `IPageLinkGenerator`
does not run for URLs nothing consumes. The taxonomy gate sits at `GetTagAdminUrls`, so a denied
Taxonomy application also skips the batched `TagInfo` query. Client already degraded to text. The
`xp-ban-sign` affordance is keyed off `isRestricted`, not a missing URL, so a link-suppressed node does
not wrongly claim permission trouble. Suite 121 passed at the time.

_The form page root needs no gate: `FormRelationshipsPage` is registered inside the Forms application,
so reaching it already implies access._

A content item's relationships graph links out to **content types**, **taxonomy tags** and **forms**.
Those are governed by _application_ permissions — a different axis from the workspace and channel
permissions #4 handles — so a user without access to those applications still sees the links. Xperience
blocks the destination, but the link should not be offered in the first place.

**Wanted:** when the application is not accessible, render the label as plain text instead of a link.

### Everything needed is public, verified against 31.7.3

| Link target   | Application class         | `IDENTIFIER`                                 |
| ------------- | ------------------------- | -------------------------------------------- |
| Content types | `ContentTypesApplication` | `Kentico.Xperience.Application.ContentTypes` |
| Taxonomy tags | `TaxonomyApplication`     | `Kentico.Xperience.Application.Taxonomy`     |
| Forms         | `FormsApplication`        | `Kentico.Xperience.Application.Forms`        |

All three are `public sealed class … : ApplicationPage` with a `public const string IDENTIFIER`
(confirmed by decompiling `Kentico.Xperience.Admin.Base.dll` / `Admin.DigitalMarketing.dll` 31.7.3, not
from XML docs — see #6 for why that distinction matters). No `.Internal` dependency.

`IApplicationPermissionEvaluator` is already injected into `ContentItemGraphPermissionEvaluator` by #4,
and its `Evaluate(ApplicationPermissionEvaluationContext)` is **synchronous, returning `bool`** — not an
awaitable with `.Succeeded`, unlike `IWorkspacePermissionEvaluator`.

### Notes

- These are **static** applications, unlike the dynamic channel applications in #4, so the identifiers
  are constants — no `<prefix>_<guid>` composition.
- Evaluate **once per application per request**, not per node. Three checks total, cached for the
  request; the graph can hold many content-type and tag nodes.
- Affects `ContentTypeAdminUrl` on every node, plus `AdminUrl` on taxonomy tag and form nodes.
- Complements #4 rather than overlapping it: #4 governs whether a _content item_ may be seen, this
  governs whether a _link out to another application_ is useful.

### [x] 27. Filters panel: collapsible, and stop it overlapping the graph on load

Content model graph. The owner added `overflow-y: auto` to the Filters box because the filters were
being clipped by the container; with that in, the panel is simply large.

- Make it **collapsed by default**, docked against the right border of the graph container, toggling
  open on click.
- **The initial fit/zoom does not account for it**, so the panel overlaps the graph on load. Note the
  left-hand action buttons do _not_ have this problem — worth understanding why before fixing (likely
  the fit call's padding, or the panel not being part of the measured bounds).

Applies to the content model graph specifically.

### [ ] 28. Minimap: smaller, and collapsible

Content item relationships graph. The minimap takes up even more room by default than the content model
graph's. Make it smaller and collapsible via a click toggle, consistent with #27's treatment.

### [x] 29. Long content type names clip the node label

Content model graph nodes. The tooltip shows the full name correctly on hover, so only the rendered
label is wrong. Likely a `text-overflow`/width interaction in `ClassNode.tsx`.

### [x] 30. Only schema edges are labelled on the global content model graph — the toggle is invisible

**Not a bug, but it reads as one.** `ContentModelGraphTemplate.tsx:214-218`:

```ts
label:
  edge.kind === "schemaAssignment"
    ? "Schema"
    : showFieldNames ? edge.label : undefined,
```

`schemaAssignment` edges are labelled unconditionally; every other kind is gated on `showFieldNames`,
which `ContentModelGraphPage.cs:33` defaults to `false` for the global graph (the contextual pages
override it to `true`). So the global graph shows `Schema` labels and nothing else, with no hint that a
toolbar toggle governs the rest.

Two candidate fixes: make `schemaAssignment` respect the toggle like everything else, or default the
toggle on for the global graph. The first is more consistent; the second is more informative. Owner's
call.

### [x] 31. `| N fields` on a node reads as if it describes the relationship

`ClassNode.tsx:29` renders `` `${data.name} | ${data.fieldCount} fields` ``. `FieldCount`
(`ContentModelGraphBuilder.cs:72`) is the class's own field count, excluding system fields and the
primary key — a property of the **node**, unrelated to any edge.

The owner read differing values on two linked nodes (Brewer Product, Coffee Product) as describing
their links to the Product SKU schema. The `|` separator immediately after the name is what invites
that reading. Consider a clearer presentation — a separate line, a subdued count, or a word that names
what is being counted.

### [x] 25. Bring the content model graph in line with the relationships graph

**Implemented.** Both asks, plus the two carry-over hazards.

- **The chip box is now shared**, not copied: `Client/src/shared/EdgeLabel.css` (`.cmg-edge-label`,
  `.cmg-edge-label__line`) and `Client/src/shared/edgeLabel.ts` (the max width, per-character advance,
  line height, chrome, and the `estimateEdgeLabelSegment` both layouts wrap text with). Each graph keeps
  its own modifiers next to the shared rules - merged entries, dividers, `+N more` and `missing` stay on
  the relationships side, and none of them were ported: a content model edge stands for exactly one
  relationship, so there is nothing to merge, count off or mark broken.
- **Dagre sizing came with it.** `ContentModelGraphTemplate`'s `layout` passes
  `{ width, height, labelpos: "c" }` per edge from `estimateClassEdgeLabelSize`, which returns a zero box
  when the "Show field names" toggle is off, so hiding labels reserves nothing. Where two fields point at
  the same class - two edges, one pair of nodes, not a multigraph - the larger reservation wins.
- **The focal node id now reaches the client.** `GraphData.FocalNodeId` is nullable and is set only by
  `ContentModelGraphNeighborhood.Filter`, so the whole-model page leaves it null and marks nothing; the
  client treats a null or empty id as "no focal node".
- **Node height is a function, not a second constant.** `classNodeHeight(isCurrent)` adds the ribbon's
  24px to `CLASS_NODE_HEIGHT`, and the layout offsets each node's centre by its own height, so the taller
  current node does not overlap its neighbours.

The relationships graph has had substantial polish (chip-styled edge labels with borders, Dagre label
space reservation, a clearly marked current item). The **content model graph** — the older of the two —
has none of it. Two asks from the owner:

**a. Same edge label design, specifically the border.** The content model graph still uses ReactFlow's
built-in SVG edge labels (`label`, `labelBgPadding`, `labelBgBorderRadius` in
`ContentModelGraphTemplate.tsx:214-221`) rather than the custom chip component the relationships graph
now uses.

**b. Mark the current item.** The contextual pages (content type, reusable field schema, taxonomy)
filter the graph to one node's neighbourhood via `ContentModelGraphNeighborhood.Filter(graph, nodeId)`,
but **the client is never told which node was focal** — `GraphData` carries only `Nodes` and `Edges`.
The owner notes this should be obvious from the centre position, but visual consistency with the
relationships graph's `CURRENT ITEM` treatment would help.

### Two carry-over hazards

1. **Adopting chips means adopting the Dagre reservation.** `ContentModelGraphTemplate.tsx:94` is
   `graph.setEdge(edge.source, edge.target)` with no label dimensions — the exact bug that made
   relationships-graph labels overlap nodes. Chip labels are wider than SVG text, so porting the design
   without porting `estimateRelationshipEdgeLabelSize`'s equivalent would _introduce_ that bug here.
2. **The focal node is optional.** The main `ContentModelGraphPage` shows the whole graph and has no
   focal node; only the three contextual pages do. Whatever carries the id must be nullable, and the
   marker must not render on the unfiltered graph.

Worth extracting the shared chip styling rather than duplicating it, since the relationships graph's
version is still being actively changed (merged labels, dividers).

### [x] 24. Order linked nodes by the field that references them

**Implemented as the post-layout permutation, client-side only.** `layout` runs Dagre unchanged, then
re-flows each rank so nodes reached through one field sit together. Nothing is hidden and no edge label
changed: only which slot in a column a node occupies.

- **Group key** `${anchor}\n${field}`, where `field` is `fieldCodeName || fieldLabel` (now carried on
  `RelationshipEdgeRecord`, via the `relationshipField` helper) and `anchor` is the node at the other end
  of the edge. A Page Builder property keeps one code name across personalization variants, so the three
  variants of a widget image share a key by construction.
- **Several edges per node:** the anchor nearest the root wins, the lowest edge id breaks the rest.
- **Ordering:** members sorted by Dagre's `y`, groups taken in order of their first member, so both the
  order between groups and the order inside one are Dagre's. Ordering groups by the anchor's own `y`
  instead was measured and made no useful difference (955 vs 981 crossings on the densest fixture).
- **Differing node heights:** the rank is re-flowed, not swapped. It starts at the same top edge and
  reuses Dagre's sequence of gaps, so the extent is unchanged and no tall node lands in a short slot.
- **Crossings:** free on the default one-hop graph — every edge meets at the root, so there is nothing to
  cross. Measured 3-5x more crossings only on synthetic multi-parent graphs with shared children
  (44 → 238, 345 → 981), which needs several expansions to reach.

**This is the right answer to the problem #23 failed to solve.** Group multi-value references
_spatially_ rather than by suppressing labels — nothing is hidden, every edge keeps its own label, and
the fan becomes legible because its members sit together.

Stated by the owner: _"if there are 3 variants of a widget image, those should appear vertically
without other content items in between them."_

Today Dagre orders nodes within a rank purely to minimise edge crossings, so the three images from one
widget property can be separated by unrelated items, and the fan reads as noise.

### Why this is cheap

`layout` (`ContentItemRelationshipsTemplate.tsx`) runs `Dagre.layout(graph)` and then reads
`positioned.x` / `positioned.y` per node. With `rankdir: "LR"`, **`x` identifies the rank and `y` is the
position within it.** So the set of `y` slots in a rank can be _permuted_ after layout — same spacing,
same extent, same rank assignment, only the order within the column changes. No second layout pass, no
Dagre configuration change.

### Design points

- **Group key:** the field of the edge connecting the node to the rank it hangs off
  (`fieldCodeName || fieldLabel`, already on `RelationshipEdgeRecord`). Nodes sharing a key cluster.
- **A node can have several edges** — an image referenced by two different widgets. Needs a
  deterministic tiebreak (e.g. lowest edge id) so the layout is stable across re-renders and expands.
- **Preserve Dagre's relative order between groups**, e.g. order groups by their members' minimum
  original `y`, and preserve original order within a group. Ordering groups arbitrarily would make
  nodes jump between renders.
- **Per rank and per side.** Incoming and outgoing occupy different ranks, so operating per distinct `x`
  handles both without special-casing direction.
- **Expect more edge crossings** — this deliberately overrides what Dagre optimised for. For a one-hop
  graph fanning from a single root, crossings are few and the grouping gain should dominate, but it is
  the real trade-off and should be looked at on a dense graph.

An alternative is Dagre's compound-graph support (`setParent`) to make each field a cluster. Likely
cleaner conceptually, but it changes layout metrics and node sizing, so the post-layout permutation is
the lower-risk first attempt.

Supersedes the deferred hover-only-label idea as the next density lever — see #20b.

### [-] 23. Group multiple links that share one field — **TRIED AND REVERTED**

**Option 1 ("label once per group") was implemented, evaluated against a real graph, and reverted on
2026-09-24.** Do not retry it.

It worked as designed — every node stayed on the canvas with its full card, links and expand buttons;
only the repeated label chips were suppressed, leaving one per `(source, field)` group with a count
suffix (`Widget: HeroImage (3)`).

**Why it was rejected:** the count read as _"3 items collapsed in here"_. The owner's reaction on
seeing it was "it's not clear how I can interact with the grouped items which aren't displayed" — when
in fact nothing was hidden at all. A label that makes a reader believe content is missing is worse
than a repeated label, and the deduplication did not save enough to be worth that cost.

The deeper lesson, which applies to any future attempt: **each edge being self-describing is worth more
than removing repetition.** An unlabelled edge in a fan gives the reader no way to tell whether it is
part of the labelled group or something the UI declined to explain.

Dropping just the count was considered and not pursued — it removes the false implication but also the
only signal that the unlabelled edges belong to the labelled one, so the fan becomes silently
inconsistent instead of misleadingly labelled.

If this is revisited, it should be a genuinely different shape (a group node, or collapse-on-demand),
not a variation on label suppression. See #20b — the two remaining density levers are hover-only labels
and orthogonal (`smoothstep`) routing, neither of which removes information from the default view.

---

<details>
<summary>Original finding (kept for context)</summary>

Observed on a content item (e.g. `NewYork-xn4wcoi7`) with **three tags referenced from a single
taxonomy field**. Each produces its own node and its own edge, so the same field label is drawn three
times fanning out to three nodes — visually noisy, and it scales badly with the edge-label work just
landed (each of those edges now reserves a label box).

The same shape occurs for any multi-value reference field: several content items selected in one
content-item selector, several pages in one page selector.

**The requirement, stated by the owner:** group the relationships that share a field, **while keeping
each linked item individually expandable for its own incoming relationships.** So this is a
presentation grouping, not a data merge — the individual nodes must survive with their own identity
and their own expand affordances.

### Where this lives

Purely client-side. The server already emits one `ContentItemRelationship` per (target, field) pair
with `FieldCodeName` / `FieldLabel`, which is exactly the grouping key. `edgeContentKey`
(`ContentItemRelationshipsTemplate.tsx:82`) is already
`${source}=>${target}:${fieldCodeName || fieldLabel}` — so edges sharing a field differ only by target.

### Options worth prototyping

1. **Label once per group.** Keep all edges, but render the field label on only one edge of each
   (source, field) group — or between the source and the fan-out point. Smallest change; removes most
   of the noise without touching layout or node identity.
2. **A group node.** Insert a small intermediate node for the field (`Tags ×3`) that fans out to the
   individual items. Reads clearly, but adds a node kind, changes the layout shape, and needs thought
   about what the group node itself does when clicked.
3. **Collapsed group, expandable.** Render the group as one node showing `3 tags`, expanding in place
   to the individual nodes on click. Most compact for high-cardinality fields; most work, and it
   overloads "expand", which currently means "fetch this item's relationships".

Interacts with #20b (dense-graph UX) and the deferred hover-only-labels idea — all three are about the
same problem of visual density, and are best judged together against a real graph rather than
separately.

</details>

### [x] 26. Coincident edges draw their label chips on top of each other

**Resolved 2026-09-24.** On a landing page that referenced the same image from its page template _and_
from a `HeroImage` widget, `Template: LandingPageSingleColumn` and `Widget: HeroImage / Variant: "Coffee
sale"` were drawn almost exactly on top of one another, the first nearly invisible behind the second.

**Cause.** `edgeContentKey` is `${source}=>${target}:${field}`, so two fields between the same pair of
nodes produce two edges. They have identical endpoints, so ReactFlow draws them as the _same_ bezier
curve and `getBezierPath` hands both chips the same midpoint. This is not a positioning bug: a curve has
one sensible place for a label, so no repositioning scheme can separate two labels on the same curve.

**Fix.** Records that share a source and a target are merged into a single edge whose chip states every
reference, each entry its own block with a hairline rule between them (`--color-divider-default`). A
single-entry chip renders exactly as before — no stray rule. `RelationshipEdgeData` now carries
`entries: { label, path }[]` instead of one `label`/`path` pair, `estimateRelationshipEdgeLabelSize`
sizes all the blocks plus the dividers so Dagre's reservation still matches what is painted, and the
4-line clamp moved from the chip to each entry. Beyond three entries the chip shows `+N more` and the
tooltip spells the rest out in full, deduplicated by line because merged references often share a Page
Builder path. Merged records share both endpoints, so `broken` is the same for all of them — there is no
mixed-state case.

**This is not #23.** That suppressed labels on edges with _different_ targets, so a reader could not tell
which node belonged to which field. Here the merged edges already had the same source and the same
target and were already drawn one on top of the other; every field is still named on the chip. The only
thing removed is a duplicate stroke nobody could see.

**The alternative was measured and rejected.** The obvious other fix — read Dagre's own edge-label
position back out of `graph.edge(v, w)` after layout instead of using the bezier midpoint — was checked
against real `@dagrejs/dagre` on multi-height fixtures. Dagre routes orthogonally through a label rank
while the chip sits on a curve, so its label point is nowhere near the curve: median divergence 65-394px
and up to ~1050px on dense fixtures, against a 38px-tall chip. Chips would have been detached from their
edges. Same measurement with the #24 re-flow disabled gives identical numbers, so the divergence is
Dagre's routing, not the node re-ordering.

**Residual.** On synthetic fixtures far denser than anything real (14 pages, 51 merged edges onto 9
shared items), 5-7 pairs of chips on edges with _different_ endpoints still touch. Nothing overlaps on
the realistic fixtures. Left alone deliberately: the remedy for that is #20b's density work, not another
positioning scheme.

---

### [x] 22. Content relationships tab for forms

**Resolved.** Forms now have a _Content relationships_ tab alongside the _Content model graph_ tab from
#19, rooted at the form and showing the content items whose latest Page Builder configuration embeds it.

**Why it was asked for:** #19's tab renders the form as an isolated node with no edges. That is correct
but useless. The content model graph is **schema-level** (content types, schemas, taxonomies and how
their definitions relate), while the only meaningful relationship a form has is **instance-level**:
"this page embeds this form via a Form Widget."

`AddFieldEdges` does run for form classes and can emit content-reference, schema-reference and
object-reference edges (only _taxonomy_ edges are gated off for non-content classes) — but those come
from field settings (`AllowedContentItemTypeIdentifiers`, `AllowedSchemaIdentifiers`, `refobjtype`),
and ordinary form fields (text, email, textarea) carry none. So form nodes are isolated in practice.

### ~~The hard part — this is a reverse lookup with no index~~ — **WRONG, the platform tracks it**

The original analysis assumed form references were untracked and that the reverse lookup meant a
`LIKE '%codename%'` scan of `ContentItemCommonDataVisualBuilderWidgets`. It does not.

`CMS.ContentEngine.Internal.ContentItemObjectReferenceInfo` (`cms.contentitemobjectreference`) records a
row for every form a content item's Page Builder configuration embeds. Verified against 31.7.3 **by
compiling**, not from XML docs — CLR-`public`, usable through `IInfoProvider<ContentItemObjectReferenceInfo>`.
Standard `.Internal` caveat applies (see #8); accepted here because there is no public alternative.

| Column                                         | Points at                   |
| ---------------------------------------------- | --------------------------- |
| `ContentItemObjectReferenceSourceCommonDataID` | `cms.contentitemcommondata` |
| `ContentItemObjectReferenceTargetFormID`       | `cms.form`                  |

`TargetFormID` is the **only** target column — this tracks forms and nothing else, it is not a general
object-reference tracker. So the reverse lookup is an indexed FK read, not a scan. The platform's own
`FormBuilderUsedInTab` / `FormUsageListingCommandManager` (`Kentico.Xperience.Admin.DigitalMarketing`)
run the same query.

The table holds **one row per common data version**, so the rows must be filtered by
`ContentItemCommonDataIsLatest` — without it, superseded versions and abandoned drafts read as current
usage.

**Tracking gaps — documented, not closed.** Forms rendered directly from view code, and forms referenced
by submission notifications, campaigns and automation processes, produce no row here. A custom component
that selects a form by its own means needs `[TrackFormReference]` (public, supported namespace) for its
usage to be recorded at all. Recorded in the XML docs on `BuildForForm`.

### As implemented

**Page.** `FormRelationshipsPage` (new file `FormRelationshipsPage.cs`), mounted on
`Kentico.Xperience.Admin.DigitalMarketing.UIPages.FormEditSection` with
`[PageParameter(typeof(IntPageModelBinder), typeof(FormEditSection))] public int ObjectId`, matching
`FormModelGraphPage` from #19. Ordered `ContentModelGraphPageOrder.BeforeLast` (= `Last - 1`) so
_Content relationships_ sits immediately left of _Content model graph_, the same order as on content items.

**Page structure.** As predicted, `ContentItemRelationshipsPageBase` could not be reused: it validates,
permission-checks and roots the graph on the content item the page is hosted on, and a form has no
content item id, no language variant and no workspace or channel. Rather than duplicate the expand
command, the shared half was lifted into a new base:

```
RelationshipGraphPageBase<T>                 // ExpandRelationships + CheckPermission
├── ContentItemRelationshipsPageBase<T>      // + content item / language resolution (behaviour unchanged)
│   └── ContentItemRelationshipsPage, WebPageRelationshipsPage, EmailRelationshipsPage, HeadlessRelationshipsPage
└── FormRelationshipsPage                    // + form existence validation, default-language expansion
```

Expansion is genuinely shared: whatever the graph is rooted at, expanding a _neighbour_ always walks
from one content item to its neighbours. The new base adds an abstract `ResolveExpansionLanguageId()`
because the Forms application has no language switcher — the form page returns the default content
language, which is also the language its graph is built in.

**Builder.** New entry point `IContentItemRelationshipGraphBuilder.BuildForForm(int formId)`. Query shape:

1. `ContentItemObjectReferenceInfo` → distinct `SourceCommonDataID` for the form (indexed FK).
2. `ContentItemCommonDataInfo` `WhereIn(those ids).WhereTrue(IsLatest)`, projected to id / item id /
   language only, grouped to **one row per content item** (a form embedded in several language variants
   of one page is one node, matching how the client keys nodes — by content item GUID, not by language).
3. Cap, then the usual batch loads for the surviving items: content items, content types, language
   metadata, locations, permissions.
4. The two wide `VisualBuilder*` columns are read **last and only for the capped set**, purely to label
   the edges with the widget and property the form sits in.

The root node is the form, built by the same `CreateFormItem` that #13's forward direction now shares —
`Kind = GraphNodeKind.FORMS`, no `ItemId`, so it stays terminal and no form id ever reaches the content
item queries or the permission evaluator. `Outgoing` is always empty: a form references nothing.

`MapItem` was lifted out of `Build`'s closure into `MapContentItem(ItemMappingContext, int)` so both
entry points project nodes identically. Pure mapping, no queries — the batch loads stay in the callers.

**Cap.** `FORM_USAGE_ITEM_LIMIT = 200` content items, applied before any per-item cost is paid. The true
total is reported through a new `ContentItemRelationshipGraph.Truncations` collection
(`Direction` / `ShownItemCount` / `TotalItemCount`); the client renders `Showing 200 of 517 referencing
items.` in a bottom-centre panel. Counts are of **items**, not edges — one item can be reached by several
edges, and the cap is per item. The collection is deliberately a collection, so **#20a can populate it
per direction for content items without another model change.**

Truncation keeps the lowest content item ids: deterministic, but arbitrary — #20b's ordering and
pagination work applies here too.

**Permissions.** `IContentItemGraphPermissionEvaluator.GetViewableItemIds` is called once for the capped
item set, exactly as `Build` does; `ContentItemGraphPermissionEvaluator` itself was **not touched**.
Inaccessible items follow the #4 decision — node shown, `IsRestricted` set, no admin or live link, no code
name, location named only by kind, not expandable — so an editor can see that _something_ references the
form without being handed a way into it.
The `ExpandRelationships` command inherited from the new base keeps its own per-item check, so a client
that guesses an item id gets a `ForbiddenAccessException` on this page as on any other.

**Client.** No second renderer — the existing `ContentItemRelationships` template fits unchanged. The
data shape is the same `ContentItemRelationshipGraphDto`, `forms` is already in `kindLabels`
(`RelationshipNode.tsx`), and a root node with no `itemId` already renders terminal. The only additions
are the `truncations` field on the DTO and the panel that renders it.

**Tests.** +7 (`FormRelationshipGraphTests.cs`), suite 92 → **99 passing, 0 failed**. They cover
`CreateMissingFormGraph`, the truncation reporting, and `MatchFormSources` (edge labelling, case-insensitive
code name match, other forms ignored, missing configuration yields no label). The reverse-lookup query
itself is **not** covered — it needs live info providers and a populated database, the same reason
`ContentItemRelationshipGraphBuilder` has no query-level tests today.

**⚠ Verified by compiling, not by running.** `ContentItemObjectReferenceInfo` and its three columns
compile against 31.7.3, and `FormEditSection`'s `ObjectId` parameter is reused from #19 — which itself
flagged that parameter as inferred. Smoke test in the admin: open a form the DancingGoat landing page
embeds and confirm the page appears with a `Kentico.FormWidget` edge label.

### Open questions

- **Unlabelled edges.** When the reference row exists but the reader cannot place it in the JSON (a
  `[TrackFormReference]` component storing its selection in a shape the reader does not recognise), the
  edge is drawn with no label rather than dropped. Deliberate — the row is the authority — but it will
  read as a bare line.
- **Taxonomies.** Should tags get the same treatment? "Which items use this tag" is equally useful and,
  unlike forms, taxonomy references _are_ tracked in regular field values — substantially cheaper.
  Still not requested.

### [x] 19. Contextual graph pages for forms and taxonomies

**Resolved.** Two page classes plus registrations added to `ContextualContentModelGraphPages.cs`, the
only file touched. No graph-builder changes were needed, as predicted.

- `TaxonomyModelGraphPage` — mounted on `Kentico.Xperience.Admin.Base.UIPages.TaxonomyEditSection`,
  `[PageParameter(typeof(IntPageModelBinder), typeof(TaxonomyEditSection))] public int TaxonomyID`.
  Looks up `TaxonomyGUID` via `IInfoProvider<TaxonomyInfo>`, returns `taxonomy:{guid}`.
- `FormModelGraphPage` — mounted on `Kentico.Xperience.Admin.DigitalMarketing.UIPages.FormEditSection`,
  `[PageParameter(typeof(IntPageModelBinder), typeof(FormEditSection))] public int ObjectId`. Resolves
  `FormID → FormClassID → DataClassInfo.ClassName` via `IInfoProvider<BizFormInfo>`, returns
  `class:{className.ToLowerInvariant()}` matching `ClassNodeId` casing.

`Kentico.Xperience.Admin.DigitalMarketing` arrives transitively via `Kentico.Xperience.Admin` — no
csproj change. Accessibility confirmed **by compiling** (no `CS0122`/`CS0246`), not from XML docs —
applying the #6 lesson. Suite 59 passed, 0 failed.

No tests added: `ResolveNodeId` on both pages needs live info providers
(`DataClassInfoProvider.ProviderObject` is a static singleton), the same reason the existing
`ContentTypeModelGraphPage` has none. `ContentModelGraphNeighborhood` is already covered separately and
its logic is unchanged.

**⚠ Two things inferred, worth a smoke test in the admin:**

1. That `FormEditSection`'s route parameter really is the inherited int `ObjectId` — its XML docs list
   no members of its own, so this was inferred from `EditSectionPage<T>`.
2. That a form's neighbourhood is non-empty. `class:` form nodes only receive edges from
   `AddFieldEdges`, so a form with no content-item / taxonomy / object references renders an **empty
   graph** — correct behaviour, but it will look broken to a user.

**Priority: low — do after all review findings (7, 8, 11, 13, 14, 15).**

New work, not a review finding. Add "Content model graph" tabs for **forms** and **taxonomies**,
matching what already exists for content types and reusable field schemas.

**The existing pattern makes this cheap.** `ContextualContentModelGraphPages.cs` already has everything:
`ContextualContentModelGraphPage` builds the full graph and filters it to a node's neighborhood via
`ContentModelGraphNeighborhood.Filter`. Each concrete page supplies only a `[PageParameter]` and a
`ResolveNodeId()`:

```csharp
internal sealed class ReusableFieldSchemaModelGraphPage(...) : ContextualContentModelGraphPage(...)
{
    [PageParameter(typeof(GuidPageModelBinder), typeof(ReusableFieldSchemaEditSection))]
    public Guid PageIdentifier { get; set; }

    protected override Task<string?> ResolveNodeId() =>
        Task.FromResult<string?>($"schema:{PageIdentifier}");
}
```

**Both node kinds already exist in the graph** (`ContentModelGraphBuilder.cs:525-529`):

| Target   | Node id                   | Notes                                                                                                                                                                                                                                       |
| -------- | ------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Taxonomy | `taxonomy:{TaxonomyGUID}` | `TaxonomyNodeId(Guid)`; nodes built at line ~116                                                                                                                                                                                            |
| Form     | `class:{className}`       | Form classes are `class:` nodes with `Kind = GraphNodeKind.FORMS`, assigned by `ResolveNodeKind` when the class is in the `BizForm` group (line ~394). `AlternativeFormInfo` contributes field edges onto the parent class node (line ~273) |

So the graph data needs no changes — only two new page classes plus their `[assembly: UIPage(...)]`
registrations, following the two already in that file.

**What needs research:** the platform edit-section types to mount on (the equivalents of
`ContentTypeEditSection` / `ReusableFieldSchemaEditSection` for forms and taxonomies) and the right
page-parameter binder for each. Given #6, **do not assume a type is usable because it appears in
Kentico's XML docs — those include `internal` types.** Verify by compiling.

Note the form page resolves to a `class:` node, so it needs the form's **class name**, not its form id —
likely a `DataClassInfo` lookup mirroring `ContentTypeModelGraphPage.ResolveNodeId()`.

Related: #18 (taxonomy tag admin links) touches taxonomy URLs in the other builder.

---

## Checked and cleared

- `ReusableFieldSchemaModelGraphPage`'s `GuidPageModelBinder` / `Guid PageIdentifier` is correct — `ReusableFieldSchemaEditSection.PageIdentifier` really is a `Guid` (verified by reflection), despite the XML doc summary saying "Reusable schema name".
- `ContentModelGraphPageOrder.Last = UIPageOrder.NoOrder + 100` does not overflow (`NoOrder` = 2147473647).
- Duplicate relationship `Id`s from `AddRelationships` (repeated widgets) are deduplicated client-side by `edgeContentKey` — no React key collision.
- Port bump 3019→3020 is consistent across `webpack.config.js` and `appsettings.Development.json`.

## Open question — resolved

DI lifetimes for `TryAddSingleton<IContentItemRelationshipGraphBuilder>`: **settled by #4.** The builder
and the new permission evaluator are now `TryAddScoped`, because `IAuthenticatedUserAccessor` is a
per-request service and a singleton capturing it would fail ASP.NET Core scope validation.
`IContentModelGraphBuilder` remains singleton.

---

## Added 2026-09-25 — admin navigation ordering

### [x] Why `ContentModelGraphPageOrder.Last` could not put the graph tab last on a content type

Not an order problem at all. `Page<T>.ConfigureNavigation` builds navigation from
`UITreeNode.RenderOrderedChildren` filtered by `node.Navigation.Display`, so the four "Allowed…" pages —
`AllowedContentTypeBindingEditSection`, `ContentTypeChannelBindingEdit`, `ContentTypeWebPageScopeBindingEdit`
and DigitalMarketing's `ContentTypeTemplateBindingEdit` — never enter the ordered list: each is annotated
`[UINavigation(false)]`. `ContentTypeEditSection.ConfigureTemplateProperties` then appends them by hand
(`AddConditionalNavigation`), and DigitalMarketing's `ContentTypeEditSectionExtender` appends "Allowed email
templates" the same way. Both run _after_ the ordered items are materialised, so no order value can sort
after them. Verified against the internal source and against the 31.7.3 assemblies by reflection.

Fixed with `ContentTypeEditSectionNavigationExtender` (`AdminNavigationExtenders.cs`), which moves the
`content-model-graph` item to the end after the page has configured its properties. `ReusableFieldSchemaEditSection`
and `TaxonomyEditSection` do not do this, so their tabs still order correctly from the attribute alone.

### [x] The graph tab on a website channel root

`WebPageLayout.HandleNavigation` removes `preview`, `content`, `page-builder`, `usage`, `urls` and `properties`
on the root (`WebPageItemID == 0`), leaving only `root-general` (order 10000) and `root-properties` (10100).
Our tab at order 1001 therefore sorted first, and the root has no content item so the page could only error.
`WebPageLayoutNavigationExtender` now removes the item on the root, and the order moved to
`ContentModelGraphPageOrder.AfterWebPageLayoutTabs` (10200) so it is last everywhere else.

Both extenders are additive: `UITree` collects _every_ registered `PageExtender<T>` whose page type is
assignable from the node into `UITreeNode.Extenders`, so an application or another library can register its own
extender on the same page without conflict. The one caveat is that extender execution order is the order the
`PageExtenderAttribute`s are discovered in, so a third-party extender that _appends_ a navigation item on
`ContentTypeEditSection` and happens to run after ours would land after the graph tab.

### [x] `dotnet format --verify-no-changes` is not clean at `HEAD`

Resolved: verified on pushed HEAD `bca6960` with `dotnet format Kentico.Xperience.ContentModelGraph.slnx
--verify-no-changes --exclude ./examples/**` — exit code 0, no diagnostics. The earlier detached-worktree
check at `6395b59` reported nine files; the two analyzer diagnostics noted afterward do not reproduce now.
Note that `git stash` round-trips rewrite `.cs` files to CRLF on this machine (`core.autocrlf=true`),
which `.editorconfig`'s `end_of_line = lf` can report as thousands of `ENDOFLINE` errors; re-running
`dotnet format` afterwards restores them.

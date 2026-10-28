# Metadata-driven content targeting and scope

The handoff spec for targeting content at players by metadata, and for scoping
what a user sees and may change by the same metadata. Assembled under
[DXME-215](https://radianttechnology.atlassian.net/browse/DXME-215) from the
closed tickets on the
[DXME-204](https://radianttechnology.atlassian.net/browse/DXME-204) wayfinder
map, the same way
[DXME-201](https://radianttechnology.atlassian.net/browse/DXME-201) assembled
[`time-zone-scheduling.md`](time-zone-scheduling.md) from the DXME-159 map.

**This is the document a builder implements from.** The map at
[`docs/maps/dxme-204-metadata-content-targeting.md`](../maps/dxme-204-metadata-content-targeting.md)
is an index and behaves like one: 22 charting premises, 14 of them amended in
place, pointing at 19 closed child tickets for the detail. Read the map only to
find out why something is the way it is. Read this document to find out what to
build.

**Every section cites the ticket that decided it. The ticket's resolution comment
holds the reasoning; this document holds the decision.** Where a later ticket
overturned an earlier one, only the surviving decision appears here, with the
correction noted so nobody reopens it from the older ticket. That matters a great
deal on this map, because a lot was overturned: the manifest cache variant key
was designed and then deleted, the owning group went from derived to picked, the
uploader's metadata preview went from reworked to removed, and the count of
inclusion predicates went from three to nine to ten.

Baseline for every file and line reference is `dev` at `f5c201d`, the commit the
DXME-205 audit and the DXME-206 survey both ran against, except where a later
ticket states its own. Line numbers drift; treat them as a starting point and
confirm the symbol.

## Two mechanisms, never conflated

- **Content targeting** decides which assets reach which devices. Playlist
  locations only. Airline states and counter walls are outside it (section 8.17).
  So is anything a location bundle schedules, because a bundle never reaches a
  device (section 10.6). A Playlist location has no displays, so nothing reaches
  it through a display (section 10.7).
- **Content scope** decides which assets a user can see, and **content
  ownership** decides which assets she may change. Scope follows the access rule
  wherever one exists, which includes the Asset Library.

They share one evaluator and differ in exactly three places, enumerated in
section 3.7. Targeting is asset-side against device-side; scope is user-side
against asset-side. Matching a user's scope does not grant control; ownership
does.

## What is true of the data today

Stated once, here, because several decisions below are affordable only because of
it and a build session will otherwise re-argue them
([DXME-206](https://radianttechnology.atlassian.net/browse/DXME-206),
[DXME-209](https://radianttechnology.atlassian.net/browse/DXME-209)).

- **68 access rules, all** `full`. Zero `partial`, zero `view`,
  `MetadataFilterKey` NULL on every one. So the whole targeting and scope path is
  dormant, the approval bypass in section 2 is latent rather than leaking, and
  every scope behaviour in this spec arrives latent.
- **4 device metadata rows across 236 devices.** 206 of 237 devices have no
  metadata authoring surface at all.
- **318 live assets**, 104 metadata rows, and **no asset carries more than one
  key**, so the ALL combine mode has no data exercising it. 26 asset rows name
  keys their location does not declare.
- `tblUserMetadata` is **empty**. `tblUserGroupMetadata` holds 12 rows, all
  `videoWallAssignments`, which is not scope metadata.
- **17 registry definitions**, 7 of which carry any data.
- **Exactly one Layout location** (1038, 8 zones) and **two Playlist locations**
  declaring metadata fields against **eight Asset Library locations** doing the
  same.

**Dev cannot speak for production on volume.** Dev holds 4 to 5 assets per
location against a working figure of 100
([DXME-177](https://radianttechnology.atlassian.net/browse/DXME-177)). Section 15
authors fixtures rather than relying on dev.

## Sections

1. [Vocabulary](#1-vocabulary)
2. [Current state, and what is deleted](#2-current-state-and-what-is-deleted)
3. [The evaluator](#3-the-evaluator)
4. [Schema and migrations](#4-schema-and-migrations)
5. [The rule builder](#5-the-rule-builder)
6. [Preview](#6-preview)
7. [Upload identity and ownership](#7-upload-identity-and-ownership)
8. [The cache boundary: publication and delivery](#8-the-cache-boundary-publication-and-delivery)
9. [The metadata registry](#9-the-metadata-registry)
10. [Scope surfaces](#10-scope-surfaces)
11. [Layout zones](#11-layout-zones)
12. [Content packages](#12-content-packages)
13. [Re-tagging and orphaned tags](#13-re-tagging-and-orphaned-tags)
14. [Code changes by file](#14-code-changes-by-file)
15. [Test plan](#15-test-plan)
16. [Release shape and open items](#16-release-shape-and-open-items)

Sections 1 to 10 are the twelve the assembling ticket asked for, in its order.
Sections 11 to 13 cover three tickets the ticket body predates. Section 14 is the
one a build session works from directly.

---

## 1. Vocabulary

Mirrored into [`CONTEXT.md`](../../CONTEXT.md) under a new "Content metadata"
section. The terms below caused real misunderstandings during the map, which is
the glossary's own bar for inclusion. Sources:
[DXME-210](https://radianttechnology.atlassian.net/browse/DXME-210),
[DXME-211](https://radianttechnology.atlassian.net/browse/DXME-211),
[DXME-212](https://radianttechnology.atlassian.net/browse/DXME-212),
[DXME-216](https://radianttechnology.atlassian.net/browse/DXME-216),
[DXME-222](https://radianttechnology.atlassian.net/browse/DXME-222).

**Content targeting**: deciding which assets reach which devices, by comparing
the asset's metadata values against the device's. Playlist locations only.

**Content scope**: deciding which assets a user can see, by comparing the
asset's metadata values against the user's. Follows the access rule wherever one
exists.

**Content ownership**: deciding which assets a user may change. A separate
question from scope: matching a scope does not grant control. Recorded as
`TblAsset.UploadedByUserId` and `TblAsset.UploadedByUserGroupId`.

**Player**: the targeting subject. Targeting evaluates `tblDeviceMetadata`,
which hangs off a device, so the subject is a player and **never a Display**.
`TblDisplay` is a different table carrying its own separate metadata key. Use
"Player" in UI copy, in tests and in tickets.

**Participating key**: a metadata key named in a location's
`TblLocationMetadataRule` rows that also exists in `TblMetadataDefinition`.
Participation is row presence; there is no flag.

**Shared**: the badge for content carrying no value for a participating key, and
therefore reaching every player at the location. The user-facing name for
untagged content.

**Uploading as**: the uploader's identity control. It names an **identity**, one
per matching `partial` access rule, never a role and never a persona. The picked
identity supplies the tag values and becomes the owner.

**Orphaned tag**: an asset metadata row whose key the location does not declare.
A legitimate, supported, visible state, not corruption. Re-adding the key
restores its targeting exactly.

**Reach**: how many of a location's players would receive a given asset. Shown
live on the upload form as values are picked.

**Preview**: always the content preview. The Layout page's zone geometry view is
a **zone layout map** and is renamed; it is not a preview of anything.

**Access levels**: shown to users as **Full**, **Scoped** and **View only**,
while the database keeps `full`, `partial` and `view`. Do not surface the
database words.

**Target content by metadata**: the exact label of the per-location rollout
switch. It gates targeting only, never scope.

---

## 2. Current state, and what is deleted

From [DXME-205](https://radianttechnology.atlassian.net/browse/DXME-205),
amended to ten predicates by
[DXME-208](https://radianttechnology.atlassian.net/browse/DXME-208) and to nine
ported plus one deleted by
[DXME-222](https://radianttechnology.atlassian.net/browse/DXME-222). The full
call-site tables are on branch
`research/metadata-targeting-predicate-audit` at
`docs/research/metadata-targeting-predicate-audit.md`.

### 2.1 Ten inclusion predicates, nine ported and one deleted

**Write "ten".** The map said three at charting, the audit found nine, DXME-208
found the tenth, and DXME-222 deleted one of them rather than porting it. A build
session that copies an older count will miss a call site.

| # | Predicate | Location | Disposition |
|---|---|---|---|
| 1 | `ShouldIncludeFileByMetadataAsync` | `SignageController.cs:1817-1843` | Ported. **Never reads approval**: this is the bypass. |
| 2 | `ShouldIncludePlainPlaylistFileAsync` | `SignageController.cs:1856-1876` | Ported. |
| 3 | `ShouldIncludeFileInManifest` | `SignageController.cs:1878-1884` | Ported. Legacy sidecar path. |
| 4 | `ShouldIncludeAssetInManifestAsync` | `SignageController.cs:1793-1810` | **Deleted.** Zero call sites, dead code. |
| 5 | `IncludeByMetadataAsync` | `EffectiveContentResolver.cs:291-350` | Ported. Duplicates the approval bypass. |
| 6 | `IncludePlainApprovedAsync` | `EffectiveContentResolver.cs:291-350` | Ported. |
| 7 | `IncludeBySidecar` | `EffectiveContentResolver.cs:291-350` | Ported. |
| 8 | `PlaylistManifestRules.ShouldInclude` | `PlaylistManifestRules.cs` | Ported. The shared leaf of both sidecar paths. |
| 9 | `LocationContentApiController.GetAssets` | `LocationContentApiController.cs:194-227` | Ported. Emits `CanEdit` / `CanDelete` **from scope rather than ownership**, which section 7 changes. |
| 10 | `RunAsFilterModal.MatchesMetadata` | `RunAsFilterModal.razor:137` | **Deleted with its modal** (section 6.5). Not ported. |

Predicate 4 is dead and deleted. Predicate 10 is the only live one that is
deleted rather than ported: DXME-222 removed the surface it served, so there is
nothing left for it to decide. Everything else collapses onto the single
evaluator in section 3.

Predicate 10 was also prior art. It already implemented the matrix semantics of
values OR'd within a field and fields AND'd across, and it was the only
case-insensitive comparison in the product. Both properties survive in the
evaluator; the code does not.

### 2.2 Four ways the ten predicates disagree today

All four are resolved in section 3 rather than inherited.

| Divergence | Today | Resolution |
|---|---|---|
| Untagged polarity | Predicate 10 **excludes** untagged content under a constraint; every device-side predicate **includes** it. The same asset previews as hidden and plays as visible. | The device side wins. `UntaggedContent` defaults to `all`. Section 3.4. |
| Case sensitivity | Predicate 10 is case-insensitive. The other nine are ordinal case-sensitive and untrimmed. | Predicate 10's behaviour wins. Section 3.2. |
| Approval fused in | Fused into some predicates, absent from others, and absent from the two that matter. | Removed from the evaluator entirely and ANDed at targeting call sites. Section 3.6. |
| One key only | Cross-field AND exists only in predicate 10. The manifest filters on a single `metadataFilterKey`. | All participating keys, on every path. |

### 2.3 The approval bypass, and why it is latent

`ShouldIncludeFileByMetadataAsync` never touches `_approvalService` on any
branch, and `IncludeByMetadataAsync` duplicates the bypass, so the preview
reproduces the defect rather than exposing it. It is **latent, not leaking**,
because it only runs when an access rule carries a `MetadataFilterKey` and all 68
rules have it NULL.

Section 3.6 turns it into a visible one-line omission at a call site rather than
a hidden branch inside shared logic, and
`CallSite_Targeting_ApprovalDisabled_TargetingStillApplies` in section 15 is its
regression guard.

Do not confuse it with the **live** approval leak on the layout zone path, which
is a different defect and is section 11.2.

### 2.4 The `HotelNo` clean cut

One release, no dual path, no migration tool, no recovery path by design
([DXME-218](https://radianttechnology.atlassian.net/browse/DXME-218)).

`HotelNo` is 29 references across 10 files, of which **five are live behaviour**;
the other 24 are comments, parameter names, log templates and suggestion lists.
Deleted in one release:

- The sidecar reader's hardcoded XPath at `EffectiveContentResolver.cs:336` and
  `MetadataCacheService.cs:189`.
- All three literal `"HotelNo"` fallbacks at `SignageController.cs:763`, `:843`
  and `:1669`. Section 11 removes two of them on its own account; this cut takes
  the third.
- A **second hardcoded key** the map never named: the `zipCode` five-digit throw
  at `DeviceMetadataService.cs:112-118`.
- The **eight dev rows** in `tblAssetContentMetadata`, all valued `67890`, at
  locations 1044 and 1046. One `DELETE` beside the code deletion, not a
  migration. Seven of the eight sit on a Playlist location, so leaving them would
  orphan live targeting tags against a key in neither the registry nor any
  participating key set.

`HotelNo`'s registry row is already gone (the `MetadataDefinitionId = 7` gap).

**No production sweep was run and none is needed.** DXME-218 closed that gap on
the map owner's authority rather than by sampling: `HotelNo` was demo only and
never reached a customer database. Recorded because a later session will
otherwise go looking for a production connection, and **no production Director
connection is configured for agent sessions.**

### 2.5 The legacy playlist components, already deleted

[DXME-219](https://radianttechnology.atlassian.net/browse/DXME-219) landed this
ahead of the spec, on branch
`task/dxme-219-delete-legacy-playlist-components` at `996ca32`. It is a
precondition of migration 3, not a parallel change.

- `PlaylistLocation_Radiant.razor` is deleted. It was referenced by nothing and
  was the **only** legacy caller of `GetMetadataFilterKeyForLocationAsync`, so
  deleting it alone fully unblocks dropping `MetadataFilterKey`.
- `PlaylistLocation_Omnivex.razor` is deleted too, on a scope call by the map
  owner rather than as a step on this route. It referenced no metadata anything.
  It was the registered component for **seeded location type 5**, "Playlist
  Location - Moxie", so type 5 goes with it: out of `director-seed.sql`, out of
  the SLC patch script, and out of existing databases via migration
  `RemoveMoxiePlaylistLocationType`, which deletes the row **only where no
  location references it**.

Two facts established there that a build session needs:

- `Location.razor:398` dispatches on `ComponentTypeName` **by reflection**, so a
  missing component fails **silently** with a blank page rather than breaking the
  build. Deleting a component is not compiler-checked.
- **No admin surface writes** `ComponentTypeName`. The repo's own seed and patch
  scripts are its only writers.

### 2.6 Everything else deleted

Collected here so the deletion list is in one place. Each is justified where it
is decided.

| Deleted | Why | Section |
|---|---|---|
| `TblLocationAccessRule.MetadataFilterKey` | Retired for its `partial` use, **replaced** by `ViewScope` for its `view` use. | 4.4 |
| `GetMetadataFilterKeyForLocationAsync` | Reads the dropped column. Deleted, not generalised. | 4.4 |
| `ContentMetadataService.AutoSetMetadataOnUploadAsync` | Sole write-path reader of the dropped column, and the silent `.First()` tagging that Uploading As replaces. | 7.1 |
| `ResolvedAccess.MatchingRuleId` | Written in six places, read nowhere. | 7.3 |
| `partialRules.FirstOrDefault()` at `LocationAccessService.cs:180` | Silent first-wins. | 7.3 |
| `ApprovalChainService`'s private matcher | Silent first-wins, and it disagreed with the uploader about who the user is. | 7.6 |
| `RunAsFilterModal` and `MatchesMetadata` | The uploader's preview stops being a targeting surface. | 6.5 |
| `DeviceMetadataEditor.razor` | Dead code. The live editor is already registry-backed. | 9.4 |
| `CustomMetadataFieldDef` and `LocationExtendedData.CustomMetadataFields` | Folded into `TblLocationMetadataRule`. | 4.2 |
| The silent purge in `SaveCustomMetadataFields` | Removing a key stops destroying asset tags. | 4.5 |
| The `.json` exemption on the layout zone path | It is why web content in a zone is never filtered. | 11.3 |
| `ShouldIncludeAssetInManifestAsync` | Zero call sites. | 2.1 |
| The manifest cache **filter component** | There is no variant key. | 8.2 |
| "Any device" in the preview subject | A mock player with nothing set is the honest version. | 6.2 |
| The preview's **Run** button | Already dead in effect; two missing `@bind:after` kept it alive. | 6.6 |

---

## 3. The evaluator

From [DXME-207](https://radianttechnology.atlassian.net/browse/DXME-207). This
section is the spec fragment written at
`docs/spec/metadata-evaluator-truth-table.md` on branch
`spec/metadata-evaluator-truth-table`, embedded here as it was written for. That
branch never lands: it exists only as a handoff, and section 3 is now the copy of
record.

One evaluator serves both mechanisms. They differ in three places only, and each
difference is an explicit parameter or a call-site concern rather than a second
function.

### 3.1 Why the per-key result is not a boolean

Every ambiguity on this subject came from forcing each participating key to
answer yes or no, then asking the location's ALL / ANY toggle to combine verdicts
that did not mean the same thing. "This key does not match" and "this key has
nothing to say" are different claims, and a boolean cannot distinguish them.

Each key returns one of three verdicts:

- **Match**: this key supports including the asset.
- **NoMatch**: this key argues against including it.
- **Abstain**: this key places no constraint on this asset.

Abstain is what makes untagged content, unconfigured keys and unpopulated
subjects expressible without any of them pretending to be a match or a mismatch.
It also gives the diagnostic preview its reason text for free: the evaluator
returns the per-key verdict list, so section 6 can say which key excluded an
asset instead of reporting a bare false.

### 3.2 Inputs and comparison

Per location:

- `Combine`: `All` or `Any`. One toggle, shared by targeting and scope.
- Per participating key `K`:
  - `UntaggedContent`: `all` or `none`. Governs assets holding no value for `K`.
  - `UntaggedPlayers`: `all`, `untaggedOnly` or `none`. Governs subjects holding
    no value for `K`.

Per evaluation:

- `AssetValues(K)`: a set, possibly empty, possibly many.
- `SubjectValues(K)`: a set. A device holds zero or one value. A user holds her
  own values unioned with the values of every group she **currently** belongs to.
- `BlankSubjectPolicy`: `ApplyKeyRules` for targeting, `AlwaysAbstain` for scope.

Participating keys are the keys named in the location's rules that also exist in
`TblMetadataDefinition`. The sentinel values `__all__` and `__none__` are not keys
and are skipped before evaluation. Keys an asset carries that the location does
not name are ignored entirely.

**Values are compared case-insensitively, whitespace-trimmed, ordinal.** Ordinal
rather than culture-sensitive so that locale-specific casing rules cannot change
which assets play. Each side is materialized as a set under that comparer, so an
asset holding both `Green` and `green` contributes one value, not two. The
evaluator never throws on such a pair and never double-counts it, which matters
because the rule builder's counters are computed from these sets.

**The evaluator never splits a value on any character.** `Green,Red` in one cell
is one value containing a comma, as far as the evaluator is concerned. Splitting
here would reserve the comma permanently and would make it impossible to target a
value that legitimately contains one. The existing CSV cells are split into rows
by migration 2 (section 4.3), which uses the registry's declared options as its
oracle. The evaluator sees only the result.

### 3.3 Stage 0: the subject gate

Evaluated once per subject, before any key is examined. Returns `ExcludeAll` or
`Proceed`.

**Targeting.** If any participating key sets `UntaggedPlayers = none` and the
device holds no value for that key, the device receives zero assets at this
location. The gate is OR'd across keys and **it beats the matrix**: another key
returning Match does not rescue the device.

"This device receives nothing" is a claim about the device's eligibility, not
about key `K`, so it cannot be expressed as a per-key verdict without breaking
`Any` mode. Modelling it as a gate is also what resolves the apparent
contradiction between `UntaggedContent = all` and `UntaggedPlayers = none` for an
untagged asset on an untagged device. The two settings are not the same kind of
setting: the asset setting describes **reach** and the device setting describes
**eligibility**, and reach cannot extend to a device that is not eligible. The
gate wins, and the rule builder says so in words beside the setting.

**Scope.** A `partial` or `view` access rule whose subject resolves to zero values
across every participating key is a **configuration error, not a permission
grant**. That subject sees nothing, and the access rule editor refuses to save
the state in the first place. Without this guard a scoped user holding no values
would abstain on every key and see everything, which turns Scoped into Full by
omission.

The gate never runs for `full` rules, because scope is not evaluated for them at
all.

### 3.4 Stage 1: the per-key truth table

`A` is `AssetValues(K)`, `S` is `SubjectValues(K)`. `UntaggedPlayers = none` never
appears here because stage 0 consumed it.

| # | A | S | UntaggedContent | UntaggedPlayers | BlankSubjectPolicy | Verdict |
|---|---|---|---|---|---|---|
| 1 | non-empty | non-empty | any | any | either | **Match** if `A` intersects `S`, else **NoMatch** |
| 2 | empty | non-empty | all | any | either | **Abstain** |
| 3 | empty | non-empty | none | any | either | **NoMatch** |
| 4 | non-empty | empty | any | all | ApplyKeyRules | **Abstain** |
| 5 | non-empty | empty | any | untaggedOnly | ApplyKeyRules | **NoMatch** |
| 6 | non-empty | empty | any | any | AlwaysAbstain | **Abstain** |
| 7 | empty | empty | all | all | ApplyKeyRules | **Abstain** |
| 8 | empty | empty | all | untaggedOnly | ApplyKeyRules | **Match** |
| 9 | empty | empty | none | all | ApplyKeyRules | **NoMatch** |
| 10 | empty | empty | none | untaggedOnly | ApplyKeyRules | **NoMatch** |
| 11 | empty | empty | all | any | AlwaysAbstain | **Abstain** |
| 12 | empty | empty | none | any | AlwaysAbstain | **NoMatch** |

Rows 1, 2 and 3 are shared by both mechanisms. Rows 4, 5, 7, 8, 9 and 10 are
targeting only. Rows 6, 11 and 12 are scope only.

Rows 11 and 12 are worth reading together. Scope abstains on a blank subject, but
the asset side is still governed by `UntaggedContent`, so an untagged asset at a
location set to `none` is hidden from a blank user rather than shown to her. **The
asset-side setting governs both mechanisms; only the subject-side handling
diverges.**

**Both defaults preserve pre-targeting playback.** `UntaggedContent = all` and
`UntaggedPlayers = all` are the coded defaults and they are the no-op pair. This
is stated here, once, in the spec's own voice. **The UI must not repeat it**: see
section 5.4.

**Multi-value matching is any-of-any.** Row 1 is set intersection in both
directions. A user holding `Concourse B` matches an asset tagged
`Concourse B, Concourse C`. There is no cover-every mode where the subject must
hold every value the asset carries. Cover-every reads tidy and behaves badly: it
hides every genuinely shared asset from everyone, and an asset tagged for two
concourses is precisely the asset both concourse managers need. The same rule
applies to asset against device, asset against user, and asset against group.

### 3.5 Stage 2: combining the verdicts

| Combine | Rule |
|---|---|
| `All` | Exclude if any key returned NoMatch. Otherwise include. |
| `Any` | Include if any key returned Match. Otherwise include only if no key returned NoMatch. |

Abstains never count either way. Both modes therefore include an asset on which
every key abstained, and **a location with targeting switched on but no rules
configured includes everything.**

Under `Any`, a key returning NoMatch does not veto: it simply fails to contribute
a yes. An asset tagged `Concourse B` and `Airline Delta` reaching a device that is
`Concourse C` and `Airline Delta` is included under `Any` and excluded under
`All`, which is the toggle's entire purpose.

**Verdicts are returned ordered by key.** They were ordered so that the manifest
variant key could hash them deterministically; that variant key no longer exists
(section 8.2), but the ordering stays, because the diagnostic preview and the
rule builder both render the list and an unstable order is a flickering screen.

### 3.6 Stage 3: what the caller adds

The evaluator is metadata-pure. It knows nothing about approval status.

**Targeting call sites AND the approval check themselves.** Today
`PlaylistManifestRules.ShouldInclude:16-19` returns true for everything when
approvals are disabled, so switching approvals off silently switches targeting off
too. Keeping approval outside the shared function means no caller can inherit
that coupling again, and it turns the section 2.3 bypass into a visible one-line
omission at a call site rather than a hidden branch inside shared logic.

**Scope call sites do not AND approval.** A scoped manager must see her own
Pending upload in the Asset Library, otherwise she cannot tell whether it
uploaded. Ownership, not approval, decides what she may do with it.

A consequence worth stating: **a scoped manager will see another manager's Pending
asset when it matches her scope.** That follows from this rule and is intended.
Its presentation is section 10.

### 3.7 The three divergences, and nothing else

These are the only points where the two mechanisms differ.

1. **`BlankSubjectPolicy`.** Targeting applies the key's `UntaggedPlayers` rule.
   Scope always abstains per key, guarded at the subject level by stage 0. A user
   scoped by concourse and unpopulated on airline is unscoped on that dimension,
   not blacked out.
2. **Approval.** ANDed by targeting call sites, not by scope call sites. Section
   3.6.
3. **The rollout switch.** The per-location "Target content by metadata" switch
   gates **targeting only**. Scope is live whenever a `partial` or `view` rule
   exists.

On the third: gating scope behind the same switch would let an admin set a user to
Scoped, save, and have that user still see everything because an
unrelated-sounding switch on another panel is off. **A scope restriction that
silently does not apply is a privilege escalation.** Scope also needs no rollout
flag, since it is already gated by the access rule's own level and every rule on
dev is `full`. The switch's label and help text name targeting explicitly so it
is not read as covering both.

Note that scope never runs on the device manifest path at all. Devices have no
user.

### 3.8 Two states the evaluator keeps visible rather than closing

Both follow the same principle: a configuration mistake should be loud, never a
dark screen.

**A file on disk with no `TblAsset` row.** Every current predicate includes these
as legacy content. They keep being included, because excluding them blacks out
legacy content the moment a location opts in. The hole is made visible instead:
the rule builder counts them and says "N files at this location have no asset
record and are not targeted."

**A key in the location's rules but absent from `TblMetadataDefinition`.**
Deleting a definition row is now warn-and-proceed rather than blocked (section
9.3), so this state is reachable. The evaluator **abstains** on that key and the
location shows a warning banner naming it. Abstaining silently would change what
devices play with no visible cause; failing closed would black out screens over a
configuration error.

### 3.9 Shape

A pure function. No I/O, no async. `PlaylistManifestRules` already has the right
instinct in the wrong shape: pure and static, but over four scalars for a single
key.

```csharp
EvaluateResult Evaluate(
    LocationRules rules,             // Combine, and per-key UntaggedContent / UntaggedPlayers
    IReadOnlyDictionary<string, IReadOnlySet<string>> assetValues,
    IReadOnlyDictionary<string, IReadOnlySet<string>> subjectValues,
    BlankSubjectPolicy blankSubjectPolicy)
```

`EvaluateResult` carries the include decision, whether stage 0 gated the subject,
and the per-key verdicts ordered by key. Callers AND approval where section 3.6
says they should.

A **separate resolver service** materializes the value sets from the database and
batches those reads. The current predicates issue two queries per file, which is
the other reason purity matters here.

Section 7.8 adds one more entry point to the same evaluator: a
one-asset-many-devices call taking values rather than rows, for the reach line on
the upload form.

### 3.10 Two latent bugs to fix rather than carry

Both sit in code this work replaces.

- The escaped versus unescaped `AssetPath` mismatch between
  `ShouldIncludeFileByMetadataAsync` (raw, `:1821`) and
  `ShouldIncludePlainPlaylistFileAsync` (escaped, `:1859`).
- The `deviceFilterValue == null` versus `IsNullOrEmpty` inconsistency inside
  `PlaylistManifestRules`.

---

## 4. Schema and migrations

From [DXME-209](https://radianttechnology.atlassian.net/browse/DXME-209): twenty
decisions, then sixteen more on reopening by
[DXME-212](https://radianttechnology.atlassian.net/browse/DXME-212), then a fifth
migration added by
[DXME-214](https://radianttechnology.atlassian.net/browse/DXME-214) and an
importer fix added by
[DXME-217](https://radianttechnology.atlassian.net/browse/DXME-217).

**Five migrations, all in the Director context.** The reopening changed no DDL.

> **Correction found while assembling this spec.** DXME-209 decision 13 and its
> migration plan, and DXME-214 decision 9, place two of the five migrations "on the
> Identity context", on the reading that `TblUserMetadata`, `TblUserGroupMetadata`
> and `TblMetadataDefinition` belong to `IdentityDbContext`. **That plan is not
> executable.** `IdentityDbContext` has no migrations folder and no model snapshot
> anywhere in the repo, nothing calls `Migrate()` on it, and in a Director
> deployment `IIdentityDbContext` resolves to `DirectorDbContext` itself
> (`src/DXSuite.Director/Services/Extensions.cs:86`); it is registered as a separate
> context only in `DXSuite.Design`, against the same physical database. All three
> tables are modelled in `DirectorDbContext`, created by the Director
> `20260622144321_InitialCreate` migration, and present in
> `DirectorDbContextModelSnapshot`. **Nothing in the substance changes**, only the
> folder and the "two contexts" framing, and the cross-context ordering constraint
> disappears because there is now one script. **Confirmed and commented on DXME-209.**
> Section 16.3 carries it alongside a second correction, to migration 5, which does
> change that migration's contents: read 4.6 before writing any of these.

### 4.1 Conventions this schema follows

Established by inspection rather than assumed, and worth stating because three of
them are not what a build session would guess.

- **Enum storage is uniform and explicit**: `varchar`, `[Unicode(false)]`,
  non-null, CLR default matching the DB default, legal values in an XML doc
  comment. There are **zero CLR enums and zero `HasConversion` calls** in the
  Director context. `TblAssetApproval.ApprovalStatus int` is the lone older
  counterexample; do not copy it.
- **`ContentPackageId` is not a universal convention.** It sits on exactly 14
  opted-in entities enumerated at `DirectorDbContext.cs:1323-1387`.
  `TblDeviceMetadata`, `TblUserMetadata`, `TblUserGroupMetadata` and
  `tblUserGroup` do not have it.
- **The `LastEditDate` + `LastEditUser varchar(50)` pair is not universal
  either.** Access rules and schedules carry it; the metadata tables and
  `TblLocation` do not.
- `TblAsset` has **no edit audit at all**. Its only bookkeeping columns are
  `CreateDate`, `IsDeleted`, `ContentPackageId`.
- **The model and the database disagree about metadata index names on every table
  this work touches, and on one table about existence.** Read against the dev
  Director database on 2026-09-15:

  | Table | Model and `InitialCreate` | Dev database | Kind | Columns |
  |---|---|---|---|---|
  | `tblUserMetadata` | `UQ_UserMetadata` | **`IX_UserMetadata_UserKey`** | unique index | `UserId, MetadataKey` |
  | `tblUserGroupMetadata` | `UQ_UserGroupMetadata` | **`IX_UserGroupMetadata_GroupKey`** | unique index | `UserGroupId, MetadataKey` |
  | `tblMetadataDefinition` | **nothing** | **`UQ_MetadataDefinition_Key`** | unique **constraint** | `MetadataKey` |
  | `tblAssetContentMetadata` | `IX_ContentMetadata_AssetKey` | same | unique index | `AssetId, MetadataKey` |
  | `tblDeviceMetadata` | `UQ_DeviceMetadata` | same | unique **constraint** | `DeviceId, MetadataKey` |

  The two that agree are the two this work does not need to change. **All three
  that disagree are ones it does.**

  **So every index change here is written as raw SQL guarded by a `sys` catalog
  lookup, never as a scaffolded `DropIndex` / `CreateIndex` by name.** Three
  distinct hazards make name-based DDL wrong: a drop naming the model's spelling
  silently does nothing where the database carries the other one; `DROP INDEX`
  fails outright against an index enforcing a unique **constraint**, which needs
  `ALTER TABLE ... DROP CONSTRAINT`; and a create by the model's name where the
  database already holds an equivalent under another name produces a **second,
  redundant** unique index.

  `20260913185502_WidenAppVersionUniqueIndexToArtifactKind.cs` is the precedent and
  the shape to copy: `IF EXISTS (SELECT 1 FROM sys.key_constraints ...)` around an
  `ALTER TABLE`, with a doc comment saying why the SQL is raw. Its own comment
  names the same class of problem, from the other side: "The index is raw SQL here
  because it exists only in the database: nothing in the EF model declares it."

  **The single-column non-unique indexes are in the model and survive**
  (`IX_UserMetadata_UserId`, `IX_UserGroupMetadata_UserGroupId`, created by
  `InitialCreate` at `:3049` and `:3028`), so lookups by user and by group still
  have an index after the composite unique ones go.
- All key columns are `CI_AS`, so key uniqueness is already case-insensitive and
  `zipcode` / `zipCode` cannot coexist.

### 4.2 `TblLocationMetadataRule`, and two columns on `TblLocation`

One row per `(LocationId, MetadataKey)`, unique on that pair.

```
TblLocationMetadataRule
  LocationMetadataRuleId  int identity PK
  LocationId              int NOT NULL  FK -> TblLocation
  MetadataKey             varchar(100) NOT NULL
  UntaggedContentPolicy   varchar(20) NOT NULL DEFAULT 'all'        -- all | none
  UntaggedPlayerPolicy    varchar(20) NOT NULL DEFAULT 'all'        -- all | untaggedOnly | none
  CreateDate              datetime NOT NULL
  LastEditDate            datetime NULL
  LastEditUser            varchar(50) NULL
  ContentPackageId        uniqueidentifier NOT NULL DEFAULT newid()
  UNIQUE (LocationId, MetadataKey)
  UNIQUE UX_tblLocationMetadataRule_ContentPackageId (ContentPackageId)
```

```
TblLocation
  TargetContentByMetadata  bit NOT NULL DEFAULT 0
  MetadataMatchMode        varchar(10) NOT NULL DEFAULT 'all'       -- all | any
```

Why each of these is the way it is:

- **`match mode` is not on the row.** ALL / ANY is location level. A location is
  where metadata rules are configured and where values are assigned to other
  subjects, but **a location never holds metadata values of its own that drive
  playback**. The row holds participation and policy only, never a value.
- **The two `TblLocation` settings are real columns, not `ExtendedData`.** The
  manifest path reads both on every poll, the rules already moved out of JSON for
  that reason, and `LocationsTab.SaveCustomMetadataFields` read-modify-writes the
  whole blob, so two settings sharing it is a live lost-update hazard.
- **The blank policies are strings with coded defaults, not nullable.** NULL would
  be a third silent meaning sitting next to the coded default.
- **The row carries the `LastEdit` pair** because it is edited configuration with
  a builder UI behind it, like `TblLocationAccessRule`, unlike the key/value
  metadata tables that are only ever written wholesale.
- **The rule table opts in to `ContentPackageId`.** An opt-in choice, not
  convention-following: it is location configuration exactly like
  `TblLocationAccessRule`, which carries it, and adding the column later to a
  populated table is a backfill. Whether the packaging subsystem registers the
  table is section 12.
- **No `SortOrder` on the row.** The builder orders by
  `tblMetadataDefinition.SortOrder` then `MetadataKey`. A per-location order is a
  second place to express the same intent and goes stale the moment a key is
  added to the registry.

**Non-Playlist locations get rows too**, carrying participation only, with the
policy columns sitting at their coded defaults. The schema must allow a location
with participating keys and no matrix. Section 10.3 says which policy columns are
actually read there.

**The fold-in and the strip happen in the same migration.** The 10 locations
carrying `customMetadataFields` (shape `[{"name":"Concourse"}]`, 11 keys total)
migrate into rows, and the property is stripped from `ExtendedData` via
`JSON_MODIFY`. Leaving the JSON orphaned was rejected: the database would hold a
stale second copy that a future reader will find and trust.

**Two deserialization paths must both be updated.** `ExtendedDataHelper` uses
camelCase with `WhenWritingNull`, while `LocationsTab.razor:425-427` and
`:469-471` use raw `JsonSerializer` with `PropertyNameCaseInsensitive`.

### 4.3 Multi-value: three index changes and a registry-gated split

**Asset side.** Drop unique `IX_ContentMetadata_AssetKey`. Create **non-unique**
`IX_ContentMetadata_AssetKey` on `(AssetId, MetadataKey) INCLUDE (MetadataValue)`,
since every query filters on that pair and now expects many rows. Create unique
`UQ_ContentMetadata_AssetKeyValue` on `(AssetId, MetadataKey, MetadataValue)`.

`MetadataValue` is `varchar(255)`, so the three-column key is 309 bytes and
indexable. Exact duplicates are forbidden because an asset tagged `Green` twice is
meaningless and would silently double-count in the rule builder's counters. Dev
has zero duplicate `(AssetId, MetadataKey)` rows, so the drop is free.

The unique index is declared as an **attribute** at `TblAssetContentTag.cs:15`,
not in the DbContext, so that is where the change lands. It is a plain unique
index on dev and the model agrees with the database on its name, so this is the
one index change on this work where scaffolded DDL would have been safe. Write it
with the same catalog guard anyway, for consistency with the other two.

**`UQ_DeviceMetadata` is a unique constraint, not an index**, so nothing may try to
`DROP INDEX` it. It is not touched here; noted only because the device side is the
one place a reader might reach for it.

**User and group side.** Drop `IX_UserMetadata_UserKey` and
`IX_UserGroupMetadata_GroupKey`, **by their database names**. `tblUserMetadata` is
empty and `tblUserGroupMetadata` holds 12 rows, so both are free today and
expensive once customers have scoped users. The user side becomes genuinely
multi-value; this is intended new capability, not a bug fix.

**Device side is a no-op.** `UQ_DeviceMetadata` on `(DeviceId, MetadataKey)`
already exists, declared at `DirectorDbContext.cs:688-690` and created by
`20260622144321_InitialCreate.cs:2625`, and dev has zero duplicates. The map's
premise that it was missing was wrong. **The device side stays single-value
permanently**: a device is one physical screen in one place, so one value per key
is a property of the thing.

**CSV becomes illegal, and the split is registry-gated.** The evaluator never
splits, so the migration must. Split a value only when its key's `FieldType` is
`multi-select` **and** every comma-separated piece matches a declared option in
`OptionsJson`. Anything else stays whole and is printed to the migration log by
`AssetId` and key.

Both dev rows pass exactly: `Colors = 'Green,Red'` and
`Concourse = 'Concourse B,Concourse C'`, both `multi-select`, all four pieces
declared. **No registry option anywhere contains a comma today**, so the oracle is
unambiguous.

An unsplittable value is then a single-valued tag that happens to contain a comma,
which the evaluator handles correctly. A quarantine table was rejected as
inventing schema to hold a problem that may have zero rows in production.

CSV is illegal afterwards. Every writer of a multi-select value changes to write
rows, and `ContentMetadataService.SetMetadataAsync` (`ContentTagService.cs:34-63`)
**stops upserting and starts reconciling a set** across its nine call sites.

**`videoWallAssignments` is deliberately excluded.** It is not in
`tblMetadataDefinition`, so the split correctly never touches it, and
`GroupVideoWallAssignments.AppliesTo` (`GroupVideoWallAssignments.cs:96-101`)
splits its own CSV. But `UserGroupMetadataService.SetMetadataAsync` is the same
`FirstOrDefaultAsync` upsert and the dropped index was the only thing guaranteeing
it found at most one row. So **the group-side service becomes key-aware**:
`GetMetadataAsync` for a key returns a list, and `videoWallAssignments` reads
`.SingleOrDefault()` so a duplicate throws loudly at the one call site that
assumes singularity rather than silently changing which walls an airline appears
on. Moving `videoWallAssignments` onto its own column belongs to the Ticket
Counter Scheduler, not to this work.

**One write path does not go through `ContentTagService` and must be fixed
separately.** `ContentPackageBundleImporter.cs:631` matches on `AssetId` alone and
is changed to match on `(AssetId, MetadataKey)`
([DXME-217](https://radianttechnology.atlassian.net/browse/DXME-217)). This closes
a real gap: dropping the asset index removes the only database-level single-value
guarantee, and the type-driven cardinality rule in section 7.2 is an **editor**
rule, so the importer was the one path that could write a second value for a
`list` key.

### 4.4 Ownership columns, and what happens to `MetadataFilterKey`

```
TblAsset
  UploadedByUserId       int NULL  FK -> tblUser       ON DELETE NO ACTION
  UploadedByUserGroupId  int NULL  FK -> tblUserGroup  ON DELETE NO ACTION
```

Both real FKs, both `NO ACTION`, copying `FK_ContentMetadata_User` exactly.
`NO ACTION` blocks deleting a user who owns content, which is already how
`tblUserMetadata` and `tblLocationAccessRule` behave, so it adds no new failure
mode. `SET NULL` would silently promote scoped-owned content to full-access-only,
which is what NULL means here, and that should require an explicit act rather
than a cascade.

**Why the existing column was not reused.** `TblAssetApproval.UploadedByUserId int
NOT NULL` already exists (`TblAssetApproval.cs:32`), reached through the 1:1 nav
at `TblAsset.cs:113-114`. Reading ownership off it would make control depend on
the approval record existing, re-entangling the two gates this work separates, and
it is null for corporate content (`TblAsset.cs:110-112`), which would leave that
content ownerless by construction. Also worth knowing before leaning on
`SetByUserId` as prior art: `SetMetadataAsync` never updates it on the update
path, so it already misreports who set the current value.

**`MetadataFilterKey` is dropped for its `partial` use and replaced for its `view`
use.** This is a correction to the original decision, which would have deleted a
live mechanism.

The column is **overloaded**. On `partial` rules it holds a metadata key, which
this work retires. On `view` rules it holds a required enum, `__none__` or
`__all__`, validated on write at `LocationAccessService.cs:57` and `:89`, surfaced
as a two-option dropdown at `LocationAccessModal.razor:174-175`, read at `:173`
and `:284`, and special-cased by `ContentPackageBundleImporter.cs:853`. It is NULL
on all 68 rules because **zero view rules exist**, not because it is vestigial.
The first view rule anyone creates makes it mandatory, and `CreateRuleAsync` would
not compile against a column it validates.

So migration 3 also adds:

```
TblLocationAccessRule
  ViewScope  varchar(20) NULL    -- none | all
```

`[Unicode(false)]`, legal values in an XML doc comment. **Nullable** because it is
meaningless on `full` and `partial` rules, which is a real absence rather than a
third silent meaning. No data migration; there is nothing to move.

### 4.5 Removing a key stops destroying asset tags

This is the load-bearing behaviour change in the schema work.

Today `SaveCustomMetadataFields` (`LocationsTab.razor:454-512`) hard-deletes every
`tblAssetContentMetadata` row for a removed key across the whole location,
silently, with no confirmation, count or undo. That purge existed only because the
JSON had nowhere to record an undeclared key.

**The purge is deleted.** An orphaned tag is an expected, supported, visible state
(section 13), and 26 exist on dev right now. Re-adding a key restores the old
targeting exactly, which is impossible today.

**Participation is then recorded by row presence, with no `IsParticipating`
flag.** With the tags safe, toggling a key off costs only its two policy values, a
trivial re-entry, while a flag would add a `WHERE IsParticipating = 1` that every
reader must not forget and whose omission is a silent targeting bug.

### 4.6 The migration plan

All five in `src\DXSuite.Director\Migrations`, in this order. See the correction
at the head of this section for why they are not split across two contexts.

| # | Name | Contents |
|---|---|---|
| 1 | `AddLocationMetadataRules` | Create `TblLocationMetadataRule`. Add `TblLocation.TargetContentByMetadata` and `.MetadataMatchMode`. Fold the 10 locations' `customMetadataFields` into rows. Strip the property from `ExtendedData`. |
| 2 | `AllowMultiValueAssetMetadata` | Drop unique `IX_ContentMetadata_AssetKey`, **then** split the registry-qualified CSV rows, **then** create non-unique `IX_ContentMetadata_AssetKey` with `INCLUDE (MetadataValue)` and unique `UQ_ContentMetadata_AssetKeyValue`. |
| 3 | `AddAssetOwnership` | Add both ownership columns with `NO ACTION` FKs. Drop `TblLocationAccessRule.MetadataFilterKey`. Add `TblLocationAccessRule.ViewScope`. |
| 4 | `AllowMultiValueUserMetadata` | Drop the unique composite index on `tblUserMetadata` and on `tblUserGroupMetadata` **by catalog lookup, not by name** (section 4.1): both are plain unique indexes on dev, so `DROP INDEX` is correct there, but the guard must cover both spellings and the constraint case. Leave the single-column `IX_UserMetadata_UserId` and `IX_UserGroupMetadata_UserGroupId` alone. Remove the two `HasIndex` declarations from `DirectorDbContext.cs:960-990` and the matching ones from `IdentityDbContext`, so no future `migrations add` resurrects either. |
| 5 | `ReconcileMetadataDefinitionKeyIndex` | **Declare the existing constraint; create one only where absent.** See below: the uniqueness already exists in the database under a name the model does not know. |

**Migration 2's internal order is load-bearing**: splitting before the drop
violates the old unique index on the second row inserted.

**Migration 4 must remove the declarations from both contexts**, even though only
one owns the migration. `TblUserMetadata` and `TblUserGroupMetadata` are modelled
in `DirectorDbContext.cs:960-990` **and** in `IdentityDbContext`, and
`UserMetadataService.cs:52-83` writes through `IIdentityDbContext`, which in a
Director deployment is `DirectorDbContext` itself. Leaving the declaration in
`IdentityDbContext` leaves `DXSuite.Design` modelling an index that no longer
exists in the database it shares.

**Migration 4 must precede any code that writes a second user-side value.** With
one script that is automatic, which is what the corrected plan buys.

**Migration 5 is a reconciliation, not an addition.** This is the second
correction found while assembling this spec, and it is the one that changes a
migration's contents rather than its folder.

DXME-214 decision 9 rests on "the registry has **no** database uniqueness on
`MetadataKey`". **That is true of the model and false of the database.**
`IdentityDbContext.cs:143-148` configures a primary key and a default date and
nothing else, the Director `InitialCreate` (`:374-391`) creates the table with a
primary key only, and **nothing anywhere in the repo declares a `MetadataKey`
index**. But the dev database carries **`UQ_MetadataDefinition_Key`, a unique
constraint on `MetadataKey`** (section 4.1), declared in neither context's model
and created by neither migration.

So the reasoning behind the decision stands and its implementation inverts:

- **Creating `UQ_MetadataDefinition` as proposed would add a second, redundant
  unique constraint** on the same single column.
- It is a unique **constraint**, so `HasIndex(...).IsUnique()` would not describe
  it and a scaffolded `CreateIndex` would not create it.
- **The migration's job is to make the model tell the truth**: declare the
  constraint that exists, and create it only where a `sys.key_constraints` lookup
  shows it absent. Databases that do have it are then correctly stamped without
  the DDL running.

The **reason** for wanting it is unchanged and worth keeping, because it is what
makes the rest safe: duplicate prevention is otherwise app-level,
`AnyAsync(d => d.MetadataKey == dto.MetadataKey)` at
`MetadataDefinitionService.cs:55-59`, which depends on collation and on every
writer going through that one method. Two rows for `Colors` with different
`FieldType` or `OptionsJson` would make it arbitrary which definition types the
value control, on a table that is now load-bearing for playback.

**Two consequences elsewhere.** The by-name key resolve on bundle import (section
12.4) named migration 5 as a precondition; **on dev that precondition is already
met**, so the resolve is unambiguous there today. And DXME-218 was asked to check
production for case-only or whitespace-only duplicate keys that would block the
migration: **wherever the constraint already exists, such duplicates are
impossible by construction**, which is the same argument DXME-217 used to retire
the equivalent check on the asset side. Confirm the constraint's presence per
database rather than sweeping for duplicates.

**On `EXEC()` wrapping**, the usual on-prem note is specifically about referencing
a **column** added earlier in the same batch. None of these does that. Migration
1's fold-in inserts into a new **table**, which SQL Server's deferred name
resolution already covers, and reads only pre-existing columns. Wrapping is
therefore not strictly required anywhere here. **Wrap migration 1's fold-in
regardless**, matching `20260814231738_AirlineContentStates.cs:40`, because
deferred name resolution is the only thing making it work and that is too subtle
to leave implicit.

**`Down()` is best-effort, and this spec says so plainly.** Migration 2 cannot
reconstitute the CSV, because it does not know which rows were joined.

**Two release-ordering constraints, neither visible from the migration files:**

- **DXME-219 lands before or with migration 3.** `PlaylistLocation_Radiant.razor`
  read the column migration 3 drops. Already done (section 2.5).
- **The upload picker lands in the same release as migration 3.** Section 16.1.

---

## 5. The rule builder

From [DXME-210](https://radianttechnology.atlassian.net/browse/DXME-210):
**variant E of five**, seven published versions. Artifact
[Mxi45WfjzxjfX8aDFx5Hzh](https://claude.ai/artifact/Mxi45WfjzxjfX8aDFx5Hzh),
variant E; source at `src/DXSuite.Director/ConfigTabs/prototype/rule-builder.html`
on branch `prototype/metadata-rule-builder` at `b4b3037`, one self-contained file.

It lives in the Locations metadata modal, which is renamed **"Content metadata"**
for every location type (section 10.3). The panel uses the StyleGuide `dx-panel`
structure.

### 5.1 The layout

One row per participating field, reading outward from the field name so the asset
side sits left and the player side sits right:

```
Field | Assets tagged | Content with no value | Players with no value | Players tagged | remove
```

A hairline on each count column carries the asset/player boundary. ALL / ANY sits
as a discrete control **above** the grid, not as a phrase built from the rows.

This is what answers the legibility question: **E stays one line per field at five
fields.** The rule-sentence variant read best at two fields and broke first at
five; the two-sided-card variant was the inverse.

The **Field** column carries a field type badge using the design system's existing
and previously unused `dx-type-badge` / `dx-type-list` / `dx-type-multi-select` /
`dx-type-string` classes.

### 5.2 Counts read "4 of 14"

One number, with a one-word sub-header. Tagged and blank always sum to the total,
so showing both spends a column on a number the reader can derive and forces a
two-line legend to say which is which.

The counts are **split by subject and by presence only**: Assets on the left of
the two settings, Players on the right. The prototype's original single column
rendered `assetsCarrying / playersBlankOn`, which put assets **tagged** beside
players **blank**: two subjects and two opposite polarities under one header.

The subject sits on the header line in header case, with what the numbers mean
beneath it as a smaller sub-header on one row. Both cells carry a tooltip spelling
the numbers out in full.

### 5.3 Three coverage counters, not two

On one confirmation step, because the rule builder's real job is showing blast
radius before you commit.

1. **Matched by no content**: players the matrix excluded.
2. **Excluded before matching**: players the stage 0 gate caught. These have a
   different fix from the first, which is why they are counted separately.
3. **Receiving content, out of the total**: the denominator. Without it the first
   two read as unqualified bad news.

Plus counted confirmations on edits:

- **Adding a key**: how many existing assets already carry it and would
  immediately become targeted. Adding such a key retroactively activates all of
  them and changes what plays on real screens, as a side effect of what looks like
  a purely additive step.
- **Removing a key**: the count, plus three statements, that the tags are kept,
  that the field stops being read here, and that re-adding the field restores the
  targeting exactly. It names **per-asset Edit metadata on the content list** as
  where those tags remain reachable (section 13).
- **Neither add nor remove saves without its count being shown.**

**Do not conflate these three coverage counters with the deleted
rows-about-to-be-destroyed counter.** That counter was briefed, then deleted when
the purge was deleted (section 4.5). Both were described in the same place
originally, which is exactly how they get merged by accident.

### 5.4 Copy rules, which generalise

Four house rules settled here. Every other surface on this map inherits them
rather than re-deciding them, and they decided at least one later ticket outright.

1. **Explanations are tooltips on the controls, not visible helper text.** The
   closed control explains the current choice; each option explains itself. This
   is what buys the one-line row.
2. **Counts read "n of total"**, never "a / b".
3. **Controls disable with a reason rather than disappearing.** A control that
   vanishes when it has nothing to offer reads as a broken screen, which is
   exactly how the Add button was first reported.
4. **Copy never dates itself.** No "today's behavior", no "as before". A defaults
   marker carries the fact instead: a **Default** pill or a `(default)` suffix.
   The pairing that both defaults preserve pre-targeting playback is stated in
   section 3.4, in the spec's own voice, **once**. An admin configuring a location
   in 2027 has no "today" to compare against, and such a sentence quietly becomes
   false rather than merely unhelpful.

Reason copy describes the **effect**: "Content with no value for this field stays
shared and reaches every player here", and "This field places no restriction on a
player that has no value for it".

**The tooltip mechanism is not specified, deliberately.** The prototype uses
native `title`, which is free and quiet but takes about a second, cannot be
styled, and **does not exist on touch devices**. Since the blank-policy semantics
now live only there, the build session either accepts that, which is defensible
for a desktop admin screen under Configuration, or names a real tooltip
component. Raise it if the surface turns out not to be desktop-only.

### 5.5 What the builder surfaces

- **Multi-select carries an explicit any-of-any tooltip**: content matches when
  any one of its values matches any one of the player's, so an asset tagged for
  two concourses reaches both. This is where a non-engineer meets section 3.4's
  rule.
- **A vanished registry key warns by name**, and the evaluator abstains (section
  3.8).
- **Undeclared tags on assets are surfaced** with key and count, stating that
  adding a rule starts targeting on them immediately.
- **Files with no asset record** are counted (section 3.8).
- **Layout usage is named where it exists.** A Playlist location can be a zone of
  a Layout location; on dev, 1044 is zone 6 of "Display Layout Control". When a
  rule filters a device to zero assets there, the result is not an empty playlist,
  it is a **blank rectangle inside a composed screen**, and the operator setting
  the rule has no way to know it is a zone. One line names the dependency, along
  the lines of "this location is zone 6 of Display Layout Control". Per-layout
  coverage numbers were considered and **rejected** as more machinery than the
  case needs.
- **The Add metadata button** is `dx-blue`, centred under the last row, 14px above
  and below. Pressing it reveals the field list in place; choosing a field goes to
  the confirmation step. When every registry field is in use it **disables with a
  reason** rather than vanishing.

### 5.6 Switched off

**The rules stay visible and editable** and the counters are replaced by one plain
statement. Collapsing the body hides the fact that rules exist.

### 5.7 Two implementation notes from the prototype

- `dxsuite.css` declares **no `background-color` and no `color` on
  `.dx-form-control`**. Bootstrap supplies both in the app, so any component
  checked against the design system alone will not look like the app. Three map
  tickets in a row hit this.
- The prototype is **one file**. An earlier two-file layout with a
  `/*__DXSUITE_CSS__*/` placeholder was deleted, and a font change at `af388fa`
  was misrecorded as fixing an unstyled render that was actually the wrong file
  being opened.

---

## 6. Preview

From [DXME-211](https://radianttechnology.atlassian.net/browse/DXME-211):
**variant A of three**, the inline diagnostic. Artifact
[S9igR74tmxQs833ZS4F6tb](https://claude.ai/artifact/S9igR74tmxQs833ZS4F6tb),
source at
`src/DXSuite.Director/Components/Modals/prototype/preview-rework.html` on branch
`prototype/metadata-preview-rework` at `9d55d18`.

This is `ContentPreviewModal`. It inherits every house rule in section 5.4.

### 6.1 With targeting off, the modal collapses to today's shape

The switch defaults off, so **this is what most locations will show**. No subject
panel, no diagnostic, no metadata tags on the rows, and **no player dropdown**,
because that dropdown's only effect today is to populate `metadataFilterValue`,
which never reaches the resolver at all. The off shape is date, time, treat
pending as approved, Preview, and the list.

**The diagnostic preview is the exception, not the baseline.**

### 6.2 The subject: two modes

A two-button **Preview as: Player / Mock player** control in the toolbar.

- **Player** picks a real player from the location and renders its metadata **read
  only**.
- **Mock player** renders the same panel with the values **editable**, so the user
  composes a player that does not exist. It is visibly a mock: orange border, a
  yellow "n of 3 fields set" badge in place of the player id, and a bar reading
  "Mock player. These values are not set on any screen. Nothing here changes a
  player." A simulation must never be mistakable for a real screen.
- Mock mode offers **"Start from"**, a player dropdown that seeds the values.
  Composing a near-miss of a real player is the common case, and typing three
  fields from scratch to get there is the wrong default.
- **The mock picker is single select per field.** Today's modal is multi-check,
  which can compose a player no screen could be, because device metadata is one
  value per key and always will be (section 4.3). This is a deliberate behaviour
  change to that surface, alongside the untagged-polarity flip in section 2.2.
- Both modes honour the date and time control.

**"Any device" is deleted.** A mock player with nothing set is the honest version
of it, and unlike "Any device" it **evaluates** the rules instead of skipping them.
On any location with a `none` or `untaggedOnly` rule that player correctly sees
little or nothing, which is exactly what the old option hid behind a list of
everything.

### 6.3 The diagnostic

**One list, excluded rows interleaved in place**, greyed, with the reason directly
underneath. The two rejected variants: plays / does-not-play columns cost
horizontal room and stop the left column reading as a playlist; a gate funnel
splits approval and targeting into counts that overlap, which is honest but is
arithmetic nobody trusts.

**Default view is unchanged**: excluded content is simply absent. "Show excluded
content" is the toggle, and it is **off by default**.

Reason wording is **one sentence per field, naming the field and both sides**:

- Mismatch: `Concourse: content is Concourse B or Concourse C, player is Concourse A`
- Multi-value renders as an `or` list on whichever side carries it.
- Blank cases **name the rule builder control** instead of restating the outcome,
  so the reader knows where to go and change it:
  `Language: content has no value, and Content with no value is set to No players`.
  The control names match section 5.1's column headers **exactly**. The four blank
  shapes are content-blank, player-blank, both-blank, and the stage 0 gate.
- **Combine mode gets its own line, and only when it earns one.** Under ANY it
  leads: "Matching any field is enough, and no field matched." Under ALL it
  trails, and only when more than one field failed: "Every field must match, so
  any one of these excludes it on its own." A single failing field under ALL needs
  no explanation.
- **The stage 0 gate is stated once, not per row.** When a key sets "Players with
  no value" to Nothing and the player is blank, that is one fact about the player,
  not a fact about each of thirteen assets. It renders as a banner and the rows
  carry no metadata reason at all.
- **Both failing gates are always shown, never just the first.** Approval and
  metadata are independent, so reporting one would send someone to fix a tag and
  rediscover approval afterwards. An asset stopped by both carries both badges,
  `Not targeted` and `Pending approval`, and both reason lines.

The reason text **is** the evaluator's per-key verdict list. It needs no second
implementation: lift `verdict()`, `evaluate()` and `liveRules()` from the
prototype, which implements section 3 exactly, so every count and every reason
string in it is computed rather than authored.

### 6.4 Three distinct empty states

Today's single string, "Nothing plays at the selected time for this identity",
conflates all three.

1. **The gate case**: "This player receives no content at this location", red,
   naming the field and saying the gate beats every other field.
2. **The matrix case**: "Nothing reaches this player", neutral, stating explicitly that this is a **valid configuration** and pointing
   at the diagnostic toggle.
3. **The not-scheduled case**, unchanged.

**Which of the last two shows is decided by targeting alone, ignoring time**
(section 8.17). The matrix case shows only when no asset passes targeting at any
time, which is why its text names no time. When at least one asset passes
targeting but none is in its window at the selected time, the not-scheduled case
shows, because that player does receive content, just not now.

### 6.5 The uploader's preview gets nothing, and `RunAsFilterModal` is deleted

From [DXME-222](https://radianttechnology.atlassian.net/browse/DXME-222).

**The uploader's playlist preview stops being a targeting surface.**
`RunAsFilterModal` is deleted outright and the preview becomes an **unfiltered
splice**, because the same page already carries a correct per-player answer one
button away, in `ContentPreviewModal`.

`RunAsFilterModal` was misread for most of the map. It is not a user preview: it is
a bare metadata **value picker** with exactly one caller,
`MediaAssetUploader.razor:419`, and it reads no user, no group, no access rule and
no ownership. "Run As" names an audience composed of metadata values, not a
person. **Scope and ownership belong to section 7 and must not drift into
Preview.**

The question the picker was answering badly moves onto the upload form as the
**reach line** (section 7.8).

Five things found in that code that the build session inherits:

- `PlaylistPreviewBuilder`'s doc comment claims a sharing with the delivery path
  that **never existed**.
- The uploader previews the **sidecar** store while delivery reads the database.
- `MediaAssetUploader.razor:866` is a live `MetadataFilterKey` caller the map
  never named.
- **The approval radios are unreachable, so Declined content plays in the preview
  today.**
- Edit mode writes the targeting value to disk **per keystroke**, sidecar only.
  Section 7.7 moves that onto the form's Save.

`locationMetadataFilterKey` dissolves into the participating key list, and every
key renders through `MetadataValueInput`, retiring the last unregistered
authoring surface.

### 6.6 Run is deleted, Preview stays

`ContentPreviewModal` already re-evaluates on every device change
(`OnDeviceSelected` calls `RunPreview`) and again before playback (`StartPlayback`
calls it, with a comment saying "so a changed selection is honored without a Run
click"). **Run survives only because `previewDate` and `previewTime` are plain
`@bind` with no `@bind:after`.** Wiring those two the same way makes Run dead.

**Preview**, which actually starts `ContentPreviewPlayer`, is the only button with
a job. It moves up beside the Player dropdown it acts on, and **disables with a
reason** when the rotation is empty.

### 6.7 Toolbar geometry

Two explicit rows: date, time, the Player / Mock player control, the player
dropdown and Preview on the first; the two checkboxes on the second. **Every
control on the toolbar is pinned to 32px.** A date input carries its own picker
button and renders taller than a select or a button at the same padding, so the
row cannot line up if the controls are left at their intrinsic heights.

---

## 7. Upload identity and ownership

From [DXME-212](https://radianttechnology.atlassian.net/browse/DXME-212) (variant
B of three) and the
[DXME-209](https://radianttechnology.atlassian.net/browse/DXME-209) reopening it
forced. Artifact
[LJoC1oBJqxgTLKebcgYAiY](https://claude.ai/artifact/LJoC1oBJqxgTLKebcgYAiY),
source at
`src/DXSuite.Director/Components/Modals/prototype/upload-identity-ownership.html`
on branch `prototype/metadata-upload-identity` at `94cbfa2`.

### 7.1 The reshape: she picks who she is uploading as

The premise was that the picker offers a scoped user her own metadata values and
she chooses among them. That is replaced by an **Uploading as** control:

1. **One entry per matching `partial` rule.** A direct user rule reads as
   "Myself"; each group rule reads as its group name. The picked entry is a rule
   id.
2. **The chosen identity's values stand alone. No union across identities.** Her
   own values remain reachable by picking "Myself". Unioning was rejected because
   it makes the resulting tags unexplainable after the fact.
3. **The picked identity becomes the owner.** `UploadedByUserGroupId` is the
   picked rule's `UserGroupId`, NULL when she picked Myself. So tagging and
   ownership come from one pick and **cannot disagree**.
4. **Full access is never prompted.** `LocationAccessService.cs:154-163` already
   resolves highest-level-wins across every matching rule, so membership of any
   full-access group already yields full access.

**State this explicitly, because it is invisible from the code: viewing unions,
uploading picks.** Effective user values for **scope** are the union of her own
and her groups' (section 3.2). Values for **tagging** come from one picked
identity with no union. Same user, same location, two value sets, two questions.

This kills two silent first-wins at once. `MediaAssetUploader.razor:1084`'s
`.First()` on values was the known one; `LocationAccessService.cs:180`'s
`partialRules.FirstOrDefault()` was not. A **third** lives at
`ApprovalChainService.cs:66`, which picks
`rules.FirstOrDefault(r => r.UserId == userId) ?? rules.FirstOrDefault(group matches)`.
All three die.

`AutoSetMetadataOnUploadAsync` (`ContentTagService.cs:86-108`) is deleted: it is
the sole write-path reader of the dropped column and it **is** the silent
`.First()` tagging this control replaces. Its interface declaration at
`IContentTagService.cs:10` goes with it.

### 7.2 Cardinality is type-driven

Not "default to all of the user's values", which was the original rule and is now
true for multi-select only.

| `FieldType` | Behaviour |
|---|---|
| `multi-select` | Every value the identity holds is populated and stays deselectable. Carries the any-of-any tooltip. |
| `list`, `string`, `int` | The identity may hold several values, but the key takes one. The first in sort order is set, marked `(Default)` in the option list, and remains selectable. Sort order does not matter as long as it is stable. |
| exactly one value available | The control **disables with the reason**. Nothing to choose. |

**Only participating keys are tagged.** A key the identity holds that the location
does not declare is **not written**. Adding a key to a location retroactively
activates every existing tag for it (section 5.3), so writing tags with no effect
today is a delayed-action hazard rather than a convenience.

### 7.3 One service owns both directions

`IUploadIdentityService`, in **Director**, alongside `LocationAccessService`:

- `GetCandidatesAsync(userId, locationId)`: one entry per matching `partial`
  rule, plus "Myself" for a direct user rule.
- `ValidateAsync(userId, locationId, pickedGroupId)`: the write-time guard,
  section 7.4.
- **The values the picked identity supplies.**

All three in one service because a validator that can reject what the picker
offered is its own bug, and computing values in the component would lose that
property on the half that produces visible tags. **Director rather than Shared**
because it is location-scoped configuration logic; putting it in Shared inverts
the existing dependency. It reaches Identity the way `LocationAccessService`
already does.

Note both contexts model `TblUserGroupMemberships` (`LocationAccessService` reads
Director's, `SessionState` reads Identity's), the same dual-modelling as the
metadata tables in section 4.6.

**`ResolvedAccess` stops collapsing.** `partialRules.FirstOrDefault()` dies, and
`ResolvedAccess` **drops `MatchingRuleId` entirely** rather than growing it into a
list, because the field is written in six places and **read nowhere**. Its "for
chain routing" XML comment is aspirational; `ApprovalChainService` routes by its
own private matcher.

**The candidate set is computed lazily, on uploader open**, not eagerly on
`ResolvedAccess`. `LoadLocationAccessMapAsync` already calls `ResolveAccessAsync`
once per accessible location at login; putting candidate lists there computes them
for every location the user will never upload to.

**The uploader does not subscribe to `LocationAccessChanged`.** One guard at write
is the whole contract, and a picker that re-shuffles under the user mid-upload is
worse than a clear failure at submit.

### 7.4 The write-time guard

The pick-to-write gap is **session length**. `userAccess` is a login-time snapshot:
`SessionState.LoadLocationAccessMapAsync` builds the map once at login,
`MediaAssetUploader` copies it into `MediaUploadConfig`, and that config rides
inside a **scoped** `UploadStateService` through navigate-away and resume
(`MediaAssetUploader.razor:699`).

Live access refresh exists but **cannot reach a pending upload**:
`LocationAccessBroadcastService` is a singleton bus and `MainLayout.razor:498`
calls `RefreshLocationAccessAsync()`, but it is per-instance with no cross-replica
reach, and refreshing `LocationAccessMap` does not rewrite a config already
captured. There is also no stale access cache to blame, because there is no access
cache: `AccessCacheStore`'s access half (`SetAccess`, `GetAccess`, `GetAccessMap`,
`ClearUser`, `ClearAll`) has **zero callers** outside the class and interface.

So the guard **re-resolves the identity, not the rule row**: the user is still a
member of the picked group **and** some rule still grants her partial access at
that location through it. Checking the rule row id was rejected as brittle, since
an admin deleting and re-creating an equivalent rule would break an in-flight
upload for no real reason. Trusting the snapshot was rejected outright.

**On failure the write fails.** Falling back to NULL is silent un-scoping. The
guard runs **before `TblAssets.Add`**, so nothing partial reaches the database, and
the staged file stays in TempFiles for existing cleanup. **The picker re-opens with
refreshed candidates** rather than dead-ending; a hard stop only when nothing
remains.

### 7.5 Nine creation sites, in three groups

The original list of four was wrong. `TblAssets.Add` appears at:

**Human picks the identity.** The picker is **identical** on all of these: same
control, same type-driven cardinality, same blocked-upload condition. Only the
surrounding modal differs.

| Site |
|---|
| `MediaAssetUploader.razor:1244` |
| `StreamAssetUploader.razor:253` |
| `TvSourceUploader.razor:312` |
| `WebContentUploader.razor:450` |
| `TemplateCreator.razor:987` |
| `MediaCreator.razor:1102` |

The Asset Library's Upload button is wired to the same `MediaAssetUploader`, so it
is covered by the first row rather than being a seventh site. The fix lives in the
uploader component, not the page that opens it.

**No human**

| Site | Behaviour |
|---|---|
| `AssetIngestService.cs:121` | `AssetIngestRequest` gains `UploadingAsUserGroupId int?`, validated by the same guard against the calling user. Omitted means user-owned. |
| `ContentPackageBundleImporter.cs:460` | Section 12.4. |

**Human, through a surface this product does not render**

| Site | Behaviour |
|---|---|
| `LocationContentApiController` `POST assets`, reached from a location bundle through `dx-location.js` | Same `AssetIngestRequest.UploadingAsUserGroupId`, but **required for any caller holding a `partial` rule at the location**: omitted, the request is refused with a 400 listing the candidates. Exactly one candidate is taken without asking, which is not derivation because there is nothing to choose. Full-access callers and API-key integrations are unaffected. Section 10.6. |

A person is present, so the "no human" default of user-owned would silently
untag a scoped upload the moment migration 3 removes `AutoSetMetadataOnUploadAsync`:
section 16.1's failure, on a path that list did not name. The bundle is not ours
to put a picker in, so the API asks instead.

**Not a creation path at all**: `CreateLayoutModal`. It creates locations, location
groups and constraints, and no asset. Remove it from the list. It is still a write
site for the participating-key list (section 10.3).

**Two negatives that are the load-bearing half:**

- **Never invent, and never derive.** Where there is no user, both columns stay
  NULL, which means full-access-only control. **No path may fall back to a system
  or first-admin user id** to keep the column non-null. And the API path must not
  derive from `caller.Access.MatchingRuleId`: that value is
  `partialRules.FirstOrDefault()`, so deriving would resurrect the very silent
  first-wins this control exists to kill, on the one path where nobody is
  watching. `LocationContentApiController.cs:398` does pass `caller.Access` for
  partial callers, so the API **has** a rule; what it lacks is a human to ask.
  That is what makes this a never-**derive** rule rather than only a never-invent
  one.
- **`StreamAssetUploader` takes `locationId ?? 0`**, so a stream asset can be
  created with no location: no participating keys, no access rule, no identity to
  pick. It gets **no picker and NULL ownership**, the one honest NULL case on a
  human-facing path.

Worth knowing: the API caller is a real `TblUser`, so a NULL group still yields an
asset owned by whatever account the integration authenticates as. That reads
correctly as corporate content, but it means an integration's uploads are editable
by that service account and full-access users only.

Leaving any of the six human sites writing NULL would mean scoped users silently
produce content only full-access users can edit, which is NULL's meaning arriving
by accident rather than by choice.

### 7.6 The picked identity routes the approval chain

Today two independent matchers disagree about who the user is: she uploads as
"Terminal Ops" while `ApprovalChainService` routes to whichever rule its own
`FirstOrDefault` finds, **preferring any direct user rule**. **The picked rule
routes the chain**, and that preference dies with the private matcher.

This is not the entanglement section 4.4 refused. That was reading ownership *out
of* the approval record; this is the identity she declared deciding which chain she
submits to, which is the coherent reading of a control called Uploading as.

**Where there is no fresh pick**, meaning resubmit, the API without an explicit
group, and the importer, **route from the stored owning group**, re-resolved to a current rule at
that location, falling back to the location default chain when the group is NULL or
no rule matches. This is what lets ownership keep its no-rule-id shape and still
give the same chain across resubmissions.

### 7.7 What ownership gates, and what it does not

**Gated**: edit, replace, delete, re-tag, **and resubmit**.

**Not gated**: approval itself.

**Group-owned content is controllable by any current member of the owning group**,
read live from `tblUserGroupMembership` (unique on `(UserGroupId, UserId)`).
Ownership pins the **group**; who is in that group is read live. That is
deliberately the mirror of the scope rule, which tracks current membership too:
the snapshot is the pick, not the roster.

**Resubmit does not re-pick.** The identity is fixed at first submission.
Re-offering the picker would let a declined asset change owner and change which
chain judges it after reviewers have commented. Its gate **moves off
`approval.UploadedByUserId`** (`Playlist.razor:570`) onto the ownership columns, so
there is one ownership notion rather than two on the same screen. This **widens
resubmit to any current member of the owning group.**

**Replacement inherits ownership and tags.** `ContentOverwriteService` copies the
replaced asset's `UploadedByUserId`, `UploadedByUserGroupId` **and its
`tblAssetContentMetadata` rows**. Taking the replacer's identity was rejected as
the closest thing to content theft this design can produce: a full-access user
replacing a scoped user's file would silently transfer ownership away from her.
Replacement changes the bytes, not who owns the content. If the old asset is
NULL-owned the new one is too.

**The tag half is a pre-existing defect this work inherits rather than causes.**
`ContentOverwriteService` copies **no** tags today, so a replace silently drops all
targeting. Stated here so it does not read as scope creep. Keeping ownership while
dropping tags would split apart the pair this section deliberately binds together.

**Tagging leaves the uploader's edit mode entirely** and moves to
`AssetMetadataEditModal` (section 13), so there is a single re-tagging surface.
That also removes the per-keystroke sidecar-only write found at section 6.5.

**No ownership backfill. The exclusion is unconditional.** Existing assets get
NULL. Backfilling from `TblAssetApproval.UploadedByUserId` was considered, because
resubmit's gate moving onto the ownership columns means existing declined assets
become resubmittable only by full-access users. Rejected: it cannot backfill the
group at all, it splits pre-existing content on the arbitrary line of whether
approvals happened to be on, and **all 68 access rules are `full`, so no scoped
user exists to lose an ability**. The regression is theoretical. This was once
conditional on production evidence; **no production sweep will ever be run**
(section 2.4), so the condition cannot be met and the exclusion is now flat.

### 7.8 The blocked upload, and the reach line

**The upload is blocked** when a participating key has **no value the identity can
supply** and that key's `UntaggedContent` is `none`. Primary button disabled,
reason on the button and visible in the footer.

The condition is identical on the Playlist and the Asset Library, because
`UntaggedContent` governs scope as well as targeting; only `BlankSubjectPolicy`
diverges and that governs a blank **subject**, not a blank asset. **The copy
differs because the consequence does:**

- Playlist: `Service Center: You have no value for this field, and this location
  sends content with no value to no players. Ask an administrator to add a Service
  Center value to your account.`
- Asset Library: `... this location hides content with no value from your view. ...`
- For a group identity the subject becomes the group name and the verb agrees:
  `Concourse A Ops has no value for this field ... add a Service Center value to
  Concourse A Ops.`

Where the same gap exists but `UntaggedContent` is `all`, the field **disables with
the outcome stated** rather than vanishing, because the outcome is reassuring:
`Campaign: You have no value for this field. Content with no value plays on all
players, so this stays empty.`

**Reason strings need a subject and an agreeing verb, not an identity label.**
"Myself" is a label; the sentence subject is "You". Interpolating the label
produces "add a Service Center value to Myself".

**The reach line** replaces what `RunAsFilterModal` was answering badly. It sits on
the upload form, **live as values are picked**, and says how many of the location's
players would receive this asset. It **warns at zero and never blocks**. It runs on
the **shared evaluator**, through the new one-asset-many-devices entry point in
section 3.9 that takes values rather than rows, which is why predicate 10 is
deleted rather than ported.

### 7.9 The ownership affordance

**Variant B: reason at the control.** No badge, no strip, no panel. The explanation
sits immediately above the disabled buttons on a card, and under them in the
table's action cell. Identity renders as the first field in the same list as the
metadata fields.

Two rejected variants, both for reasons worth keeping:

- A **status strip** (band across the card, rail on the table row) sits between
  thumbnail and name, so it changes card height and names in a row stop aligning.
- **Ownership as metadata** (owner line on the card, Owner column in the table)
  fails because **an owner is not worth indicating when no `partial` rules exist at
  the location**, and a table column cannot be conditional. With every access rule
  on dev `full`, that is a column of noise on every screen in the product.

Wording:

- **Ownership surfaces only when it takes a control away.** No positive "Yours"
  marker. A full-access user owns nothing and controls everything, so a marker
  would appear on every card for no information.
- **Name the group, never the person.** `Owned by Terminal 3 Ops. It is here
  because it is also tagged Service Center SLC-A.` The second sentence names the
  actual overlap that made it visible. A person's name is both sensitive and
  unreliable once they leave.
- **NULL ownership**: `Only full access users can change this content.` Functional
  rather than factual. "No owner recorded" describes a gap and reads as a bug; this
  states the live rule and does not date itself.
- **View only**: the action row stays hidden as it is today, and the reason is
  stated **once** as a page-level banner. Nothing varies between rows, so repeating
  it on forty cards is the mistake section 6.3 corrected for the stage 0 gate.
- **The Shared badge does not collide.** It is a corner chip on the thumbnail; this
  affordance is a line in the action row.

**One deliberate departure from section 5.4's disable-with-a-reason rule**:
**Uploading as is absent entirely for a full-access user**, not disabled. That rule
exists to stop a control vanishing from an otherwise identical row. For a
full-access user the concept does not exist at all, so rendering a disabled
instance would invent a restriction in order to explain it.

`LocationContentApiController.GetAssets:194-227` already emits per-asset `CanEdit`
and `CanDelete`. It derives them from **scope**, which this section changes to
**ownership**. That divergence is the thing to design against, not to copy.

---

## 8. The cache boundary: publication and delivery

From [DXME-213](https://radianttechnology.atlassian.net/browse/DXME-213), which
dissolved its own question rather than answering it.

**There is no variant key and no hash.** The charted design was a stable hash of
the device's values for the participating keys. It is deleted. Four of the
ticket's own eight questions stopped existing with it: hash input, blank
contribution, variant bound, and eviction policy.

### 8.1 The invariant

**A publication is a function of content, never of configuration.** This belongs
in `CONTEXT.md`, because it is what makes the invalidation trigger list
**derivable** rather than remembered.

- Approval is **device-neutral** and belongs to the **publication**.
- Targeting is **device-dependent** and belongs to **delivery**.
- The clock is device-dependent and belongs to delivery, which
  [`time-zone-scheduling.md`](time-zone-scheduling.md) already established.

The publication holds approval-filtered assets, each carrying its **full** tag
set, including tags for keys the location does not declare. The rule set is read
at delivery, never baked in. So **a rule config edit invalidates no manifest.**

Two rejected alternatives, both of which would break the invariant:

- **Trimming tags to participating keys.** Smaller payload, but the publication
  would then depend on rule config, so a config edit would invalidate the
  folder-walking rebuild.
- **Moving approval to delivery too.** Contradicts section 11.

### 8.2 `BuildCacheKey` loses its filter component everywhere

The playlist path takes the same seam as the layout path, so the filter component
leaves the key on **all three paths**.

**The seam already exists on the playlist path and is already shipped.**
`ResolvePlaylistDelivery` (`SignageController.cs:1317`, called at `:850` on a
cache hit and `:994` on a fresh build) already takes the publication, copies each
asset, **strips fields that must not reach the wire** (it nulls `StartDate`,
`EndDate`, `StartTime`, `EndTime`), rebuilds a fresh `CachedManifest`, and
re-stamps `ComputeDeliveryVersion` plus `RecordDeliveredVersion` per device. The
change is **one `deviceMetadata` parameter, one `.Where` on the list it already
filters, and the tag fields nulled in the loop that already nulls four.**

Consequences: **one cache entry per location and one per display**, so the entry
count is bounded by locations plus displays rather than by metadata combinations.
No hash, no eviction policy, no variant bound. **A device metadata edit
invalidates nothing**, because the value is read live per poll (through
`PlayerPollCache.cs:66`, 60s TTL) rather than baked into a key.

Note that device metadata already reaches any decision through that 60s cache, so
the input is up to 60s stale independently of any manifest TTL.

### 8.3 The key gains a type discriminator

`BuildCacheKey`'s second component is `locationId` on the playlist and layout
paths (`:847`, `:767`) but `displayId` on the display path (`:613`, `:654`),
inside a segment spelled `:loc:`, and `_locationKeys` buckets by the same
overloaded number. **Two live consequences:**

- `StatusSelectorService.cs:77` and `:114` invalidate `display.LocationId` after
  changing a **display's** DisplayMode and default event, but that manifest is
  cached under `displayId`. The invalidation lands on a bucket that does not hold
  the entry, and `NotifyManifestBucket` (`ManifestCacheService.cs:120`) wakes the
  wrong bucket with it.
- `MediaFolderCacheService.cs:142` chains into the manifest cache by
  `locationId`, so the file watcher never reaches a display-keyed entry either.

The key becomes `manifest:display:{tenant}:{displayId}` and
`manifest:loc:{tenant}:{locationId}`; the bucket key becomes a **typed triple**.

`ContentManifestLogService.RecordIfChangedAsync:41` **already carries this
discriminator** as `"display"` / `"location"`, so the log solved a year ago what
the cache key did not. `Location.razor`'s Republish button (`:317-329`) is
correct today and stays so.

**This matters more under section 8.2, not less**: with one entry per location
serving every device there, a missed invalidation now affects every player instead
of one variant.

### 8.4 One TTL across all three paths: thirty minutes

Today: a 30s default (`ManifestCacheService.cs:15`) on the display (`:654`) and
layout (`:822`) writes, five minutes on the playlist write (`:988`). Three
different TTLs in one file is how the 30s layout default survived unexamined.

**Uniformity matters more than the number.**
[`time-zone-scheduling.md`](time-zone-scheduling.md) states that raising the TTL
to 30 minutes was rejected **solely** because the TTL was the only backstop for
approval and access-rule edits, filed that as DXME-178, and called it "explicitly
not a prerequisite". Sections 8.5 and 8.7 remove that reason, so thirty minutes is
precisely the number that spec deferred pending this one.

Two supports: under section 8.2 the entry count is bounded, so a long TTL costs
almost no memory; and `NotifyManifestBucket:120` already **pushes** players on
invalidation rather than making them wait for a poll.

**Delete the comment at `SignageController.cs:981`** claiming the TTL is
load-bearing for correctness.
[`time-zone-scheduling.md`](time-zone-scheduling.md) already instructs this and
calls any ticket reasoning from it a false premise.

**The rationale is narrower than a capacity claim.** One publication rebuild at
N=100 costs 120 to 420 ms, dominated by 200 sequential database round-trips, plus
18.6 ms of sidecar parsing memoised nowhere (`MetadataParser.ParseAllMetadata` is
constructed fresh at `SignageController.cs:857` every rebuild). Moving 5 minutes
to 30 is **6x fewer database round-trips and, in CPU terms, noise**. Take it for
database load and approval latency, not as a capacity improvement.

### 8.5 Invalidation goes in a service seam

Every write to a publication input goes through a service that invalidates after
`SaveChangesAsync`, the pattern `AssetApprovalService`, `StatusSelectorService`
and `OverrideService` already follow. **Razor components stop writing these tables
directly.**

The uncovered write surfaces this map adds are `TblLocationMetadataRule` and asset
tag writes at `ContentTagService.cs:34` and `:102`, `AssetIngestService.cs:133`
and `:137`, `AssetMetadataEditModal.razor:168`,
`MediaAssetUploader.razor:1123`, `:1254`, `:1259`, and
`WebContentUploader.razor:418`, `:460`.

**Rejected: an EF `SaveChanges` interceptor.** One hook would cover every present
and future call site, and it is the better engineering answer in a codebase that
already had one. **There is no `SaveChanges` interceptor anywhere in `src`**, and
introducing the first one on a map whose destination is a spec is the wrong place.
It also invalidates invisibly, which is hard to reason about in the field.

Two pre-existing gaps close in the same pass:
`AssetApprovalService.PurgeDeclinedAssetAsync:165` and
`WithdrawPendingForAssetAsync:419` do not invalidate today.

`MetadataCacheService.InvalidateLocation`, which has zero callers today, is wired
into the same seam.

### 8.6 The rule set gets its own per-location cache

Cleared by the same `InvalidateLocation` seam. A rule config edit invalidates
**only** this, never a manifest.

**Rejected: riding `PlayerPollFlagCache`**, which already fetches per device on a
60s TTL and already carries `DeviceMetadata` (`PlayerPollCache.cs:18`, `:66`), so
it would co-locate both operands. It loses because the rule set is per **location**
and that cache is per **device**: it would hold one copy per device at a location,
expiring independently, which is the variant explosion section 8.2 just deleted
wearing a different hat.

**Rejected: reading live per poll.** A database round trip on the hottest path in
the product.

### 8.7 After the filter key is retired, an access rule edit invalidates nothing

Today access rules are a publication input, because
`ShouldIncludeFileByMetadataAsync` reads them to resolve the filter key, and
`time-zone-scheduling.md:508` names access rules as a publication-version input.
Section 4.4 **retires** `MetadataFilterKey` and deletes
`GetMetadataFilterKeyForLocationAsync`. Afterwards access rules govern content
**scope** (user-side) and `ViewScope`, and neither decides what a device receives.

So they **drop off the trigger list entirely**, and `LocationAccessService` needs
no cache dependency. **Stated explicitly**: a trigger kept "just in case" is one
nobody can later prove is needed, and it would quietly contradict section 8.1.

**Staging constraint: this holds only after the filter key is retired**, so stage
1 in section 16.2 must not drop access-rule invalidation early.

### 8.8 The invalidation trigger list

Derived from section 8.1 rather than enumerated from memory. "Publication" is the
manifest cache; "rule cache" is section 8.6's.

| Trigger | Invalidates | Covered today? |
|---|---|---|
| Media file added / removed / changed, `.metadata` sidecar written | Publication | Yes, `MediaFolderCacheService` FileSystemWatcher → `:142` |
| Asset approved / declined / resubmitted / bulk-approved | Publication | Yes, `AssetApprovalService:120`, `:156`, `:239`, `:413` |
| Declined asset purged, pending withdrawn | Publication | **No**, `AssetApprovalService:165`, `:419`. Closed by 8.5 |
| Asset metadata tag written or removed | Publication | **No**, 10 sites listed in 8.5. Closed by 8.5 |
| Ownership columns written | Publication | **No**, new here. Covered by 8.5 |
| Display mode / default event changed | Publication (display bucket) | Aimed at the wrong bucket, `StatusSelectorService:77`, `:114`. Fixed by 8.3 |
| `TblLocationMetadataRule` participation, blank policy, ALL/ANY, targeting switch | **Rule cache only** | New. 8.6 |
| Access rule created / updated / deleted | **Nothing** | 8.7 |
| Device metadata edited | **Nothing** | 8.2, read live per poll |
| Source location content changed, at a Layout location | Publication of **every layout sourcing it** | **No**, 11.5's layout-to-sources map |
| Schedule events written through the Location Content API (`SaveSchedule`, from a location bundle) | Publication (display bucket) | **No**, the controller holds no cache reference. Closed by moving the write behind 8.5's seam, reachable once 8.3's discriminator lands. Section 10.6 |

### 8.9 The manifest log records the publication, and loses its filter component

`RecordIfChangedAsync("location", locationId, deviceMetadataFilterValue, playlistManifest)`
at `:992` records one row per distinct filter value on a table with 400-day
retention.

**Drop the `filterValue` parameter. Do not pass null**: a lingering
`string? filterValue` on the signature invites someone to refill it. **Leave the
column nullable** so existing history stays readable.

This makes the code match its own stated contract; the comment at `:990` already
says the log records the publication.

**Named loss**: per-device reality is not recorded. It is reconstructible from the
publication plus the device's metadata plus the rule set. If it ever needs
auditing, `RecordDeliveredVersion` is the per-device hook and is currently
memory-only.

### 8.10 Dead code: delete two, wire two, fix the logging

- **Delete `MatchesAnyVariant`** (`ManifestCacheService.cs:80`). Zero callers,
  warned against by name in `time-zone-scheduling.md:1522`. It answers "does any
  variant match this version", which has no meaning once there is one entry.
- **Delete `InvalidateAll`** (`:123`). Zero callers.
- **Keep `GetStatistics`** (`:138`, zero consumers) and wire it, **fixing its
  counting bug first**: `_locationKeys` (`:17`) sheds keys only on explicit
  invalidation, never on TTL expiry, so `CurrentEntries` (`:146`) over-counts.
- **Wire `MetadataCacheService.InvalidateLocation`** into the section 8.5 seam.

**Logging.** The only line printing a composed key is the eviction callback
(`:63`). Hit and miss lines still use a `{HotelNo}` placeholder (`:41`, `:47`).
The "TTL {TTL}s" line at `:76` always logs the 30s default even when the caller
passed five minutes. Add the composed key to hit, miss and set lines, rename the
stale placeholders, fix the TTL log.

`Location.razor:306` fixes itself: it reads with a hard-coded `null` filter today
and misses on any location whose devices carry a filter value, and under section
8.2 that call becomes correct by construction.

**Rejected: a cache inspector panel.** A new surface, and the destination here is
a spec. Stated rather than left silent.

### 8.11 Client-side targeting was reopened and rejected

Publish once, send the full list, let the player filter. Recorded at length
because it will come up again.

- **The player downloads everything it is told about.**
  `assetManager.ensureAssetsAvailable` (`Engage_SignageOS/src/assetManager.ts:206`)
  and `preloadAssets` (`:191`) pull every asset in the manifest to local storage
  before anything plays. The full list means **every screen stores every other
  audience's content.**
- **The evaluator would exist twice, and there is proof the second copy drifts.**
  `displayManager.ts`'s `isAssetScheduled` uses end-inclusive comparison and has
  no midnight-wrap branch, disagreeing with the server on a far simpler rule, live
  on 90 players. DXME-159 chose to **delete** it rather than fix it. And it is no
  longer twice: BrightSign is a second runtime and the Director preview is a third
  consumer needing the server-side evaluator regardless.
- **Nothing is gained, and this is the decisive one.** A client-side evaluator
  earns its keep when the answer changes while the server is out of the picture.
  That is true of the clock: midnight arrives whether or not Director is
  reachable. **It is not true of metadata**: a player's metadata is set in
  Director, so the player learns of a change only from the server. It would be
  re-deciding, from data the server handed it seconds earlier, a question the
  server already answered. Even the clock case lost: DXME-159 accepted one poll
  interval of boundary latency rather than keep a second evaluator alive.

### 8.12 The cost argument is measured, not assumed

**Always check
[DXME-177](https://radianttechnology.atlassian.net/browse/DXME-177) before
arguing cost on this work.** It is Done, resolved 2026-09-12, and it measured this
exact pass. At N=100 assets with shuffle on, the delivery pass is 357.6 us:

| Component | Cost | Share |
|---|---|---|
| Day, date and window filter | 3.4 us | **1%** |
| Weighted loop expansion | 36.1 us | 10% |
| **Delivery-version serialize + SHA256** | **288.1 us** | **80%** |
| Sort, allocation | ~30 us | 9% |

**The targeting filter is the same shape as the 3.4 us line**: a `.Where` over the
same in-memory list, comparing strings instead of times. It lands in the 1% slice,
not the 80% one. The dominant term is a hash that runs whether or not we filter.

At fleet scale: one core saturates at 2,797 requests/second at N=100 shuffled. 90
players cost **0.018% of one core** at the normal 180s wake-connected cadence, and
**0.32%** in the worst case where the wake channel is down fleet-wide and cadence
falls to the 10s floor. Three to four orders of magnitude of headroom.

**N=100 is synthetic.** Dev holds 4 to 5 assets per location. Costs are linear in
N except the expander, which is O(n squared) with shuffle on, so a 500-asset
shuffled publication is roughly 25x the pass, and at that size the **expander**
rather than the filter is what to watch.

**Measured once the evaluator landed** ([DXME-234](https://radianttechnology.atlassian.net/browse/DXME-234),
2026-09-16, `PlaylistDeliveryTests.Volume`, a development workstation, Release
build). One delivery pass through the real evaluator plus the delivery-version
hash, averaged over three players and 2,000 passes, with three participating keys
and a third of the assets untagged:

| N | Shuffle | Pass | Items delivered |
|---|---|---|---|
| 100 | off | **156 us** | 68 |
| 500 | on | **2,671 us** | 1,008 |

N=100 sits under DXME-177's 358 us, which was measured shuffled on different
hardware, so read it as "targeting did not move the pass out of its order of
magnitude" rather than as a speed-up. N=500 shuffled is about 17x N=100, inside
the 25x section 8.12 predicted for the expander, and the expanded loop is what
grew: 1,008 plays per pass.

### 8.13 Three known interactions, stated rather than left to be hit

- **[DXME-188](https://radianttechnology.atlassian.net/browse/DXME-188) is the one
  thing that could reintroduce a variant key.** It proposes memoising the delivery
  pass per `(tenant, locationId, filter, timeZone)`. Its key contains `filter`,
  which no longer exists, and the natural replacement is the device's metadata
  combination: **the variant hash section 8.2 just deleted, reappearing as a
  delivery memo.** It stays rejected, and DXME-177 agrees it is explicitly not
  needed at current scale. But state the shape: **if the delivery pass ever does
  need memoising, the memo key is a metadata combination and it belongs at the
  delivery layer, never in the publication cache.** That distinction is what keeps
  section 8.1 intact.
- **[DXME-187](https://radianttechnology.atlassian.net/browse/DXME-187): there is
  no single-flight guard on publication rebuild.** `TryGetManifest` returns false,
  the caller rebuilds, and nothing stops N concurrent requests each paying 120 to
  420 ms. **Section 8.2 changes this risk's shape**: today a stampede is per
  filter variant, but with one entry per location every device at a location misses
  at the same instant and stampedes together. Fewer miss events, each wider.
  Section 8.4's thirty-minute TTL makes them rarer and each one costlier.
  Pre-existing and off this map.
- **The rebuild's round-trip count should fall.** Those 200 round-trips are `2N`
  because `ShouldIncludeFileByMetadataAsync` and
  `ShouldIncludePlainPlaylistFileAsync` each await a `TblAssets` lookup and then a
  second await, per file, in a sequential `foreach`. Section 8.7 removes the
  access-rule half of that work from the publication build. **If the count does
  not fall, the sequential `foreach` is the thing to fix.**

### 8.14 DXME-178 is absorbed and closed

Its facts had moved. The approval half **is fixed**: `AssetApprovalService`
invalidates at `:120`, `:156`, `:239`, `:413`, and the
`LocationContentApiController` approve/decline endpoints it named no longer exist.
What was still live is covered by sections 8.5 and 8.7. The invalidation trigger
list is one list, and splitting it across two tickets guarantees the second
re-derives the whole thing.

### 8.15 This overturns three claims in `time-zone-scheduling.md` section 5

That document is an **as-built record of shipped behaviour**, not a plan: DXME-159
is built and on `dev`, five commits including
`e68c116 DXME-159: cache the playlist publication, resolve the delivery per device`.
So an unmarked contradiction in it is a trap with a long fuse. The three affected
claims, annotated in place on branch `spec/timezone-section5-superseded`:

1. "The playlist key stays `(tenant, locationId, filter)`, unchanged. The layout
   key keeps its `layoutFilterValue` component." **Overturned**, the layout half
   by section 11.4 and the playlist half by section 8.2.
2. The manifest log key `(tenantId, bucketType, bucketId, filterValue)`. **Loses
   its filter component**, section 8.9.
3. The TTL numbers. **Raised**, section 8.4, on the condition that document itself
   named.

**Unaffected, and worth stating because time zone behaviour is working and nobody
should read this as touching it:** the publication/delivery split itself (this
extends it, not overturns it); "the cache key does not gain the time zone";
publication version versus delivery version; boundary latency; the `localNow`
seam; and all of sections 3, 4, 6, 7, 8 and 9 of that document. No change to
`DeviceLocalTime`, `PlaylistWindow`, `TimeZoneCatalog`, `TimeZoneVerification`,
the `osTimeZone` emission, the rules fingerprint, the window rule, or which clock
any consumer evaluates against.

**Why that sentence stops being true is that document's own argument, one
dimension along.** Its case for removing the time zone was: once the cached
artifact is the pre-filter build, a clock component keys nothing and only
multiplies entries for identical bytes. This is the same sentence with one word
changed: once targeting moves to delivery, the cached artifact is metadata-free,
so a filter component keys nothing. It was a statement of what was true before
targeting moved, not a principle.

**The residual risk is that we edit working code, not that we change its
semantics.** Removing a component from a cache key only ever merges entries, never
splits them, and the publication is clock-free by construction, so the key change
cannot reach time-zone behaviour by any path. The regression worth guarding is the
two filters **together**, which is section 15.4's 2x2.

### 8.16 One stale fact to discard

DXME-213's own notes list, under facts worth not rediscovering, that
"`TblDeviceMetadata` has no unique constraint on `(DeviceId, MetadataKey)`, and
`GetMetadataAsync` will throw on a duplicate pair". **That is false**, inherited
from the same charting premise section 4.3 corrects. `UQ_DeviceMetadata` exists in
the EF model, in `InitialCreate` and in the dev database, verified twice by live
read. `GetMetadataAsync` cannot throw. Do not add the index and do not add a
guard.

### 8.17 Composition with schedules, dayparting and airline states

From [DXME-244](https://radianttechnology.atlassian.net/browse/DXME-244). Section 16.4 left this open.

**Targeting and the window are independent filters joined by AND.** An asset
reaches a player when it is in the publication (approved), the evaluator does not
exclude it for that player, and its window is open at that player's local time.
Neither filter reads the other's input, so their order cannot change the result;
section 15.4 guards that. **Targeting never depends on time and a window never
depends on the player.** Dayparted targeting is two assets, one per daypart, each
with its own tags. A rule that varies by time, or a window that varies by player,
would be a new design, not composition.

**Targeted-out assets are removed before `WeightedLoopExpander.Expand`**, in the
same pass as the window filter. The expander has no fixed slots: the loop is the
sum of the remaining weights, so removing an asset shortens the loop and every
remaining asset keeps its relative share. A targeted-out asset never plays as a
gap. A window-closed asset already behaves this way.

**When nothing survives, the player receives an empty manifest.** No fallback
content is introduced for Playlist locations: not per location, not per tenant,
and not borrowed from the display path's `DefaultContent`. Section 6.4 treats
"nothing reaches this player" as a valid configuration and shows it in the
preview. A fallback would hide the configuration mistake that section 3.8 requires
to stay loud. Fallback content for playlists is its own feature if it is ever
wanted.

**An unresolvable time zone skips the window, never targeting.**
`ResolvePlaylistDelivery` serves the playlist unfiltered by time when the device's
time zone does not resolve (`time-zone-scheduling.md`, fallback table). Targeting
reads no clock, so it still applies. A time zone fault must not deliver content
targeted at other players.

**Airline states are outside targeting.** Airline state slot assets and counter
content on video walls take no part in it. Targeting's subject is the player
(section 1), and that path has none: `AirlineStateResolver` picks a state per
airline and wall location, one airline cluster spans several counters, and the
counter content endpoints are anonymous. Making the wall location or the counter a
subject would redefine the player, which this spec does not do.

**Playlist-sourced layout zones are targeted without a clock.** They read no clock
today ([DXME-191](https://radianttechnology.atlassian.net/browse/DXME-191)). Section 11 targets them at delivery without
waiting for DXME-191, and DXME-191 does not take on targeting. When it adds the
window, the AND rule above covers the combination and nothing further needs
deciding.

**The preview's empty banner is decided by targeting alone**, as stated in section
6.4. Per-asset reasons are unchanged: section 6.3 already shows every failing gate
on every row.

Two defects on the airline path were found while deciding this. Both are
independent of targeting and do not wait for its release:
[Airline counter content skips the approval gate](https://radianttechnology.atlassian.net/browse/DXME-247) and
[Three airline state lookups bypass AirlineStateResolver and ignore dayparting
states](https://radianttechnology.atlassian.net/browse/DXME-248).

---

## 9. The metadata registry

From [DXME-214](https://radianttechnology.atlassian.net/browse/DXME-214) (thirteen
decisions) and [DXME-217](https://radianttechnology.atlassian.net/browse/DXME-217)
(eleven). The framing was corrected by fact several times, and the net result is
that **most of what the map asked for here already exists.**

### 9.1 The registry is the vocabulary; the rule table is the participation

Not a partition, and the map's premise must be read this way or a build session
will build the wrong thing.

Participation is **row presence in `TblLocationMetadataRule`**, so an unregistered
key can never be targeted, because nothing can reference it. **The editor's
registry constraint is therefore a guardrail** against typing `Colours` where
`Colors` participates, not a correctness gate.

**A key can be both.** Targeted at one location and injected as a URL parameter at
another. `zipCode` is the live case. The registry **already holds the injection
keys**: `zipCode`, `propertyName`, `locale` and `timezone`, the exact keys
`TblDeviceMetadata`'s own comment names as URL parameters, are all registered. The
"registry key versus free-text key" framing never matched the data.

**The data already splits by use and by location type.** The one `Colors` device
sits at location 22 "HES Content", a Playlist. All three `zipCode` devices sit at
location 32 "Director Client Player", a Card location this work excludes. That is
this section confirmed empirically.

### 9.2 A key name is stored in six places, and every live authoring surface is
already a registry dropdown

The map knew three and the ticket named a fourth. There are six. **A key-name
column is a third role** alongside tagging and participating: a **reference into
the vocabulary**.

| Store | Authoring surface | Dev rows |
|---|---|---|
| `tblAssetContentMetadata.MetadataKey` | tag editors, registry-backed | 113 |
| `tblDeviceMetadata.MetadataKey` | `ContentChannelsTab`, registry-backed | 4 |
| `tblUserMetadata` / `tblUserGroupMetadata` | settings editors, registry-backed | 0 / 12 |
| `tblLocationAccessRule.MetadataFilterKey` | `LocationAccessModal`, registry-backed | 0 non-null |
| `tblDisplay.StatusSelectorMetadataKey` | `DisplayEditModal:174-179`, registry-backed | 50 |
| `tblAccessSchedule.MetadataKey` | `LocationAccessModal:411`, registry-backed, trimmed on write | 4 |
| `tblFloorPlanIconSchedule.MetadataKey` | **none** | 0 |

**So the constraint needs no work.** The spec records the fact and commissions
nothing.

Two of these had never been on the map, and the first **outranks** the column the
ticket was about:

- **`tblAccessSchedule.MetadataKey`** flows `TblAccessSchedule.MetadataKey` →
  `ScheduleContext.MetadataKey` → `MetadataKeyOverride` → `StatusSelectorService`,
  where `:46` and `:125` read
  `metadataKeyOverride ?? display.StatusSelectorMetadataKey`. Two callers pass it,
  `StatusSelectorPage.razor:81` and `FloorPlanDisplayModal.razor:50`. It is also
  read by `LocationAccessService.cs:260` into `activeMetadataKey`, so it is
  entangled with scope resolution, not only with the selector.
- **`tblFloorPlanIconSchedule.MetadataKey`** carries a paired `MetadataValue` and
  is read at `FloorPlanService.cs:154-159` with a case-sensitive `==`. No razor
  file sets it. Zero rows.

`tblDisplay.StatusSelectorMetadataKey` was **never invisible to the registry**,
correcting an earlier handoff line on this map that said it was.

### 9.3 Deleting a registry key warns and proceeds

The existing guard is **hollow and worse than reported**.
`MetadataDefinitionService.DeleteDefinitionAsync:101` **hard-blocks** on
`usage.TotalUsage > 0`, and `GetKeyUsageInternalAsync:166` computes that total
from five counters, **three of which are hardcoded `0`** with cross-project TODOs
at `:170`, `:173` and `:174`, because `TblDeviceMetadata`,
`TblAssetContentMetadata` and `TblLocationAccessRules` live on the Director
context while the guard lives in `DXSuite.Shared/Services/Identity`. Only the user
and group counts are live, and `tblUserMetadata` is empty. **Net effect on dev:
every key deletes with no warning**, including `ticketCounterStatus` at 50
displays, 44 asset rows and 4 access schedules. `GetKeyUsageAsync`, the public
entry point, has **zero callers**.

**The hard block becomes a confirmation naming every user of the key.** This is
affordable because **deleting a definition is runtime-inert**:
`StatusSelectorService.GetContentAsync` groups by reading
`TblAssetContentMetadata` directly and never consults the registry, so 50 screens
keep working. What is lost is the display name, the option list, and the ability
to select the key again.

It would otherwise be the only hard block on this map, and it would be a trap: with
no bulk tag deletion (section 13), a key on 44 asset rows would be **permanently
undeletable** under a working hard block.

**All six stores are counted, reported in two groups**, because they fail
differently. An orphaned **tag** is invisible; an orphaned **reference** degrades a
screen. Do not flatten them into one number.

- **Tag stores**: asset, device, user, group.
- **Reference stores**: display status selector, access schedule, floor plan icon
  schedule, access rule filter key.

Worked example: `Status is used by 50 displays, 4 access schedules and 44 assets.`

**`tblLocationAccessRule.MetadataFilterKey` is counted only until migration 3
replaces it with `ViewScope`.** It is born and dies inside this work, so that
counter is optional and may be skipped if the guard lands in the same release as
migration 3.

**Removing a registry option also warns and proceeds.** The evaluator never reads
the option list, so an orphaned value is just a value that matches nothing on-list
and needs no evaluator change. The registry editor counts existing tags carrying
that value across every side and says so before saving. No data is changed and no
bulk retag tooling is required. Orphaned values also surface in the rule builder's
per-field view.

**The mechanism is registry edits orphaning existing tags, not operators typing
badly.** The only off-list value anywhere is `Category = "Category 2"` on 2 asset
rows, absent from `Category`'s option list, which was last edited 2026-08-26.

### 9.4 The contributor seam

The cross-side counts need to read Director tables from a service living in
`DXSuite.Shared/Services/Identity`, which structurally cannot.

**Each module counts its own tables and contributes the number to the registry.**
A contributor seam defined in `DXSuite.Shared`, implemented in Director,
aggregated by `MetadataDefinitionService`. `GetKeyUsageAsync` stops being dead code
and becomes the shared entry point.

**Built once, used twice**: by the key-deletion warning and by the option-removal
warning.

Rejected: adding Director DbSets to `IIdentityDbContext`, which punches through a
module boundary the codebase maintains everywhere else; and moving the registry tab
into Director, which relocates a settings screen to the wrong module and strands
the user and group counts.

### 9.5 The device authoring surface

**`DeviceMetadataEditor.razor` is deleted and a metadata section is built into
`DevicesTab`**, using `MetadataValueInput` exactly as the asset, user, group and
content-channel editors already do.

Nothing renders `<DeviceMetadataEditor />`; it is dead code, and its only
distinguishing feature is the free-text escape hatch section 9.1 says should not
exist. The only reachable device metadata authoring path is
`ContentChannelsTab.razor:328-339`, and it is **already** registry-driven: key
dropdown from `MetadataDefinitionService.GetAllDefinitionsAsync()`, value via
`MetadataValueInput` typed from the definition, and a hard block reading "No
metadata keys are defined. Add keys in the Metadata tab first." when the registry
is empty. `ContentChannelsTab` keeps its editor; both go through
`DeviceMetadataService` and the same registry so they cannot disagree.

**The reason `DevicesTab` needs one at all: 206 of 237 devices cannot be tagged by
any UI.** Device metadata is keyed by `TblContentChannel.DeviceId` and there are 31
`engage` channels on dev. `DevicesTab.razor` has no metadata column or editor. All
4 metadata-carrying devices happen to be engage channels, which is luck.

**This item is the one most likely to be dropped from an implementation-ready
spec**, since no charting premise points at it, which is why it has a row of its
own in section 14.

**The device editor warns on a missing targeted value, and shows no participation
badge.** When the device has a `LocationID` and that location targets a key the
device has no value for, the editor names the key and says what happens, because
that device otherwise falls silently into the untagged-player bucket. A badge is
decoration and would be blank for 230 devices. The rule builder's coverage
counters answer "how many of my devices are covered" from the location side; this
answers "is this device covered" from the device side, which is the question the
provisioning tech has.

**Device to location is thinner than assumed**: 230 of 237 devices have a NULL
`LocationID`, 5 have a `DisplayID`, 0 have a `SiteId`. All 4 metadata-carrying
devices are among the 7 with a location. For 97% of devices "that device's
location" does not exist, which is why the warning degrades to silence rather than
to a blank badge.

Adjacent, not decided here: the map owner intends to move content channel
functionality back into `DevicesTab` separately. This is compatible with that and
does not depend on it.

### 9.6 No entity-type scope column, and pickers show the key

**`tblMetadataDefinition` gains no `AppliesTo`.** Every picker offers all keys
sorted by `SortOrder` then `DisplayName`. An `AppliesTo` column trades a small
permanent annoyance for an occasional hard block (add a key, forget to tick
"device", the key is silently absent) on a table nobody maintains. **Revisit only
past roughly 30 keys**; there are 17.

**Pickers must show the key, not just the `DisplayName`.** `Category` (id 16) and
`Titan East Category` (id 17) both display as "Category". This applies to every
registry picker across all six stores in section 9.2, including `DisplayEditModal`
and `LocationAccessModal`'s access-schedule picker.

The registry's full column set, for reference: `MetadataDefinitionId`,
`MetadataKey`, `DisplayName`, `FieldType`, `OptionsJson`, `Description`,
`SortOrder`, `CreateDate`, `LastEditDate`, `LastEditUser`. Nothing scopes a
definition to a side, and there is no tenant column, because tenancy is
per-database.

### 9.7 Field types, and where CSV came from

**`FieldType` gains server-side validation.** It is an unvalidated free string;
`MetadataDefinitionService.cs:65` assigns `dto.FieldType` verbatim. Server-side
validation was chosen over a `CHECK` constraint.

**`MetadataValueInput`'s final `else` branch must surface the unrecognised type
instead of silently degrading it to a free-text box.**

**`MetadataValueInput` is the source of the CSV.** Its multi-select branch writes
`string.Join(",", selectedValues.OrderBy(v => v))`, comma-joined and alphabetically
ordered, which is the CSV section 4.3 splits under the registry's authority.

**Cardinality by type**, which the whole design leans on: `MetadataValueInput.razor:4`
renders `list` as a single `<select>`; only `multi-select` (`:14`) renders
checkboxes. `int` is a real supported field type (`MetadataTab.razor:113-118`) with
no key using it on dev.

**The device side renders a `multi-select` registry key as single select against
the same option list.** `MetadataValueInput` gains an **explicit single-value
mode**; this is its first caller needing one.

**Guidance for authoring**: a targeting key with a constrained value set should be
typed `list`, not `string`, which makes drift impossible by construction.

### 9.8 Comparison normalisation: trim on write, nothing else

**Today is case-SENSITIVE ordinal and untrimmed**, and **no write path normalises
anything.** Plain `==` at:

- `PlaylistManifestRules.cs:51`
- `SignageController.cs:1809` and `:1857-1865` (the key match at `:1857` is also
  case-sensitive ordinal)
- `EffectiveContentResolver.cs:300-304`
- `ScheduleAssetPanel.razor:501`, `:532`, `:558`
- `FloorPlanService.cs:154-159` (a seventh site the map's list omitted)

The only existing trims are in the registry itself and in the content-channel
**insert** branch, whose **update** branch at `ContentChannelsTab.razor:1094` does
not trim.

**Ruled: trim on write everywhere, and nothing else.** Applied identically on
asset, device, user and group writes.

- **No case folding on write.** Comparison is already case-insensitive (section
  3.2) and folding would corrupt display.
- **No numeric normalisation.** `042` and `42` are genuinely different zip codes.

**The sweep does not reach `StatusSelectorService`**, per the standing requirement
in section 9.10.

### 9.9 URL injection is unchanged

**An allowlist scoped to the location's targeted keys was designed and rejected.**
It breaks the plausible case of targeting on `Carrier` while also passing `Carrier`
to a Carrier-specific web page at the same location, and it breaks it
**invisibly**: a parameter silently no longer sent, with nothing in any UI
explaining why. Visible failure beats invisible failure, and this work should not
change injection semantics as a side effect. The overlap with targeting is a known
consequence of section 9.1.

Two things that **are** in scope as correctness cleanups:

- **The three `AppendMetadataToUrl` implementations fold into one**: the static
  helper at `DeviceMetadataService.cs:225-257`, an inline loop at
  `SignageController.cs:692-699` that unconditionally prefixes `&`, and a private
  duplicate at `SignageController.cs:1541-1553`. Every key in the dictionary
  becomes a query parameter verbatim with no allowlist.
- **Layout-zone injection moves to delivery** (section 11.6), which fixes **whose**
  values are injected rather than which. That is a correctness defect in existing
  behaviour, not a change to the rule.

**The hardcoded `zipCode` validator is deleted.** `DeviceMetadataService.cs:112-118`
throws on a value that is not five digits: a second hardcoded metadata key in the
device path. `zipCode` becomes a registry key like any other. **Name the deletion
explicitly next to the `HotelNo` sweep** so it is not mistaken for an oversight.

**A general registry validation pattern** (a nullable regex column) is the right
home for format rules but is a **separate feature outside this work**, and is
deliberately not ticketed.

### 9.10 Standing requirement: StatusSelector is unchanged

Set by the map owner on 2026-09-15 and **binding on everything in this spec.**

**StatusSelector functionality and its metadata key handling are left unchanged.**
Consequences already worked through:

- The section 9.8 normalisation sweep **skips `StatusSelectorService`**.
- The hardcoded `"Uncategorized"` group **stays** and is **not** reconciled with
  `UntaggedContent`.
- **No grouping semantics change.** An earlier recommendation that a multi-valued
  asset appear in every group it is tagged for was **withdrawn**.

**Fixes that keep the rendered result identical are inside the requirement.** One
such fix is specified:

**`StatusSelectorService.cs:182` degrades instead of throwing.**
`metadata.ToDictionary(m => m.AssetId, ...)` throws on a duplicate, so the ticket
counter page fails to load rather than rendering. It becomes a **first-value-wins
grouping**. Nothing observable changes, because `ticketCounterStatus` is typed
`list` and section 4.3's importer fix means there is always exactly one value; it
only matters in the case that should not happen, and there a working screen beats a
broken one. `TicketCounterScheduler.razor:1266` is the milder twin, a single display
label per asset where picking one of several is defensible.

**`ticketCounterStatus` is single-value because it is typed `list`, and that stays
true.** **Rejected:** any per-key single-value rule, which would make it the only
key in the system carrying one.

The requirement is not tripped by scope either: all **50** StatusSelector displays
point at exactly one library, 2072 "Generic Counter Statuses", which declares
`ticketCounterStatus`, holds 17 assets with **zero** untagged, and has **no access
rule at all**, so content scope never engages there.

`ticketCounterStatus` is a first-class registry key: id 11, `FieldType` `list`,
`DisplayName` "Status", 18 options, created 2026-03-16. The map asserted it was
unregistered; it is not. Its asset row count has drifted from 39 to 44.

**Bulk device metadata tagging is out of scope.** Players are provisioned one at a
time. Prior art if it ever returns: `OverrideEditor.razor` and
`PlayerUpdates.razor` both have working multi-select device pickers with
select-all.

---

## 10. Scope surfaces

From [DXME-208](https://radianttechnology.atlassian.net/browse/DXME-208) and
[DXME-223](https://radianttechnology.atlassian.net/browse/DXME-223).

### 10.1 Content scope filters the Asset Library

**Against the library location's own access rule, using the same evaluator.**

The Asset Library is a **location type** (`LocationTypeId 1020`, 10 on dev), not a
tenant-wide or cross-location view. `AssetLibrary.razor` loads assets by
`ConstraintId == globalConstraint.ConstraintId`, where that global constraint is
owned by the library location itself, so the surface only ever shows its own
location's assets. It carries its own `TblLocationAccessRule` rows, 6 on dev, all
`full`. "The location's participating keys" is therefore well defined there, and no
inheritance question arises.

**Inheriting scope from a host location was rejected as unimplementable**, since a
library reached directly by URL has no "came from". **Leaving it unfiltered was
rejected** because it renders unconditional Delete buttons to a `view` user.

**This is entirely new behaviour on that screen, not a modification.**
`AssetLibrary.razor` does **no** access filtering of any kind today: no
`ResolveAccessAsync`, no `UserMetadataValues`, no
`GetMetadataFilterKeyForLocationAsync`, no scope filter, no ownership gate, no
approval filter. Approval status is used only for a row badge, never to hide a row,
and Upload, Edit and Delete render unconditionally for anyone who can reach the
location. Say "new", not "changed".

Ownership gates Edit, Delete and re-tag there exactly as on the Playlist, with
section 7.9's visible affordance rather than silently disabled buttons.

**Scope goes live without a per-location flag to stage it**, because the rollout
switch gates targeting only (section 3.7). The data makes that safe today: every
access rule on dev is `full`, so the change is latent on arrival.

### 10.2 The Asset Library stays out of scope as a targeting surface, on the
corrected reason

**The map's stated reason was false.** It said the library "is never tied to
displays, so nothing plays from it". `TblDisplay.AssetLibraryLocationId` ties them
directly: on dev, **63 displays across 7 host locations point at 4 library
locations**, and library assets reach devices through three paths:

- `StatusSelector.razor`, which renders one button per asset or per metadata value
  from the linked library.
- `PanelApiController.IsSelectableAssetAsync` (`PanelApiController.cs:193`),
  gating what a panel may assign.
- `LocationContentApiController.SaveSchedule` (`:295`), which explicitly
  allowlists library assets as schedulable onto a display.

**Content in an Asset Library plays.** What actually keeps it out of targeting is a
different fact: **the link belongs to a display, and a Playlist location has no
displays.** The 7 host locations are VideoWall (51 displays), Custom (7), Panorama
(4) and Card (1), all types this destination excludes. Chasing the linkage would
drag in three excluded location types.

That a Playlist location has no displays is an invariant this work enforces, not
only a fact of today's data (section 10.7).

Also worth recording: all 44 `ticketCounterStatus` asset rows live on Asset Library
locations, feeding `StatusSelector`. The other library keys are `Category` (22
rows, Vertiv Test Library) and `Group` (2 rows, Airline Logos).

### 10.3 Where a non-playlist location's participating keys are authored

**The same screen, already.** The premise that no screen authors these for a
library was false.

`CustomMetadataFields` has **one store and two writers, both admin-side**:
`SaveCustomMetadataFields` (`LocationsTab.razor:454`) for edit, and
`CreateLayoutModal.CreateAssetLibraryLocation` (`:2867-2876`) for create. There is
no Playlist-location-config editor over that store at all; `Playlist.razor:386`
reads it and never writes it. **So the rule builder replaces the Custom Metadata
Fields modal at `LocationsTab.razor:186-231`**, in admin Configuration →
Locations.

The button that opens it is gated on
`loc.LocationType?.LinkedToDisplay == false` (`LocationsTab.razor:110-113`), which
is **not** a playlist gate. Asset Library (1020) is `LinkedToDisplay = 0`, so
**8 of 10 Asset Library locations already carry a key list against 2 of 8 Playlist
locations**. The library is the dominant author of this list, not an omission from
it.

**The gate is kept unchanged.** `ContentLog.razor:210` builds its playlist bucket
list with the identical predicate, so `LinkedToDisplay == false` is an established
repo-wide shorthand for **"this location owns its own content"**, and a
participating key list only means anything where content lives. The 27
display-linked locations that do have access rules on dev do not own content: it
comes from the display's schedule, the same reasoning that puts display-sourced
layout zones out of scope in section 11.2. Authoring a key list there would also be
inert on today's code, since none of the four consumers (`Playlist`,
`AssetLibrary`, `MediaAssetUploader`, `WebContentUploader`) renders on those types.

**The reduced shape is the left half of variant E, unchanged**: dropping the two
player-side columns from section 5.1 yields

```
Field | Assets tagged | Content with no value | remove
```

A truncation of E's structure rather than a new shape, so section 5's variant
choice is not reopened.

**Full versus reduced is selected on the component type being Playlist**, through
a new `LocationTypeConstants.IsPlaylist` helper mirroring the existing
`IsAssetLibrary`, **not** a `LocationTypeId == 7` check.
`LocationTypeConstants.cs:5-15` states that ids are not the key for built-ins;
`LocationsTab` already violates that at `:90` and `:98`, and a third instance would
deepen an inconsistency the repo has ruled against. The targeting switch cannot be
the selector, because switched off the rules stay visible and editable (section
5.6).

**Which policy columns are authored where:**

| Column | Playlist | Non-playlist |
|---|---|---|
| `UntaggedContentPolicy` | Authored | **Authored**, defaulting to `all`, with warn-and-proceed on the flip to `none` naming how many existing assets would become invisible |
| `UntaggedPlayerPolicy` | Authored | **Not authored**, stays NOT NULL at its default and is never read |

`UntaggedPlayerPolicy` is unread on a scope-only location because scope always
abstains per key at subject level (section 3.7). Making it nullable was rejected: a
third state the evaluator must handle for no gain, and a Playlist location that
later turns targeting on finds a usable value already there rather than a NULL to
migrate.

`UntaggedContentPolicy` **is** read by scope, since it governs a blank asset and
section 7.8 blocks a scoped user's upload when a participating key has no value her
identity can supply and the key is `none`. **Fixing libraries at `all` was
rejected**: the upload block is not a wart, it is the defence that makes `none`
safe going forward.

**The permissive default is forced by the data**: untagged content in a library is
real and common. Titan East Library declares `Titan East Category` and **both** its
assets are untagged; Airport - Status is 7 of 7 untagged; Luma Layer 5 of 5.

**Two copy changes, and nothing else diverges.**

1. **The modal is retitled "Content metadata" for every location type.** "Custom
   Metadata Fields" no longer describes what it holds, and one title beats a title
   that varies by type, because the reduced form is a subset of the same thing.
2. **The targeting switch is not rendered on a non-playlist location.** It gates
   targeting only, so on a location that targets nothing it would be a control that
   does nothing and a label that is a false statement about that location.

With both player-side columns gone there is no player language left on the reduced
surface, so the column headers, the type badge, the any-of-any tooltip and the
count format all carry over from variant E verbatim.

**A Layout location authors no participating keys and needs no rule row**, and the
Layout type does not show the switch at all (section 11.7). That is a narrowing of
this set, not an addition to it.

**Type-agnostic, with no type enumeration.** `TblLocationMetadataRule` is keyed by
`LocationId` alone. An enumerated list of location types would go stale the next
time a type is seeded, and the `bundle:host` types (`Custom`, `Custom Display`)
have membership that is not knowable at build time. The `LinkedToDisplay` gate is a
single capability boolean, not a type list, so it does not conflict with this.

`CustomMetadataFieldDef` is name-only: `{ Name }` (`LocationExtendedData.cs:98-101`),
a bare reference into the registry where type and options live. **Nothing is lost
moving it to the rule table.**

### 10.4 Which surfaces list a location's asset catalogue

Enumerated rather than assumed.

**At a Playlist location**: `Playlist.razor` itself and
`LocationContentApiController.GetAssets` (predicate 9). No other surface on this
route lists assets without a display attached.

**`Schedule.razor` (`/schedule/{DisplayId}`) is out of scope.** It does list a
display's location assets and resolves `userAccess`, but every navigation to it
comes from a non-Playlist location type (`CheckinBackwall`, `TVController`,
`DefaultLocation`, `DefaultLocationWithSections`, `VideowallWithMask`,
`NetJetsVideoWall`) or from the admin `LocationsTab`. **It is the first thing to
revisit if `Playlist.razor` ever grows a schedule link.**

`MediaCreator`, `TemplateCreator` and `ContentLog` do not list a location's
catalogue for selection.

### 10.5 Two defects on the Asset Library upload path

Both are code changes rather than behaviour decisions.

- **`AssetLibrary.razor` never sets `MediaUploadConfig.UserAccess`.** The field
  exists (`MediaUploadConfig.cs:39`) and the Playlist surfaces populate it
  (`Playlist.razor:966`, `:990`), but the library's config block
  (`AssetLibrary.razor:300-320`) omits it, so `userAccess` is null inside the
  uploader (`MediaAssetUploader.razor:771`) and `AutoSetMetadataOnUploadAsync`
  no-ops. **Section 7's identity flow therefore has no path into an Asset Library
  today**, and this is what opens it.
- **`AssetLibrary` loads assets by `ConstraintId` while `StatusSelectorService`
  loads the same library by `LocationId`.** Two different notions of "the assets in
  this library". They agree exactly on dev, zero divergent rows across all ten
  libraries, so nothing is broken today. **The spec must name one deliberately
  rather than inheriting whichever screen it is written against: use
  `ConstraintId`**, because that is what the Asset Library surface itself is built
  on and what the scope filter therefore attaches to, and because changing
  `StatusSelectorService` is forbidden by section 9.10.

### 10.6 Location bundles

From [DXME-245](https://radianttechnology.atlassian.net/browse/DXME-245). Section 16.4 left this open.

**A location bundle is an authoring surface, not a delivery path.** It is a web
page (a zip of HTML and JavaScript, uploaded once and hosted on any number of
`bundle:host` locations) that Director shows in an iframe on the location's page,
to signed-in users only. It never reaches a device: there is no bundle manifest,
no bundle cache entry, and nothing computed per device. The ticket's questions
about whether a bundle is a publication and what keys its cache therefore have no
subject, and section 8.1's invariant is not at risk from bundles. Bundles have been
on `dev` since 2026-07-10, so the interaction is in scope.

A bundle acts through the **Location Content API** (`dx-location.js`) with the
signed-in user's own access. That is where every real interaction lives:

1. **Bundle-hosted locations are scope-only.** `Custom` and `Custom Display` are
   not Playlist locations, so they get section 10.3's reduced rule builder with no
   targeting switch. Devices at a `Custom Display` location play through the
   **display path, which does not participate in targeting**, the same boundary as
   card zones (section 11.2). Content a bundle schedules is never targeted.
   Targeting the display path when its display belongs to a bundle-hosted location
   was rejected: it reopens a path the destination excludes.
2. **Library assets listed through a bundle are scoped by their own library.**
   `GetAssets` unions the location's assets with every display's linked Asset
   Library. Each library asset is evaluated against **that library location's**
   rules with the user as subject, exactly as section 10.1 scopes the library when
   reached by URL; a user with no rule there sees none of it. Applying the host
   location's scope to everything listed through the host was rejected, since it
   would let one location's rule decide another location's visibility. Built in the
   slice that retires `MetadataFilterKey`, where predicate 9 is already ported. The
   listing unioning all displays' libraries while the write allows only that
   display's is a separate defect,
   [DXME-250](https://radianttechnology.atlassian.net/browse/DXME-250).
3. **Uploads ask for the identity through the API** (section 7.5, fourth group).
   The `context` response carries the caller's upload candidates for the location
   and the upload takes `uploadingAs`. Blocking scoped callers from uploading
   through bundles was rejected, and so was accepting untagged scoped uploads.
4. **`SaveSchedule` is a publication write and a scope check.** It joins section
   8.8's trigger list and moves behind section 8.5's seam, in the cache stage. It
   **checks each asset against the caller's scope** with the evaluator, in the retire
   slice, so a user cannot schedule what she cannot see. It keeps its library
   allowlist (section 10.2). That neither it nor the display publication checks
   approval is pre-existing and outside targeting:
   [DXME-249](https://radianttechnology.atlassian.net/browse/DXME-249).
5. **Panels and the public channel are outside both mechanisms.** The anonymous
   panel assets API and the `public.*` bundle storage channel (DXME-60, read by
   wall zones) have **no subject**: no user for scope, no device for targeting
   (section 3.3). The panel API keeps returning the library's own tags, which a
   bundle uses to group assets in the browser.

A content package carries a location's bundle assignment by value inside
`ExtendedData` and never the bundle itself. Section 12's strip of
`customMetadataFields` from that blob leaves the assignment untouched.

### 10.7 A Playlist location has no displays

From [DXME-246](https://radianttechnology.atlassian.net/browse/DXME-246). Section 16.4 left this open as whether a Playlist location ever links an Asset Library.

**The question was asked of the wrong entity.** `AssetLibraryLocationId` is a
column on `TblDisplay`, not on `TblLocation`, and playlist delivery never reads it:
the playlist manifest and `GetLocationMediaContentAsync` read only the location's
own media folder. Library content reaches a device only through a display's
schedule events, which `SignageController` resolves by asset id alone. So the gap
section 10.2 described is not a Playlist location linking a library. It is **a
Playlist location having a display**, which would be a second, untargeted delivery
path at a location this work declares targeted, whether or not the display links a
library.

**Decided:**

1. **The invariant is that a Playlist location has no displays.** No library link
   at a Playlist location follows from it. Linking one is not a supported
   configuration. Supporting it would need a targeting story for the display path
   and would pull `Schedule.razor` in (section 10.4), a separate effort beyond this
   destination.
2. **"Playlist location" means location type 7, `Playlist`, only.** Custom types
   whose names say playlist are not Playlist locations and are not targeted:
   `Concourse Entry Playlist` (type 10) has 4 displays on dev.
3. **No environment breaks it today.** On dev the 8 Playlist locations have 0
   displays, and the map owner confirmed no Playlist location links an Asset
   Library in any environment.
4. **The content package importer is the only path that can break it, and the only
   place it is guarded.** `CreatePlaylistLocation` creates no display,
   `LocationEditModal` offers add display only for `LinkedToDisplay` types and
   bundle hosts, and no UI changes a location's type. The importer does both: it
   inserts displays under whatever location a package names
   (`ContentPackageBundleImporter.cs:558`) and overwrites an existing location's
   `LocationTypeId` (`:285`). A shared check across the UI paths was rejected
   because no current caller can trip it, and a database trigger because this
   codebase has none. **`LinkedToDisplay` is not the test**: three custom types
   flagged `false` have displays on dev (Airline Location Scheduler, CBP Messages,
   Concourse Entry Playlist).
5. **The importer refuses the whole package, before any write,** when it would
   leave a type 7 location with a display, either by inserting a display under one
   or by retyping a location that has displays. The error names the location and
   its displays. Skipping the displays with a warning, and importing while keeping
   the target's type, were both rejected: each imports something other than what
   was exported, with only a warning to show for it.
6. **The release checks it before migrating** (section 16.1).

---

## 11. Layout zones

From [DXME-216](https://radianttechnology.atlassian.net/browse/DXME-216): sixteen
decisions. Two live leaks were found here, one of them an approval leak.

### 11.1 Playlist-sourced zones participate, with the source location as subject

**The subject is the source Playlist location, not the layout**, so this does not
widen the destination past Playlist locations.

Ruling them out would make a layout **a documented way to read a targeted playlist
without its targeting**, and would force every location in a layout to be treated
as untargetable.

**The evaluator is keyed per source location**, evaluated once per distinct source
location and reused across every zone sourcing it. Targeting is a property of
(source location, device); the zone only decides where the result is painted. Zones
1 and 7 of layout 1038 both source location 1039, so zone is not the natural unit.
This is deduplication rather than semantics, but stating it keeps a twenty-zone
layout over three locations from evaluating twenty times.

### 11.2 Display-sourced (card) zones do not participate, stated as a boundary

A Card location's content is chosen by the display's schedule, not by metadata, so
there is nothing to evaluate without extending targeting to Card locations, which
the destination excludes.

**The per-device seam exists in `ResolveLayoutDeliveryAsync` and is deliberately
unused.** Writing that down stops the next reader treating the silence as an
oversight.

There is a third path the ticket did not mention: `ResolveLayoutDeliveryAsync:1029-1126`
fills display-sourced zones per device **after** the cache, with no metadata
filtering of any kind. Layout 1038 has 4 such zones against Card locations 1039,
1040 and 1046, and 1046 carries a `HotelNo` tag.

### 11.3 The live approval leak, and the `.json` exemption

**The layout path is the only one of three parallel implementations that collapsed
to a single branch.** The direct poll (`SignageController.cs:883-904`) and the
in-Director preview (`EffectiveContentResolver.cs:112-122`) both dispatch three
ways: metadata key, plain approval, legacy sidecar. `GetLocationMediaContentAsync:1738`
**always** calls the sidecar one, so it is missing two of the three branches
including the DB approval check.

**It is not a bypass, it is a cross-key mismatch.** `BuildLayoutZonesAsync:1661`
resolves the source location's filter key correctly and pulls the device's value
for it, then hands that value to a predicate comparing it against the asset's
**`HotelNo`** sidecar field. The device's value for key X is compared against the
asset's value for `HotelNo`.

**The leak has data behind it.** Layout 1038 zone 6 sources Playlist **1044**:
approval **on**, 37 assets, 6 Approved, **2 Declined**, 29 with no approval row.
The direct poll takes branch 2 (`ShouldIncludePlainPlaylistFileAsync`, real DB
check) and withholds the Declined pair. The layout zone takes the sidecar branch,
and with no sidecar `ApprovalStatus` `PlaylistManifestRules.cs:37` returns true.
**Unverified: whether those two assets carry `.metadata` sidecars on the dev
server.** Proven in the code path, unquantified in bytes.

**The approval half is closed unconditionally, whatever the targeting answer.** The
layout path adopts the same three-branch dispatch its two siblings already have,
which closes the leak as a side effect of making the paths agree. Approval and
metadata are independent gates, so this stands even if targeting were ruled out
entirely.

**The `.json` exemption is deleted.** It is incidental, not a policy: no rule
anywhere exempts web content, and two sibling implementations disagree with it. It
exists in exactly one of the three implementations; both others filter first and
branch on extension second. The sharper reason is section 11.6: an unfiltered
`.json` is then handed the device's own metadata values.

The layout path's `else` branch at `:756-764` also falls back to raw `HotelNo`
**unconditionally**, with no `enableContentApproval` guard, unlike the playlist path
at `:840`.

### 11.4 Zone targeting runs at delivery time

**This is the load-bearing decision.** Approval is device-neutral and belongs to
publication; targeting is device-dependent and belongs to delivery. Three reasons:

1. **The seam already exists and was built for this exact dependency shape.** Card
   zones are deferred because "which event is on depends on the asking device", and
   targeting depends on the asking device identically.
2. `CachedDisplayEvent` is the same publish-all-choose-at-delivery pattern already
   shipped on the display path.
3. A publication key would have to be the product of participating values across
   every source location, which for an 8-zone layout approaches **one cache entry
   per device**, defeating the cache it is keying.

**The layout cache key drops its filter component entirely: one entry per layout
location.** This also removes the `HotelNo` fallback at `:763` by making it
unreachable rather than by special-casing it.

The cache defect it fixes is independent of the predicate and larger than reported:
the layout cache key came from the **layout location's own** filter value (`:756`,
`:767`, `:822`) while content inside was filtered by each **source** location's
value. Layout 1038's own rule is `full` with NULL key, so it fell back to the
device's `HotelNo`, which no device carries, so the key was `filter:all` and every
device shared one entry. **A correct per-zone evaluator would have been erased on
the second poll.**

**The publication carries each asset's participating-key tags, and delivery projects
them away.** `ZoneContentItem` is the wire type (`BuildResponseFromCache:1434`
assigns `Zones = cached.Zones`), so tags must not survive into the response.
`ResolveLayoutDeliveryAsync` already rebuilds `ZoneDisplayContent` for card zones at
`:1101-1109`; extend it to rebuild playlist zones too, emitting clean wire items.
**No `JsonIgnore` attribute, no second type.** Re-reading tags per poll was rejected:
it puts a DB read per zone on the hot path for data that cannot change between polls
without invalidating the cache anyway.

### 11.5 Invalidating a source location invalidates every layout sourcing it

Via a **layout-to-sources map** held in memory and rebuilt when a Layout location is
saved.

**No invalidation path reaches a layout today.** Every caller invalidates the
location that changed (`AssetApprovalService.cs:44`, `MediaFolderCacheService.cs:142`,
`StatusSelectorService.cs:77`) and `ManifestCacheService` tracks keys by
`(tenantId, locationId)`, so changing an asset at 1044 clears
`manifest:loc:{t}:1044:*` and **never touches 1038**.

Relying on the TTL alone was a live option, since the layout TTL is the 30s default.
**Rejected** because `AssetApprovalService` already invalidates on approval change
specifically so declining an asset pulls it immediately, and through a layout that
guarantee silently degrades to roughly one loop of continued playback. This work
already carries one approval leak; a second by convention is not acceptable.

The bookkeeping is small because zone configs change only from `CreateLayoutModal`.

Note this makes invalidation **not symmetric with keying**: the layout path now
calls `InvalidateLocation` for locations other than the one that changed, and
whatever key shape is chosen has to keep working with `(tenantId, locationId)`
tracking.

### 11.6 The live cross-device identity leak

**The cached layout publication is not device-neutral today.** `:1753` runs
`AppendMetadataToUrl(webContent.Url, deviceMetadata, deviceId)` at **publication**
time and caches the result into `ZoneContentItem.Url`.
`DeviceMetadataService.cs:246-253` writes every metadata key/value **plus
`deviceId`** into the query string.

Combined with the single cache entry in section 11.4, **the first device to poll a
layout bakes its own metadata and its own deviceId into the web content URLs every
other device then receives.** The same file does this correctly for card zones at
`:1084`, at delivery, per device. Same feature, opposite sides of the seam.

**URL injection for playlist-zone web content moves to delivery.** This is **not**
the thing section 9.9 excluded: that rejected an allowlist deciding **which keys**
are injected, whereas this is about **whose keys** are injected, a correctness
defect in existing behaviour rather than a change to it. Injection stays exactly as
specified and simply runs against the asking device.

Record this as a **pre-existing cross-device leak this work inherits rather than
introduces**, and as the reason the publication must be device-neutral in fact and
not only in intent.

### 11.7 Two more rulings

**The source location's targeting switch governs a zone, and the Layout location
type does not show the switch at all.** A layout holds no assets and has no devices
tagged against it, so a switch there could only be set and then do nothing, which is
the invisible-failure mode this whole design rules against.

**The runtime branches on the source location's TYPE, not on
`SourceDisplayId.HasValue`.** The two coincide only by UI convention:
`CreateLayoutModal` restricts sources to Playlist or Card (`:1934`) and a Card
selection opens a display picker (`:252-274`) so only `AddZoneFromDisplay` is
reachable, while a Playlist adds a zone with no display id. But **the config is JSON
in `ExtendedData` that bundle import also writes**, so a hand-edited or imported
zone with a null display id against a Card location would read that Card location's
media folder and evaluate playlist targeting semantics against an excluded location
type. `locationsMap` is already loaded at `:1631`, so the type is in hand and the
check costs nothing. A non-Playlist source gets **no targeting and a warning log**,
matching the missing-source log at `:1654`.

### 11.8 The Layout page is not a preview surface

`LayoutLocation.razor:11` already reads "Layout Preview" but is **geometry only**:
coloured rectangles at zone positions, click-through to the source.

**It is renamed a zone layout map**, in the UI, in this spec and in `CONTEXT.md`,
and **Preview** is reserved for content, so a later reader does not conclude
section 6 already covered layouts.

**A per-zone content preview is out of scope**: a new surface rather than section
6's rework.

### 11.9 What this section does not decide

**The sidecar reader's fate.** This section removes its two `HotelNo` fallbacks
(`:763`, `:1669`) and this call site, and stops there.
`ShouldIncludeFileInManifest`, `PlaylistManifestRules.ShouldInclude` and
`MetadataCacheService`'s `.metadata` reader stay live under the direct poll's `else`
branch and `EffectiveContentResolver.IncludeBySidecar`. The clean cut is section
2.4's, one release, and `ShouldInclude` is the shared leaf the evaluator replaces
map-wide.

### 11.10 Dev data behind this

- **Exactly one Layout location**: 1038 "Display Layout Control", type 1015,
  approval off, 8 zones.
- **4 playlist-sourced zones**: locations 1041 (2 assets), 1042 (0), 1043 (0),
  **1044 (37 assets, approval ON)**. **4 display-sourced zones**: displays 1187,
  1189, 1188, 1191 against Card locations 1039, 1040, 1046.
- **All 8 access rules in the layout graph are `full` with NULL
  `MetadataFilterKey`**, so `GetMetadataFilterKeyForLocationAsync` returns null
  everywhere and every path falls through to the `HotelNo` fallback today.
- Asset tags in the graph: **1044 has `Department=IT` x8 and `HotelNo=67890` x7**;
  1046 has `HotelNo=67890` x1. The 7 + 1 split corroborates section 2.4's count of
  8 exactly. The only asset metadata anywhere in the layout graph sits on a
  zone-sourced Playlist location, and `Department` is exactly the shape of key
  targeting would use.
- Approval at 1044: 6 Approved, 2 Declined, 29 no row. At 1041: 1 Approved, 1 no
  row.
- The Director metadata table is **`tblAssetContentMetadata`**, not
  `tblContentMetadata`, and there is **no file named `ContentMetadataService.cs`**;
  the implementation is `ContentTagService` behind `IContentTagService`.

---

## 12. Content packages

From [DXME-220](https://radianttechnology.atlassian.net/browse/DXME-220): nine
decisions.

**The framing: the participating-key list already travels today.**
`LocationRecord.ExtendedData` is copied verbatim by the builder
(`ContentPackageBundleBuilder.cs:140`, tokenizing only video-wall display ids) and
**overwritten wholesale** by the importer at `ContentPackageBundleImporter.cs:298`.
`customMetadataFields` lives in that blob, so a location's declared metadata fields
promote between environments **today**. Migration 1 moves them into
`TblLocationMetadataRule` and strips the property. **Not registering the table is
therefore a silent regression, not a deferral.** Without that sentence the
registration reads as gold-plating.

Also already portable: **the asset side of targeting.**
`TblAssetContentMetadata` is one of the packaged entities
(`DirectorDbContext.cs:1381`) with its own manifest list, diff line and upsert. Only
the rule side and ownership were missing.

### 12.1 The rule table travels

`TblLocationMetadataRule` is registered in the content-package subsystem: a manifest
list, a `ComputeDiffs` line, a `LogItem` entity type, an upsert block in the pattern
every other entity follows, and expansion from promoted locations in
`ContentPackageDependencyResolver` step 2.

### 12.2 Upsert by natural key; the target keeps its own `ContentPackageId`

**Match on `(LocationId, MetadataKey)`, not on `ContentPackageId`.** The natural key
is the identity of the fact being promoted: whether `Concourse` participates at this
location is the same question regardless of which environment declared it first.
`ContentPackageId`-keyed insertion would violate the unique index whenever two
environments independently declared the same key on the same location, and the apply
runs in one `BeginTransactionAsync`, so that is a **whole-import failure** rather
than one skipped row.

**Where the natural key matches and `ContentPackageId` differs, the target row keeps
its existing id.** Adopting the incoming one can itself collide with a different
target row under `UX_tblLocationMetadataRule_ContentPackageId`. The cost is that the
two environments never converge on one id and every future import re-matches by
natural key, which is harmless because that is the match rule.

**This lands consistently with section 4.3's importer fix**, which makes the same
move on the tag table. **Two packaged tables now match on a natural key rather than
`ContentPackageId`, against the convention of every other entity block, and they do
so for the same reason.** They should land together.

### 12.3 Replace mode gains `keepMetadataRules`

Replace mode deletes direct-child records of promoted locations whose
`ContentPackageId` is absent from the bundle (`:1158-1175`), inside the same
transaction as the upserts. Rule rows need the same treatment, and **the keep-set is
built from the natural key, not `ContentPackageId`**, for consistency with section
12.2.

Participation is row presence, so a stale rule row left behind is a key that still
targets content, inside an operation whose entire contract is that the bundle is the
truth for these locations.

**What makes the deletion affordable is section 4.5**: removing a key no longer
hard-deletes the location's asset tags. The deletion costs two policy values and is
undone by re-adding the key.

### 12.4 A rule's key resolves by name against the target registry

An imported rule naming a key the target registry does not have is **skipped with a
warning**, following the `ResolveGroupAsync` pattern the access-rule block already
uses for principals.

**This is not the orphan-tag case, and the distinction is load-bearing.** Orphaned
asset **tags** are an expected, supported, visible state. But the rule builder orders
its matrix by `tblMetadataDefinition.SortOrder` then `MetadataKey`, so a rule row for
an unregistered key **renders nowhere while still participating in targeting by row
presence.** Invisible and active is the one combination this design avoids
everywhere else.

**The tag side keeps its current behaviour and does not gain this check.** State that
so section 4.5's supported orphan tags are not read later as an inconsistency.

**Uniqueness on `MetadataKey` is a precondition**, since it is what makes the
by-name resolve unambiguous. **On dev it already holds**: the constraint
`UQ_MetadataDefinition_Key` exists in the database although no model declares it
(section 4.6). So migration 5 reconciles rather than establishes, and the
dependency is on the constraint being present per database, not on the migration
having run.

**Packaging the registry itself was rejected**: an Identity-module, environment-wide
table with no `ContentPackageId`, and pulling it into a bundle whose contract is
location-scoped promotion would drag the entire vocabulary along with three
locations.

### 12.5 Both `TblLocation` columns travel, and the preview gains a targeting check

`TargetContentByMetadata` and `MetadataMatchMode` become `LocationRecord` fields.
They sit on an already-packaged entity and their sibling gate
`EnableContentApproval` already travels, so omitting them would be the anomaly.

**No device entity is in the content package manifest at all**, and
`TblDeviceMetadata` carries no `ContentPackageId`. **So targeting's device operand
can never arrive in a bundle**, and every import of a targeting-enabled location
lands in an environment whose device side is empty unless players were tagged there
by hand.

Under the coded defaults that is benign. Where the source author set a key's policy
to `none`, which is a pre-matrix device gate, **every player at that location in the
target plays nothing from the moment of import, silently.**

**So when the bundle carries a location with `TargetContentByMetadata = 1`, the
preview counts how many devices at the target location carry a value for each
participating key and reports the keys where that count is zero.** This is section
5.3's "never silently" carried across the environment boundary. The preview is
already a distinct operator-facing step with a diff UI, so there is somewhere to put
it.

**Forcing `TargetContentByMetadata = 0` on insert was rejected**: it makes a
promotion never fully promote, and the update path would still overwrite, so the
safety would hold only for a first import.

The consequence, that a promoted targeting-enabled location always arrives
half-configured, is **made visible rather than closed.**

### 12.6 Ownership resolves by name, and each half nulls independently

`AssetRecord` gains `UploadedByUserPrincipalName` and `UploadedByUserGroupName`. On
import each resolves through the existing `ContentPackageUserResolver`, **each half
independently**, and an unresolved half is written NULL with a warning.

**Never fall back to the system user.** The importer already carries two different
principal-failure policies: `AssetContentMetadata.SetByUserId` resolves with
`fallbackToSystemUser: true` (`:629`), and `LocationAccessRule` resolves with
`fallbackToSystemUser: false` and skips the whole record with a warning (`:867-877`).
The first is right for an audit note and **wrong here**: these two columns decide who
may edit, replace, delete, re-tag and resubmit, and the system user is a disabled
account no member can ever be.

**Nulling everything unconditionally was also rejected.** Promotion between a
customer's own Dev and Prod is the normal case and the principals genuinely are the
same people. Nulling would hand every promoted asset to full-access users only,
which is NULL's meaning arriving by accident rather than by choice.

**`UploadedByUserId` NULL with `UploadedByUserGroupId` set is legal, and the importer
is its only writer.** No path in the section 7 design can produce that combination,
since Uploading as always has a user. It was allowed rather than collapsed: nulling
both when either fails would throw away a correct group match to avoid an unfamiliar
row shape, and the state is legible, because control is read off live
`tblUserGroupMembership` on the group. **State this or a future reader will find it
and treat it as corruption.**

**No access check on resolved ownership.** The importer does not verify that a
resolved user or group holds a rule granting access at the target location. It
resolves, writes, and warns. Ownership naming a user with no access there is inert
rather than dangerous: a full-access user controls the asset regardless, and the
named owner simply never sees it, which is the accurate record of who uploaded it.
Applying section 7.4's write-time guard here would mean failing the write, and
section 12.6 already ruled that dropping ownership beats dropping content. That
guard exists for a reason the importer does not have: the session-length gap between
picking an identity and writing. The importer resolves and writes in one
transaction.

**Chain routing already degrades correctly** for imported assets, per section 7.6,
and needs no work here.

### 12.7 `SchemaVersion` 4, and pre-v4 bundles are translated

**`MetadataFilterKey` becomes `ViewScope` in the manifest.** It is a
`LocationAccessRuleRecord` field copied verbatim on import (`:882`), and section 4.4
drops the column for its partial use and replaces it for its view use. **Every bundle
in existence carries the old field.**

Bump `SchemaVersion` to 4 and rename the manifest field. On reading a pre-v4 bundle,
translate: `__all__` becomes `all`, `__none__` becomes `none`, and any other value (a
real metadata key from a `partial` rule) is **dropped with a warning naming the
rule**.

**Do not refuse pre-v4 bundles.** The field is NULL on all 68 dev rules because zero
view rules exist, so refusal would break every existing bundle to handle a case with
no known instances.

The importer's comment at `:853`, which documents the `__all__` / `__none__`
convention, becomes wrong and is **rewritten rather than left**.

**A pre-v4 location record is translated, not ignored and not merely stripped.** Two
hazards from one source, both invisible until an old bundle is imported into an
upgraded environment:

1. **The stale blob.** Migration 1 strips `customMetadataFields` from
   `TblLocation.ExtendedData`. Every pre-v4 bundle still contains it, and `:298`
   overwrites the target column with the bundle's copy. So the import
   **re-introduces the exact orphaned second copy section 4.2 refused to leave
   behind**, into a column the migration just cleaned, while the real configuration
   does not arrive and the location's participating keys silently become whatever
   the target happened to have.
2. **The NOT NULL abort.** A missing `MetadataMatchMode` deserializes to `null` and
   reaches a `NOT NULL` column.

So on a pre-v4 bundle the importer parses `customMetadataFields` out of the incoming
`ExtendedData`, creates the corresponding rule rows through section 12.2's
natural-key upsert with coded default policies, **strips the property from the blob
before writing it**, and **coalesces a missing `MetadataMatchMode` to `'all'`**.
`TargetContentByMetadata` is benign, defaulting to false.

**This is migration 1 applied to a bundle instead of to a database**, which is the
honest reading of what an old bundle is: a snapshot of a pre-migration schema.

Stripping without translating was rejected as the cheaper half-measure: it fixes the
stale copy but discards configuration the bundle genuinely carries, turning an old
bundle from "promotes targeting" into "silently clears it". Because keys resolve by
name against the target registry, a translated key that is unregistered skips with
the same warning, so the two paths converge.

### 12.8 Derived details, stated rather than decided

- The rule upsert sets `CreateDate = DateTime.UtcNow` on insert and does **not**
  carry `LastEditDate` / `LastEditUser` from the bundle, following the
  `LocationAccessRule` block's existing precedent.
- `ComputeDiffs` gains a `LocationMetadataRule` line; the generic `Diff<T>` helper
  reaches `ContentPackageId` through `EF.Property<Guid>` shadow access because there
  is no shared interface. That cost is paid once.
- `ContentPackageDependencyResolver` step 2 expands promoted locations to their rule
  rows alongside displays, assets and constraints.
- **An asset-only bundle (no locations selected) carries tags but no rules**,
  unchanged from today's behaviour for tags. The asset lands at a location whose
  rules are whatever the target has. Not a defect; stated so the asymmetry is not
  read as one.

---

## 13. Re-tagging and orphaned tags

From [DXME-221](https://radianttechnology.atlassian.net/browse/DXME-221): twelve
decisions.

**Nothing owns bulk tag deletion, and the capability was never the hole.**

### 13.1 No bulk tag deletion, and the dangling reference is deleted

The rule builder's remove confirmation pointed at a "Clear tags" action on the
content list. **That action will never be built.**

- **No bulk selection exists anywhere.** Neither `Playlist.razor` nor
  `AssetLibrary.razor` has a checkbox, a select-all, or any multi-asset action. A
  bulk clear is not a button on an existing list, it is a **selection model neither
  list has ever had.** That is what makes it disproportionate, weighed against 26
  orphaned rows on 318 live assets.
- **Orphaned tags are a supported, surfaced state**, removing a key is
  non-destructive and exactly reversible, and there is no data pressure.

**The rule builder's remove confirmation keeps its count and all three statements
and changes only its pointer**, to per-asset Edit metadata on the content list. All
three statements were already true; only the pointer was wrong.

**The rows-about-to-be-deleted counter is deleted, not rehomed.** Nothing is deleted
by a remove any more, so the number has no referent. **This is not the same as the
three coverage counters in section 5.3, which stand untouched.**

### 13.2 The real hole was reachability

**Per-asset clearing already ships.** `AssetMetadataEditModal.Save` calls
`RemoveMetadataAsync` on a blank value, so a cleared field already deletes its row
rather than storing a blank.

But **`Open()` builds `values` from `locationFields` and then discards any loaded row
whose key is not in that dictionary.** So **a tag whose key was removed from the
location is invisible and unclearable through any surface in the product.** That is
the defect, and it is not a bulk defect.

It is also wired in **exactly one place**, `AssetLibrary.razor:124`. The Playlist
content list renders a read-only METADATA block per card (`:655-670`) beside an
existing `dx-asset-actions` row gated by `cardCanEdit`, and offers **no way to change
what it is displaying.**

### 13.3 The per-asset editor surfaces orphaned tags, clearable

A change to one existing modal rather than a new surface, and it is what makes
"tags are kept" honest about the user's ability to act rather than only about the
data.

- **Orphan means no rule row at the asset's location.** Registry membership is
  irrelevant to the definition: both registered keys with no rule row here **and**
  unregistered keys entirely (the `HotelNo` class) appear in the group. Registry
  `DisplayName` where one exists, raw key where it does not.
- **The "This location has no custom metadata fields" empty state must not suppress
  the group**, or a location that removed its last field hides every tag it still
  holds.
- **One row per orphaned key**, its values listed on the row, **cleared as a unit.**
  Once the asset side is genuinely multi-value a key can carry several values, but
  the group's job is removing a key that no longer applies here, not editing it, so
  per-value granularity buys nothing.
- **Clearing stages and commits on Save.** A Clear control marks the row as pending
  removal and Save applies it through the existing `RemoveMetadataAsync` path. **No
  separate confirmation dialog**: one key on one asset, the modal's Save is already
  the commit point, and Cancel already means nothing happened. The control disables
  with a reason when the user does not own the asset.

### 13.4 The Playlist content list gains Edit metadata

Added to `dx-asset-actions` and gated by **ownership**, not by scope. The Playlist is
where targeting is authored and therefore where a wrong tag is noticed; today it
shows the tag next to content it is excluding and offers no way to fix it.

**The button is always shown**, subject to the ownership gate, and the modal carries
the empty state. Today the Asset Library gates it on `metadataFields.Count > 0`
(`AssetLibrary.razor:103`), which is exactly the suppression section 13.3 forbids.
Always showing it also removes the question of how a list would know per-asset
whether orphans exist without a new data load.

**The Asset Library's Edit button has no ownership gate today.** It gains one,
alongside the Playlist button. **Both surfaces gate on ownership, never on scope.**

**No orphan badge on the content lists.** The rule builder is the discovery path,
already surfacing undeclared tags with key and count; the modal is the resolution
path. The question "what happened to the old tags" arises while editing rules, not
while browsing content. A badge puts a permanent marker on the most-used screen in
the product for a state that is legitimate and usually uninteresting.

### 13.5 An off-list value must render as a marked selected option

**A pre-existing defect this section owns**, because it takes `MetadataValueInput`
from one surface to two and points the rule builder's copy at per-asset clearing as
the place tags are reachable.

`MetadataValueInput` renders a `list` field as a `<select>` carrying only the
an empty "Select" placeholder option, so **a stored value absent from
`Options` matches no option and the control displays as blank while the row still
exists and targeting still evaluates it.** The string survives an untouched Save, so
nothing is lost silently, but **the screen misreports actively-targeted content as
having no value, and one click on that select makes the display true by deleting the
row.** In a `multi-select` the same value is preserved across a toggle by
`ToggleMultiSelect` but never rendered, so it is invisible in a second way.

This is the normal failure mode, not an operator error: `Category = "Category 2"` on
2 rows, orphaned by a registry edit.

**This spec cannot describe per-asset clearing as the home for this capability while
the editor misreports the values being cleared.** The fix belongs against
`MetadataValueInput.razor` in section 14, not only in the modal's description, since
it is a shared component used by the device and user metadata editors too.

### 13.6 No registry-side tag purge on definition delete

**Cheap to build and refused anyway.** `MetadataTab` already counts asset tags, so a
checkbox would be nearly free.

Refused on blast radius and on what it would cost section 9.3.
`TblMetadataDefinition` is an Identity-module table and **environment-wide**, so a purge
launched from there crosses every location and every tenant in one click, which is
the most dangerous possible form of the thing just declined in its cheapest form.

**More decisively: deleting a definition is currently runtime-inert, and that is the
entire reason the guard can be warn-and-proceed instead of a block.** Attaching a tag
purge makes it destructive again and forces the guard back to a hard block. The
counter reports, the delete proceeds, no tag is touched.

### 13.7 Tagging leaves the uploader

Section 7.7 moves tagging out of the uploader's edit mode entirely and into
`AssetMetadataEditModal`, so there is **a single re-tagging surface.** The orphan
group and the ownership gate therefore apply to every re-tag in the product.

---

## 14. Code changes by file

**This is the section a build session works from directly.** Every row names the
section that decided it. Line numbers are from `dev` at `f5c201d` unless a later
ticket restated them; confirm the symbol rather than trusting the number.

Three rows are flagged **[easily dropped]**. Each is a consequence of another
ticket rather than of its own, and each was named by its resolving ticket as the
thing most likely to fall out of an implementation-ready spec.

### `src/DXSuite.Director/Migrations/`

| Change | Section |
|---|---|
| Five new migrations, in order, **all in this folder**. Not split across two DbContexts; section 4.6's head note says why. | 4.6 |
| **Every index change is raw SQL guarded by a `sys.indexes` / `sys.key_constraints` lookup, never a scaffolded `DropIndex` / `CreateIndex` by name.** The model disagrees with the database on all three metadata tables this work changes, two of the five relevant indexes are unique **constraints** rather than indexes, and `DROP INDEX` fails against a constraint. Copy `20260913185502_WidenAppVersionUniqueIndexToArtifactKind.cs`, including its habit of saying in a doc comment why the SQL is raw. | 4.1 |
| Migration 5 is a **reconciliation, not an addition**: the uniqueness it wants already exists on dev under a name no model declares. | 4.6 |

### `src/DXSuite.Director/Entities/`

| File | Change | Section |
|---|---|---|
| `TblLocationMetadataRule.cs` | **New.** One row per `(LocationId, MetadataKey)`. Blank policies as `varchar` with `[Unicode(false)]` and legal values in XML doc comments. | 4.2 |
| `TblLocation.cs` | Add `TargetContentByMetadata bit` and `MetadataMatchMode varchar(10)`. | 4.2 |
| `TblAsset.cs` | Add `UploadedByUserId int?` and `UploadedByUserGroupId int?`, both `NO ACTION` FKs. **Do not** reuse `TblAssetApproval.UploadedByUserId` reached through the 1:1 nav at `:113-114`. | 4.4 |
| `TblLocationAccessRule.cs` | Drop `MetadataFilterKey`. Add `ViewScope varchar(20) NULL`. | 4.4 |
| `TblAssetContentTag.cs` | The unique index is an **attribute at `:15`**, not a DbContext declaration. Change it there: non-unique on `(AssetId, MetadataKey)`, new unique on `(AssetId, MetadataKey, MetadataValue)`. | 4.3 |
| `DirectorDbContext.cs` | Register `TblLocationMetadataRule`, including in the `ContentPackageId` opt-in list at `:1323-1387`. Remove the two user-side `HasIndex` declarations at `:960-990`. Leave `UQ_DeviceMetadata` at `:688-690` **untouched**. | 4.2, 4.3, 4.6 |

### `src/DXSuite.Shared/Entities/Identity/`

| File | Change | Section |
|---|---|---|
| `IdentityDbContext.cs` | Remove the two user-side `HasIndex` declarations. Add the `MetadataKey` unique index declaration at `:143-148` so the model matches migration 5. | 4.6 |

### `src/DXSuite.Director/Services/`: the evaluator

| File | Change | Section |
|---|---|---|
| `MetadataEvaluator.cs` | **New.** The pure function of section 3.9: three-state per-key verdicts, stage 0 gate, 12-row table, ALL/ANY combine, verdicts ordered by key. No I/O, no async. Plus the one-asset-many-devices entry point taking values rather than rows, for the reach line. | 3 |
| `MetadataSubjectResolver.cs` | **New.** Materializes asset, device, user and group value sets from the database and **batches** those reads. The current predicates issue two queries per file. | 3.9 |
| `LocationMetadataRuleService.cs` | **New.** Reads and writes `TblLocationMetadataRule`, owns the per-location rule cache, and invalidates it after `SaveChangesAsync`. | 8.5, 8.6 |
| `UploadIdentityService.cs` | **New.** `IUploadIdentityService`: candidates, write-time validation, and the values the picked identity supplies. In **Director**, not Shared. | 7.3 |
| `PlaylistManifestRules.cs` | `ShouldInclude` is replaced by the evaluator. Fix the `deviceFilterValue == null` versus `IsNullOrEmpty` inconsistency on the way out. Note `:16-19` returns true for everything when approvals are disabled: that coupling must not survive. | 2.1, 3.6, 3.10 |
| `EffectiveContentResolver.cs` | Collapse `IncludeByMetadataAsync`, `IncludePlainApprovedAsync` and `IncludeBySidecar` (`:291-350`) onto the evaluator. Delete the hardcoded `HotelNo` XPath at `:336`. Normalise the comparison at `:300-304`. **No extraction and no test coverage of the orchestration around it** (15.6). | 2.1, 2.4, 9.8 |
| `ContentTagService.cs` | Delete `AutoSetMetadataOnUploadAsync` (`:86-108`) and its `.First()` at `:101`. `SetMetadataAsync` (`:34-63`) **stops upserting and starts reconciling a set**. Trim on write. Invalidate after save (`:34`, `:102`). | 4.3, 7.1, 8.5, 9.8 |
| `IContentTagService.cs` | Delete the `AutoSetMetadataOnUploadAsync` declaration at `:10`. | 7.1 |
| `LocationAccessService.cs` | Delete `GetMetadataFilterKeyForLocationAsync`. Delete `partialRules.FirstOrDefault()` at `:180`. Read group metadata as a scope operand. Fix the `.First()` on values at `:183-189`. Keep the highest-level-wins resolve at `:154-163`. Read `ViewScope` where `:57`/`:89` validated the old column. **No cache dependency** (8.7). `:260`'s `activeMetadataKey` reads `tblAccessSchedule.MetadataKey`. | 3.2, 7.1, 8.7, 9.2 |
| `ApprovalChainService.cs` | Delete the private matcher and the first-wins at `:66`. Route from the picked rule, then from the stored owning group re-resolved to a current rule, then the location default chain. | 7.6 |
| `AssetApprovalService.cs` | Add invalidation to `PurgeDeclinedAssetAsync` (`:165`) and `WithdrawPendingForAssetAsync` (`:419`). | 8.5 |
| `AssetIngestService.cs` | Capture ownership from `request.UploaderUserId` and the new `UploadingAsUserGroupId`, validated by `IUploadIdentityService`. **Never derive from `caller.Access`.** Fix the `.First()` at `:218`. Invalidate at `:133`, `:137`. Replace the tagging that died with `AutoSetMetadataOnUploadAsync` at `:123`. | 7.5, 8.5 |
| `ContentOverwriteService.cs` | Copy `UploadedByUserId`, `UploadedByUserGroupId` **and the `tblAssetContentMetadata` rows** at `:133`. The tag copy is a pre-existing defect, not new scope. | 7.7 |
| `DeviceMetadataService.cs` | **Delete the `zipCode` five-digit throw at `:112-118`.** Fold `AppendMetadataToUrl` (`:225-257`) into the single implementation. Trim on write. `GetMetadataAsync` at `:66` needs no duplicate guard: `UQ_DeviceMetadata` exists. | 2.4, 8.16, 9.8, 9.9 |
| `StatusSelectorService.cs` | **Only two changes, both inside the standing requirement:** `:182`'s `ToDictionary` becomes first-value-wins; `:77` and `:114` invalidate the **display** bucket, not `display.LocationId`. **Do not normalise its comparison. Do not touch `"Uncategorized"`. Do not change grouping.** | 8.3, 9.10 |
| `ManifestCacheService.cs` | `BuildCacheKey` loses its filter component and gains a `display`/`loc` discriminator; `_locationKeys` (`:17`) becomes a typed triple. **Delete `MatchesAnyVariant` (`:80`) and `InvalidateAll` (`:123`).** Keep `GetStatistics` (`:138`) and fix `CurrentEntries` (`:146`) over-counting after TTL expiry. TTL default to thirty minutes (`:15`). Fix logging at `:41`, `:47`, `:63`, `:76`. Add the layout-to-sources map. | 8.2, 8.3, 8.4, 8.10, 11.5 |
| `MetadataCacheService.cs` | Delete the hardcoded `HotelNo` field name at `:189`. Wire `InvalidateLocation` into the seam. | 2.4, 8.10 |
| `ContentManifestLogService.cs` | **Drop the `filterValue` parameter from `RecordIfChangedAsync` (`:41`). Do not pass null.** Leave the column nullable. Its `"display"`/`"location"` discriminator is the model for the cache key. | 8.3, 8.9 |
| `MediaFolderCacheService.cs` | `:142` chains by `locationId`; it must reach display-keyed entries too under the new key shape. | 8.3 |
| `GroupVideoWallAssignments.cs` | `AppliesTo` (`:96-101`) keeps splitting its own CSV. Read through the key-aware group service with `.SingleOrDefault()` so a duplicate throws loudly here. | 4.3 |
| `FloorPlanService.cs` | **No change.** Its case-sensitive `==` at `:154-159` is deliberately left alone, and `tblFloorPlanIconSchedule.MetadataKey` gets no authoring surface. Listed so the omission is visible. | 9.2, 9.8 |
| `PlaylistPreviewBuilder.cs` | `BuildFields` loses `locationMetadataFilterKey`; it dissolves into the participating key list. **Fix the doc comment**, which claims a sharing with the delivery path that never existed. | 6.5 |

### `src/DXSuite.Shared/Services/Identity/`

| File | Change | Section |
|---|---|---|
| `MetadataDefinitionService.cs` | `DeleteDefinitionAsync` (`:101`) becomes **warn-and-proceed**. `GetKeyUsageInternalAsync` (`:166`) replaces its three hardcoded `0` counters (`:170`, `:173`, `:174`) with contributor results, reporting **tag stores and reference stores separately**. `GetKeyUsageAsync` becomes the live shared entry point. Add server-side `FieldType` validation at `:65`. Option removal warns with a cross-side count. | 9.3, 9.4, 9.7 |
| `IMetadataUsageContributor.cs` | **New.** The contributor seam: defined in Shared, implemented in Director, aggregated here. Built once, used by both the key-deletion and the option-removal warnings. | 9.4 |
| `UserMetadataService.cs` | `:52-83` writes through `IIdentityDbContext`, which in Director is `DirectorDbContext`. Multi-value read path. Trim on write. | 4.3, 4.6, 9.8 |
| `UserGroupMetadataService.cs` | `SetMetadataAsync` stops relying on the dropped index. `GetMetadataAsync` for a key **returns a list**. Trim on write. | 4.3, 9.8 |

### `src/DXSuite.Director/Controllers/`

| File | Change | Section |
|---|---|---|
| `SignageController.cs` | The bulk of the delivery work. Broken out below. | 8, 11 |
| `LocationContentApiController.cs` | `GetAssets` (`:194-227`) folds onto the evaluator and emits `CanEdit`/`CanDelete` **from ownership, not scope**. `GetAssets` evaluates each linked library's assets against **that library's** rules. `SaveSchedule` (`:295`) keeps its library allowlist, **checks each asset against the caller's scope**, and writes through the 8.5 seam so it invalidates the display bucket. `:398` passes `caller.Access`, which must **not** be used to derive an upload identity; `POST assets` requires `uploadingAs` from any caller with a `partial` rule, and `context` returns the candidates. | 7.5, 7.9, 8.8, 10.2, 10.6 |

#### `SignageController.cs` in detail

| Change | Section |
|---|---|
| **Delete `ShouldIncludeAssetInManifestAsync` (`:1793-1810`).** Zero call sites. | 2.1 |
| Collapse `ShouldIncludeFileByMetadataAsync` (`:1817-1843`), `ShouldIncludePlainPlaylistFileAsync` (`:1856-1876`) and `ShouldIncludeFileInManifest` (`:1878-1884`) onto the evaluator, with approval **ANDed at the call site**. Fix the escaped/unescaped `AssetPath` mismatch between `:1821` and `:1859`. | 2.1, 3.6, 3.10 |
| Normalise comparisons at `:1809` and `:1857-1865` (the key match at `:1857` is also case-sensitive). | 9.8 |
| **Delete all three literal `"HotelNo"` fallbacks** at `:763`, `:843`, `:1669`. Section 11 removes two on its own account. | 2.4, 11.4 |
| `ResolvePlaylistDelivery` (`:1317-1373`): **extract to a pure static** taking the publication, the device's values, the device's local time and the location's rule set. Add the targeting `.Where` beside the window filter and before `WeightedLoopExpander.Expand`, applied even when the time zone is unresolvable. Null the tag fields in the loop that already nulls four. Leave `ComputeDeliveryVersion` and `RecordDeliveredVersion` behind in the controller. Call sites `:850`, `:994`. | 8.2, 8.17, 15.5 |
| `BuildCacheKey`: drop the filter component at `:613`, `:654`, `:767`, `:847`. Add the `display`/`loc` discriminator. | 8.2, 8.3 |
| TTL: `:988` stops passing five minutes, `:822` and `:654` inherit thirty. **Delete the comment at `:981`** claiming the TTL is load-bearing for correctness. | 8.4 |
| `:992`: drop `filterValue` from the `RecordIfChangedAsync` call. | 8.9 |
| `GetLocationMediaContentAsync` (`:1738`): adopt the **three-branch dispatch** its two siblings at `:883-904` and `EffectiveContentResolver.cs:112-122` already have. **This closes the live approval leak.** | 11.3 |
| **Delete the `.json` exemption** at `:1738`. | 11.3 |
| `BuildLayoutZonesAsync` (`:1661`): stop comparing the device's value for key X against the asset's `HotelNo`. Remove the unconditional `HotelNo` fallback in the `else` branch at `:756-764`. Carry participating-key tags into the publication. | 11.3, 11.4 |
| `ResolveLayoutDeliveryAsync` (`:1029-1132`): evaluate playlist-zone targeting **here, per source location, per device**. Extend the `ZoneDisplayContent` rebuild at `:1101-1109` to playlist zones so tags do not reach the wire. **Extract the decision over the fetched rows as a pure static; leave the `TblDisplays` query inside.** Branch on the source location's **type** using `locationsMap` (already loaded at `:1631`), warning-logging a non-Playlist source. | 11.1, 11.2, 11.4, 11.7, 15.5 |
| **Move `AppendMetadataToUrl` for playlist-zone web content from `:1753` (publication) to delivery.** This closes the live cross-device identity leak. Fold the inline loop at `:692-699` and the private duplicate at `:1541-1553` into the single helper. | 9.9, 11.6 |
| `BuildResponseFromCache` (`:1434`): `Zones = cached.Zones` must not carry tags. | 11.4 |
| `MetadataParser.ParseAllMetadata` is constructed fresh at `:857` on every rebuild, memoised nowhere. Not required, but named because it is 18.6 ms of the rebuild. | 8.4 |
| The publication rebuild's `2N` sequential round-trips should fall once the access-rule reads go. **If they do not, the sequential `foreach` is the thing to fix.** | 8.13 |

### `src/DXSuite.Director/wwwroot/scripts/`

| File | Change | Section |
|---|---|---|
| `dx-location.js` | Expose the upload candidates from `context` and accept `uploadingAs` on asset upload. Document that a scoped caller's upload without it is refused. | 7.5, 10.6 |

### `src/DXSuite.Director/ConfigTabs/`

| File | Change | Section |
|---|---|---|
| `LocationsTab.razor` | **The rule builder replaces the Custom Metadata Fields modal at `:186-231`.** Retitle it "Content metadata" for every type. Reduced shape on non-Playlist. **Delete the silent purge in `SaveCustomMetadataFields` (`:454-512`)** and write rule rows instead. Both deserialization paths at `:425-427` and `:469-471`. Keep the `LinkedToDisplay == false` gate at `:110-113`. Stop using `LocationTypeId` literals at `:90` and `:98` if touched. | 4.5, 5, 10.3 |
| `DevicesTab.razor` | **[easily dropped]** **New metadata section**, using `MetadataValueInput` in its new single-value mode. Warns on a missing targeted value when the device has a `LocationID`. No participation badge. This is why 206 of 237 devices can be tagged at all. | 9.5 |
| `ContentChannelsTab.razor` | Keep the editor at `:328-339`; it is already registry-backed. **Trim on write in the update branch at `:1094`**, which does not trim while the insert branch does. | 9.5, 9.8 |
| `MetadataTab.razor` | Warn-and-proceed on delete and on option removal, with the two-group count. Pickers show the key, not just the `DisplayName`. `int` stays a supported type (`:113-118`). The asset-tag counter at `:277-282` is the one that already worked. | 9.3, 9.6 |

### `src/DXSuite.Director/Components/`

| File | Change | Section |
|---|---|---|
| `Modals/ContentPreviewModal.razor` | Two subject modes with "Start from"; mock panel single-select per field; **"Any device" deleted**; inline diagnostic with per-key reasons; stage 0 gate as a banner; both failing gates always shown; three empty states; **Run deleted** (wire `@bind:after` on `previewDate` and `previewTime`); Preview moves beside the Player dropdown and disables with a reason; toolbar two rows, every control 32px. Collapses to today's shape with targeting off. | 6.1–6.7 |
| `Components/Shared/RunAsFilterModal.razor` | **Deleted**, with `RunAsSelection.MatchesMetadata` (`:137`). | 2.1, 6.5 |
| `Modals/MediaAssetUploader.razor` | The **Uploading as** control and the **reach line**. Remove the `RunAsFilterModal` call at `:419`. Fix the `.First()` at `:1084`. `:866` is a live `MetadataFilterKey` caller. Tagging leaves edit mode; the per-keystroke sidecar write moves onto Save. **The approval radios are unreachable, so Declined content plays in the preview today**: fix or remove them. Ownership capture at `:1244`. `:771` reads `userAccess`. Fields at `:794-809`, persist at `:1257-1259`, `uploadToSql` at `:855-857`. Invalidate at `:1123`, `:1254`, `:1259`. Config rides a scoped `UploadStateService` (`:699`); **do not subscribe to `LocationAccessChanged`**. | 6.5, 7, 8.5 |
| `Modals/WebContentUploader.razor` | Same picker. Ownership at `:450`. Replace the tagging lost at `:456`. Invalidate at `:418`, `:460`. | 7.5, 8.5 |
| `Modals/StreamAssetUploader.razor` | Same picker where there is a location. **`locationId ?? 0` means no picker and NULL ownership** when there is none. Ownership at `:253`. | 7.5 |
| `Modals/TvSourceUploader.razor` | Same picker. Ownership at `:312`. | 7.5 |
| `Modals/AssetMetadataEditModal.razor` | **The single re-tagging surface.** `Open()` stops discarding loaded rows outside `locationFields`. Add the **orphaned-tag group**, defined by rule-row absence, indifferent to registry membership, one row per key cleared as a unit, staged and committed on Save, disabled with a reason without ownership. **The empty state must not suppress the group.** Invalidate at `:168`. | 8.5, 13.2, 13.3 |
| `Modals/LocationAccessModal.razor` | `ViewScope` replaces the `MetadataFilterKey` dropdown at `:174-175`, read at `:173` and `:284`. **Refuse to save a `partial` or `view` rule resolving to zero values.** Access-schedule picker at `:411` shows the key. Invalidate on save at `:1155`, `:1184`. | 3.3, 4.4, 8.7, 9.6 |
| `Modals/DisplayEditModal.razor` | Picker at `:174-179` shows the key, not just the `DisplayName`. Otherwise unchanged. | 9.6 |
| `Modals/CreateLayoutModal.razor` | **Writes rule rows** as a second write site (`CreateAssetLibraryLocation`, `:2867-2876`); the step at `:1965` already picks from the registry. **Creates no asset**, so no ownership capture. Source restriction at `:1934`, display picker at `:252-274`. | 7.5, 10.3 |
| `DeviceMetadata/DeviceMetadataEditor.razor` | **Deleted.** Dead code; nothing renders it. | 9.5 |
| `LocationTypes/Playlist.razor` | Scope filter and ownership affordance (variant B). **Gains Edit metadata** in `dx-asset-actions`, ownership-gated and **always shown**. `:570`'s resubmit gate moves onto the ownership columns. `:386` reads the key list from the rule table. Action row at `:438-495`; METADATA block at `:655-670`; `userAccess` at `:966`, `:990`. | 7.7, 7.9, 13.4 |
| `LocationTypes/AssetLibrary.razor` | **Scope filtering is new here, not changed**: the screen does none today. Ownership gates Edit, Delete and re-tag. **Set `MediaUploadConfig.UserAccess`** in the config block at `:300-320`. Columns at `:201-203` read the rule table. Remove the `metadataFields.Count > 0` gate at `:103`. Edit metadata at `:124`. **Load by `ConstraintId`**, deliberately. | 10.1, 10.3, 10.5, 13.4 |
| `LocationTypes/Custom/LayoutLocation.razor` | Rename "Layout Preview" at `:11` to **zone layout map**. No content preview. | 11.8 |
| `Schedulers/ScheduleAssetPanel.razor` | Normalise comparisons at `:501`, `:532`, `:558`. | 9.8 |
| `TemplateCreator.razor` | Same picker. Ownership at `:987`. | 7.5 |

### `src/DXSuite.Shared/Components/`

| File | Change | Section |
|---|---|---|
| `MetadataValueInput.razor` | **[easily dropped]** Three changes, and this is a shared component used by the device, asset, user, group and content-channel editors. (1) **An off-list stored value renders as a marked selected option** rather than as blank: `list` at `:4` and the `multi-select` preservation in `ToggleMultiSelect` at `:14`. (2) Add an **explicit single-value mode** for the device side. (3) The final `else` branch **surfaces** an unrecognised `FieldType` instead of degrading to free text. Its multi-select branch is the source of the CSV, so it stops comma-joining. | 4.3, 9.7, 13.5 |
| `Settings/UserMetadataEditor.razor` | Multi-value control. Already registry-backed through `IMetadataDefinitionService`, so this is a change inside an existing component. | 4.3 |
| `Settings/GroupMetadataEditor.razor` | Same. | 4.3 |
| `Authorization/ISessionState.cs` | **`ResolvedAccess` drops `MatchingRuleId`** entirely: written in six places, read nowhere. | 7.3 |

### `src/DXSuite.Director/Models/`

| File | Change | Section |
|---|---|---|
| `LocationExtendedData.cs` | **Delete `CustomMetadataFieldDef` (`:98-101`) and `CustomMetadataFields`.** | 4.2 |
| `LocationTypeConstants.cs` | Add `IsPlaylist`, mirroring `IsAssetLibrary`. `:5-15` says ids are not the key for built-ins; honour that. | 10.3 |
| `MediaUploadConfig.cs` | `UserAccess` (`:39`) gains the picked identity and the candidate list. | 7.3, 10.5 |
| `AssetIngestRequest` | Add `UploadingAsUserGroupId int?`, plus the controller form field. | 7.5 |

### `src/DXSuite.Director/Services/ContentPackage/`

| File | Change | Section |
|---|---|---|
| `ContentPackageBundleImporter.cs` | **Fix `:631` to match on `(AssetId, MetadataKey)`**, not `ContentPackageId` alone: after the index drop it inserts silently and a bundle import tags one asset twice. Rule upsert by natural key. `keepMetadataRules` in replace mode (`:1158-1175`). Resolve rule keys by name, skipping with a warning. Ownership by UPN and group name, each half nulling independently, **never falling back to the system user** (contrast `:629`'s `fallbackToSystemUser: true` with `:867-877`'s `false`). **Strip `customMetadataFields` from the incoming blob before the wholesale `ExtendedData` assignment at `:298`**, and translate it into rule rows. Coalesce a missing `MetadataMatchMode` to `'all'`. Translate `MetadataFilterKey` to `ViewScope` and **rewrite the now-wrong comment at `:853`**. | 4.3, 12 |
| `ContentPackageBundleBuilder.cs` | `:140` copies `ExtendedData` verbatim; it must no longer carry `customMetadataFields`. Emit rule rows and the two `TblLocation` columns. | 12.1, 12.5 |
| `ContentPackageDependencyResolver.cs` | Step 2 expands promoted locations to their rule rows, alongside displays, assets and constraints. | 12.8 |
| Manifest / `SchemaVersion` | Bump to **4**. `LocationRecord` gains the two columns and a rule list; `AssetRecord` gains the two ownership names; `LocationAccessRuleRecord` renames its field. `ComputeDiffs` gains a `LocationMetadataRule` line, reaching `ContentPackageId` through `EF.Property<Guid>`. | 12.7, 12.8 |
| Import preview | **Zero-device targeting check** when the bundle carries a location with `TargetContentByMetadata = 1`. | 12.5 |
| `ContentPackageBundleImporter.cs` validation | **Refuse the whole package, before any write,** when it would leave a `Playlist` (type 7) location with a display: a display record under one (`:558`), or a location record retyping to 7 an existing location that has displays (`:285`). The error names the location and its displays. | 10.7 |

### `db/`

| Change | Section |
|---|---|
| One `DELETE` removing the **eight `HotelNo` asset rows** at locations 1044 and 1046, beside the code deletion rather than as a migration. | 2.4 |
| `director-seed.sql` and the SLC patch script no longer seed location type 5. Already done. | 2.5 |

### `tests/DXSuite.Director.Tests/`

| File | Change | Section |
|---|---|---|
| `ApprovalGateTests.cs` | **[easily dropped, and the highest-value line here].** This is the only existing test in this path and **it pins the very function this work dismantles.** Its 6 methods and 8 calls target `ShouldInclude(string? assetFilterValue, int? approvalStatus, string? deviceFilterValue, bool enableContentApproval)`, and **every part of that signature changes.** The function splits, so the tests split, and **neither half is a port**: the approval half becomes the call-site ANDing tests and keeps the intent of the existing cases; **the metadata half is deleted**, superseded by the truth table, since single-value equality is one row of any-of-any. A build session that meets a red test it was not warned about **makes it green by restoring the old semantics**, which is the most likely way this work gets quietly undone. | 15.3, 15.7 |
| `MetadataEvaluatorVectors.json` | **New**, beside `TimeZoneVectors.json`, read the same way through `AppContext.BaseDirectory`. The 12-row table as data, with section 3.4's row numbers visible. | 15.2 |
| `MetadataEvaluatorTests.cs` | **New.** The vector file plus the stage 0, stage 2, subject-resolution and visible-hole cases. | 15.3 |
| `MetadataCallSiteTests.cs` | **New.** One test per call site proving it ANDs approval and hands the evaluator the right subject. **Not the truth table re-run nine times.** | 15.3 |
| `PlaylistDeliveryTests.cs` | **New.** The 2x2 masking regression with an **exact-set** assertion, plus the volume runs behind a trait. | 15.4, 15.8 |
| `ContentPackagePlaylistDisplayTests.cs` | **New.** A package inserting a display under a Playlist location, and one retyping a location that has displays to Playlist, are each refused with nothing written. A package retyping a display-less location to Playlist imports. | 10.7 |
| `LayoutDeliveryTests.cs` | **New.** The cross-device identity leak (two devices, each asserted to get its own injected values) and the layout approval leak. | 15.5 |
| `MetadataWorldBuilder.cs` | **New.** A small C# builder for the correctness world, and code generation for the volume set from N. **Worlds are built per test, never shared.** **Reuse an existing `ITenantContext` stub; do not add a fifth.** | 15.9 |

### `.github/workflows/`

| File | Change | Section |
|---|---|---|
| `ci.yml` | The existing `Test` step (`dotnet test DXSuite.sln --no-build -c Release`, landed in `3ed1609`) gains a **trait filter excluding the volume set**. | 15.8 |

### Docs

| File | Change | Section |
|---|---|---|
| `CONTEXT.md` | New **"Content metadata"** section carrying section 1's vocabulary. Add the invariant **a publication is a function of content, never of configuration** beside the existing "Published content" and "Delivered content" entries, and **remove access rules from that entry's list of things that change published content**. Rename the Layout page's view to **zone layout map**. | 1, 8.1, 11.8 |
| `docs/spec/time-zone-scheduling.md` | Annotate the **three** overturned claims in section 5 in place, per the annotations already drafted on branch `spec/timezone-section5-superseded`. Nothing else in that document changes. | 8.15 |
| `docs/spec/metadata-evaluator-truth-table.md` | **Nothing to do.** It is a handoff fragment for this spec, it lives only on the unmerged branch `spec/metadata-evaluator-truth-table`, and it is now section 3. Do not merge that branch; abandon it. | 3 |
| `docs/maps/dxme-204-metadata-content-targeting.md` | Unchanged by the build. It is the index, not the instruction. | n/a |

### Not in this work, and not accidentally

| Left alone | Why |
|---|---|
| `UQ_DeviceMetadata` and any duplicate guard in `GetMetadataAsync` | The index exists. One resolved ticket's note said otherwise and is wrong (8.16). |
| `StatusSelectorService` grouping, `"Uncategorized"`, comparison | Standing requirement (9.10). |
| `tblFloorPlanIconSchedule.MetadataKey` authoring, and its comparison | 9.2, 9.8. |
| URL injection semantics, and any allowlist | 9.9. |
| `videoWallAssignments` restructuring | Belongs to the Ticket Counter Scheduler (4.3). |
| Bulk tag deletion, bulk device tagging, registry-side tag purge, orphan badges | 13.1, 13.6, 9.10. |
| A `SaveChanges` interceptor | 8.5. |
| A cache inspector panel, a memoised delivery pass | 8.10, 8.13. |
| `Schedule.razor` | 10.4. |
| `Location.razor`'s silent component dispatch | 16.4. |
| The sidecar and database metadata stores' reconciliation | 16.4. |
| Consolidating the four `ITenantContext` stubs | 15.9. |
| An integration layer, a browser suite, `EffectiveContentResolver` orchestration coverage, `LocationAccessService` query coverage | 15.6. |
| Modal button order | Inherited from the StyleGuide at build time. |

---

## 15. Test plan

From [DXME-226](https://radianttechnology.atlassian.net/browse/DXME-226): nineteen
decisions.

### 15.1 CI is the gate, and that is the binding constraint

`.github/workflows/ci.yml` carries a `Test` step running
`dotnet test DXSuite.sln --no-build -c Release`, on push and pull request to both
`dev` and `master`. It landed in `3ed1609` on 2026-09-12. **It is on `origin/dev`
and not yet on `master`**, so the gate holds on `dev` today and on `master` only
after `dev` merges up.

Correcting that fact is what moved the answer. Earlier tickets recorded "CI runs
no tests at all", which was true when written; DXME-185 is still To Do in Jira
despite its work having landed, which is how the dead premise survived. **Tracker
hygiene on DXME-185 is not a step on this route.**

**The constraint: no test on this work may require a database, a browser, or a
file share.** CI is a bare `windows-latest` runner with no `services:` block, no
LocalDB step, no `sqlcmd` and no browser install. Adding any one would make the
targeting tests **the only tests in the repo that do not actually run.** The
rejected alternative, "they run when someone remembers", is named here as
considered and refused.

**What the repo actually is**, so nothing here invents infrastructure: one test
project, `tests\DXSuite.Director.Tests`, net10.0, xUnit, 38 files, 230 test
methods. **Zero of those 230 construct a `DbContext`.** No SQLite, no EF InMemory,
no LocalDB, no Testcontainers, no Respawn, no connection string anywhere under
`tests\`. No `IClassFixture`, no `ICollectionFixture`, no base class, no builder:
all 38 classes are standalone and concrete. The house style is pure functions over
values plus hand-written `private sealed` stubs. `TimeZoneVectors.json` is the
sole fixture asset.

### 15.2 Two fixture sets, never mixed, in three places by kind

- A **correctness set**, deliberately tiny and hand-countable, sized so the
  expected asset list can be stated in the test name.
- A **volume set**, parameterised by N.

**Every fixture declares the N it represents** rather than inheriting dev's 4 to 5
assets per location.

| Kind | Where | Why |
|---|---|---|
| The 12-row truth table | A JSON vector file beside `TimeZoneVectors.json` | Reuses the one precedent in the repo and keeps section 3.4's row numbers visible **as data** rather than buried in attributes |
| The correctness world | A small C# builder in the test project | It is an object graph; JSON would only be a worse constructor |
| The volume set | Generated in code from N, never authored | Hand-writing 500 rows is how a fixture stops being maintained |

**The truth table is hand-authored, never generated from the spec.** Generating the
C# would be more machinery than the table it produces, and inverting it would make
this document a build output, which is wrong for a document whose job is to be read
before any build exists. **Each case is named with its row number**, so a red test
names a spec row rather than a tuple of booleans.

**Scope and ownership get their own fixture identities**: one scoped user, in one
group, one `partial` rule, one `view` rule, one group-owned and one user-owned
asset. Taken knowing these assert behaviour no customer uses yet, **because that is
exactly the condition under which the first customer to switch targeting on is the
one who finds the bug.**

### 15.3 One evaluator, nine call sites

**"Coverage of the ten predicates" stops meaning anything after unification.**
There is **one evaluator and nine call sites**, the tenth deleted rather than
ported.

- **The evaluator is asserted once**, by the 12-row table.
- **Each call site gets one test** proving it ANDs approval and hands the evaluator
  the right subject.
- **Re-running the truth table at each call site is forbidden by name.** It is 108
  tests that go red together and localise nothing, and it is the default a build
  session falls into when unwarned.

The named cases, carried verbatim from section 3's fragment:

**Stage 0, the subject gate**

- `Gate_DeviceBlankOnNothingKey_ReceivesNoAssets`
- `Gate_DeviceBlankOnNothingKey_AnotherKeyMatching_StillReceivesNoAssets`
- `Gate_DeviceHasValueOnNothingKey_NotGated`
- `Gate_ScopedUserWithNoValuesOnAnyParticipatingKey_SeesNothing`
- `Gate_ScopedUserWithValuesOnOneKeyOnly_NotGated`
- `Gate_FullAccessRule_ScopeNotEvaluated`

**Stage 1, one key at a time.** Row numbers refer to section 3.4.

- `Key_BothTagged_Intersecting_Matches` (1)
- `Key_BothTagged_Disjoint_NoMatch` (1)
- `Key_AssetMultiValue_DeviceSingleValue_IntersectionMatches` (1)
- `Key_UserMultiValue_AssetMultiValue_AnyOfAnyMatches` (1)
- `Key_CaseVariantValues_Match` (1)
- `Key_WhitespacePaddedValues_Match` (1)
- `Key_AssetHoldsCaseVariantDuplicates_CollapseToOneValue` (1)
- `Key_ValueContainingComma_NotSplit` (1)
- `Key_UntaggedAsset_TaggedDevice_AllPlayers_Abstains` (2)
- `Key_UntaggedAsset_TaggedDevice_NoPlayers_NoMatch` (3)
- `Key_TaggedAsset_BlankDevice_AllContent_Abstains` (4)
- `Key_TaggedAsset_BlankDevice_UntaggedOnly_NoMatch` (5)
- `Key_TaggedAsset_BlankUser_AlwaysAbstains` (6)
- `Key_BothUntagged_AllPlayers_AllContent_Abstains` (7)
- `Key_BothUntagged_AllPlayers_UntaggedOnly_Matches` (8)
- `Key_BothUntagged_NoPlayers_AllContent_NoMatch` (9)
- `Key_BothUntagged_NoPlayers_UntaggedOnly_NoMatch` (10)
- `Key_BothUntagged_AllPlayers_Scope_Abstains` (11)
- `Key_BothUntagged_NoPlayers_Scope_NoMatch` (12)

**Stage 2, combination**

- `Combine_All_OneNoMatch_Excludes`
- `Combine_All_MatchAndAbstain_Includes`
- `Combine_All_MatchAndNoMatch_Excludes`
- `Combine_Any_MatchAndNoMatch_Includes`
- `Combine_Any_AllAbstain_Includes`
- `Combine_Any_NoMatchAndAbstainOnly_Excludes`
- `Combine_NoParticipatingKeys_Includes`
- `Combine_VerdictsOrderedByKey`

**Subject resolution and participation**

- `Subject_UserValuesUnionedWithGroupValues`
- `Subject_GroupMembershipReadCurrent_NotSnapshotted`
- `Participation_KeyOnAssetButNotInLocationRules_Ignored`
- `Participation_KeyInRulesButNotInRegistry_Abstains`
- `Participation_SentinelAllOrNone_NotTreatedAsKey`

**Call-site behaviour.** These guard section 3.6 and belong with the call sites,
not the evaluator.

- `CallSite_Targeting_UnapprovedAssetExcluded`
- `CallSite_Targeting_ApprovalDisabled_TargetingStillApplies`
- `CallSite_Scope_PendingOwnAssetVisible`
- `CallSite_Scope_PendingAssetOfAnotherUserVisibleWhenInScope`
- `CallSite_TargetingSwitchOff_TargetingSkipped_ScopeStillApplies`

`CallSite_Targeting_ApprovalDisabled_TargetingStillApplies` is the regression guard
for `PlaylistManifestRules.ShouldInclude:16-19`.

**Visible-hole cases**

- `Hole_FileWithNoAssetRow_Included`
- `Hole_OrphanedRegistryKey_AbstainsAndWarns`

### 15.4 The two-filter masking regression is one test, not four

The targeting filter and the window filter end up in the same method, so they can
cover for each other. One device, one publication, four assets, the full 2x2:

| Asset | Metadata | Window | Expected |
|---|---|---|---|
| A1 | matches | inside | present |
| A2 | matches | outside | absent |
| A3 | excluded | inside | absent |
| A4 | excluded | outside | absent |

**The assertion is on the exact resulting asset id set, never a `Contains` /
`DoesNotContain` pair.** Targeting broken open surfaces A3, window broken open
surfaces A2, either broken shut loses A1. **The exact-set assertion is the thing
that stops the two filters covering for each other; per-case containment is what
lets them.**

**Two neighbours of this test, from section 8.17.** With the device's time zone
unresolvable, A2 becomes present and A3 and A4 stay absent: the window is skipped,
targeting is not. With shuffle on and weighted assets, a targeted-out asset
contributes nothing to the expanded loop, asserted on the exact expanded sequence.

**No reason-code assertion on the delivery path**, since delivery carries no reason
and section 6.3's reasons belong to the preview.

This is also the regression guard for the riskiest change in the whole work: stage
1 moves live filtering from build time to delivery time, and **all 68 dev access
rules are `full` with a NULL filter key, so dev cannot exercise it.**

### 15.5 Two extractions, not one

Both follow the seam `PlaylistManifestRules` already established, which is why that
is the one thing in the path with tests.

1. **`ResolvePlaylistDelivery` becomes a pure static**, taking the publication, the
   device's values, the device's local time and the location's rule set, and
   returning the delivery. **The two `_manifestCache` calls stay behind in the
   controller**, because recording a delivered version is a side effect and not
   part of the decision. Its body already touches no database: it is pure over
   `CachedManifest`, `TblDevice` and `TblLocation`.
2. **The layout decision comes out too.** `ResolveLayoutDeliveryAsync` **does** hit
   the database, loading `TblDisplays` with `TblScheduleEvents` included. **The
   query stays inside; the decision over the fetched rows comes out as a pure
   static**, and the test passes rows in.

**`InternalsVisibleTo` was rejected**: it lets a test reach into the shape without
fixing it, and the shape is the problem.

**The two leaks from section 11 are split by shape.** The **cross-device identity
leak** is a plain unit test against the first extraction: two devices, each
asserted to get its own injected values. The **approval leak on the layout path**
gets the second extraction. Leaving the layout path to an instrument alone was
rejected specifically because it is an approval leak, and unapproved content
reaching a screen is the failure this whole effort sits downstream of.

### 15.6 The extraction budget stops, and here is where

**The rule, in one line: extract where a decision is trapped behind a query, not
where a query surrounds a decision that is already extracted.**

`EffectiveContentResolver` already delegates its leaf decision to
`PlaylistManifestRules.ShouldInclude` at `:349`, so the deciding part is already
pure and already covered by the table; what remains is query plus orchestration,
and orchestration coverage needs a database. **No extraction there and no coverage
there, recorded as a decision rather than left as a gap.** Same for
`LocationAccessService`: scope is the same evaluator with exactly three divergences,
so the divergences get table rows and the query around them is **uncovered on
purpose**.

**Three refusals, stated rather than left silent:**

1. **No integration layer against a real `DbContext`.** All 230 tests in the repo
   construct none, and CI cannot create a database, so such a layer would be the
   only tests in the repo that never run. Coverage comes from extraction instead.
2. **No E2E or browser suite.** None exists: no Playwright, Selenium or Puppeteer
   anywhere, and no login helper. `BrowserEgressIntegrationTests` shells out to an
   installed browser against in-process loopback servers and **self-skips as a
   vacuous pass**; it never starts the app. The rule builder, preview and uploader
   are verified by **the human checklist their prototypes already define.** Building
   the suite is a new capability, not a step to the spec.
3. **No coverage of `EffectiveContentResolver` orchestration or
   `LocationAccessService` queries**, per the rule above.

### 15.7 Test versus instrument, split by failure shape

The **per-device resolution proof** and the `NotModified` ratio are **instruments,
not tests**: a test proves them only for the fixture devices, while the instrument
proves them for every real device forever. This follows the house rule already
written in `time-zone-scheduling.md` section 11, where performance is verified by
DXME-177's five instruments and **never by a test**.

**The two section 11 leaks get both**, because an instrument only reports after
screens are already wrong and one of the two is an approval leak.

**Exactly one dimension is added to DXME-177's leading instrument, and no new
instrument is invented.** Delivery-pass duration gains the device's metadata
combination alongside time zone, emitted where the tag is emitted.

**The tag must be bounded or it is an unbounded-cardinality metric by
construction**: the combination is a stable short hash of the device's values for
the participating keys, and the instrument reports the **distinct-hash count per
location** rather than each hash as its own dimension. That is also what makes it
prove the intended thing, since a distinct count above 1 means per-device
resolution is genuinely happening.

**This hash is not the deleted variant key returning.** Section 8.2 deleted exactly
that hash from the cache key. This one is observability: it is computed after the
fact, it keys nothing, and nothing reads it back. Stated in one sentence because a
later reader will otherwise read the decision as reversed.

### 15.8 Volume: N is 100 and 500-with-shuffle, and it asserts nothing

- **100** because it is DXME-177's working figure and makes a run comparable to the
  measured 358-microsecond pass.
- **500 with shuffle** because `WeightedLoopExpander.Expand` is the only
  superlinear term in the path.

**The volume set asserts nothing.** Its stated purpose is to **produce a number
once, when the build lands, recorded in this document's capacity paragraph** the
way the 358 microseconds was. It is not an ongoing green-or-red.

**A volume run that looks slow is a finding for
[DXME-188](https://radianttechnology.atlassian.net/browse/DXME-188), never a
licence to add a cache or a memoised delivery inside this work.** Section 8.2
deleted the variant key on precisely those grounds.

**It lives in the test project behind a trait, excluded from CI by filter.** Name
the risk in the same breath: **a test filtered out of CI rots exactly like a test
with no CI**, and the only thing making that acceptable is that this one is a
measuring instrument with a one-time job. A standalone console harness is cleaner
in principle and was rejected as a new project for one number.

### 15.9 Worlds are built per test, never shared

The builder supplies defaults and each test builds exactly what it asserts on. The
house style already points here, but **the binding reason is section 15.4**:
exact-set assertions are the most fragile thing to hang on shared state, and adding
one device for a scope test would rewrite the expected set of a 2x2 test that has
nothing to do with it.

**Reuse an existing `ITenantContext` stub.** The repo stubs that interface four
times under four names (`FakeTenantContext`, `StubMediaTenant`, `StubTenantContext`,
plus two `ITenantStore` stubs). **Adding a fifth by accident is the thing being
avoided.** Consolidating the four is a tidy-up with no metadata content and is out
of scope.

**Test files are rows inside section 14**, under a `tests\` heading, exactly like
any `src\` directory. A parallel "Tests by file" section reads as an appendix and is
the first thing dropped when a build session runs long.

---

## 16. Release shape and open items

### 16.1 One release is forced, and here is the forcing function

Migration 3 drops `MetadataFilterKey`. That kills
`AutoSetMetadataOnUploadAsync`, and that is **the only thing tagging scoped uploads
today**. Every item below is downstream of that single drop, so **any split
produces a release where scoped uploads are silently untagged.**

The release contains:

- Pickers on all six human creation sites (section 7.5).
- `IUploadIdentityService`: candidates, validation, values.
- `AssetIngestRequest.UploadingAsUserGroupId` and its controller form field.
- Approval chain routing off the picked rule, then off the stored owning group.
- `ContentOverwriteService` inheriting ownership **and tags**.
- Resubmit re-gated onto the ownership columns.
- `ResolvedAccess.MatchingRuleId` deleted.
- `partialRules.FirstOrDefault()` and `ApprovalChainService`'s private matcher both
  removed.
- `TblLocationAccessRule.ViewScope` added in migration 3.

Two more ordering facts, neither visible from the migration files:

- **DXME-219 lands before or with migration 3.** Already done (section 2.5).
- **Migration 4 precedes any code writing a second user-side value.** Automatic
  now that all five migrations are one script (section 4.6).
- **No Playlist location has a display, checked before migrating.** This query
  must return zero rows in the target database (section 10.7):

  ```sql
  SELECT l.LocationId, l.LocationName, COUNT(d.DisplayId) AS Displays
  FROM tblLocation l JOIN tblDisplay d ON d.LocationId = l.LocationId
  WHERE l.LocationTypeId = 7
  GROUP BY l.LocationId, l.LocationName;
  ```

### 16.2 What can land ahead of that release

Two independent no-migration stages, both of which close live defects that have no
reason to wait.

**From section 8 (the cache), not yet ticketed.** Whoever picks this up should read
[DXME-227](https://radianttechnology.atlassian.net/browse/DXME-227) first: its cache-key
change and this stage's discriminator both touch `BuildCacheKey`.

- The key discriminator and the `StatusSelectorService` invalidation fix.
- The delivery-time filter on `ResolvePlaylistDelivery`, **wired to today's single
  filter value**.
- The dead-code deletions and the logging fixes.
- The two uncovered approval-invalidation paths.
- Dropping `filterValue` from the manifest log.

**From section 11 (layout zones), now ticketed as
[DXME-227](https://radianttechnology.atlassian.net/browse/DXME-227):**

- The three-branch dispatch, **which closes the live approval leak**.
- Deleting the `.json` exemption.
- Moving URL injection to delivery, **which closes the live cross-device identity
  leak**.
- Dropping the filter component from the layout cache key.
- The layout-to-sources invalidation map.

**Needs the release:** the unified evaluator reading the rule table, the rule cache,
retiring `MetadataFilterKey` and with it access-rule invalidation, the TTL rise
(which must not precede the seam), targeting at delivery for layout zones, and
removing the two layout `HotelNo` fallbacks.

**One caveat on the record.** The delivery-time filter is the risky piece of the
early stage: it moves live filtering from build time to delivery time while the
filter value is still the legacy single value. Behaviour-preserving in principle,
and in practice **it is the change that could make content appear or disappear on
real screens.** Dev cannot exercise it. Section 15.4's 2x2 is the guard, and it
must exist before this lands.

**This was a sequencing decision recorded in the spec rather than a licence to
begin building.** The layout stage has since been ticketed as
[DXME-227](https://radianttechnology.atlassian.net/browse/DXME-227), which carries the
scope, the tests and the pre-ship checks; it also **blocks
[DXME-191](https://radianttechnology.atlassian.net/browse/DXME-191)**, the sibling
defect in the same method, because it restructures the method DXME-191 fixes.

### 16.3 Two corrections to closed tickets, both settled

Found while assembling this spec, **confirmed by the map owner on 2026-09-15, and
commented on the tickets they correct** so nobody reopens either from the older
text. **Nothing here is pending and neither changes a design decision.**

Everything else in this spec is settled too: no resolved ticket contradicts a
charting premise, fourteen premises were amended, every amendment is itself
settled, and only the surviving decision appears in this document.

Both have the same root cause, which is worth naming once: **the EF model is not a
reliable description of this database's metadata indexes.** Three of the five
relevant tables disagree, and the two tickets each reasoned from the model. Section
4.1 carries the audit and the rule that follows from it.

#### Correction 1: the migration plan is one context, not two (section 4.6) Two closed tickets
place two of the five migrations on the Identity context, and that is not
executable: `IdentityDbContext` has no migrations infrastructure, and the three
tables in question are modelled in `DirectorDbContext`, created by the Director
`InitialCreate`, and present in the Director snapshot.

**Nothing in the substance changes.** The same two indexes are dropped and the same
one is added; only the folder and the "two contexts" framing change, and one
ordering constraint disappears. One ordering constraint disappears with it,
because there is now a single script.

#### Correction 2: migration 5 reconciles rather than adds (section 4.6)

DXME-214 decision 9 rests on the registry having no database uniqueness on
`MetadataKey`. True of the model, **false of the dev database**, which carries
`UQ_MetadataDefinition_Key` as a unique constraint that no model declares. Creating
the proposed `UQ_MetadataDefinition` would add a second redundant constraint on the
same column.

**This one does change a migration's contents**, so it is the one to look at first:
migration 5 becomes declare-and-create-where-absent. The decision's reasoning is
untouched and still load-bearing for section 12.4's by-name resolve, which on dev
is already satisfied.

### 16.4 Still not specified

In scope for the destination, not yet sharp enough to build. These are the map's
own fog, carried here so a build session knows what is deliberately absent rather
than forgotten.

Nothing remains. The last item, whether a Playlist location ever links an Asset
Library, is decided in section 10.7.

Two adjacent problems are large, separate, and deliberately untouched:

- **Reconciling the sidecar and database metadata stores.** `Playlist.razor` builds
  its entire content list by parsing `.metadata` XML sidecars off disk, while every
  targeting decision here reads `tblAssetContentMetadata`. `MediaAssetUploader`
  writes both, nothing reconciles them, and nothing detects drift. Ruling it in
  would swallow the work. **This spec declines to add new drift while leaving the
  old**: section 7.7 moves edit mode's per-keystroke sidecar-only write onto Save.
- **`Location.razor`'s silent component dispatch.** When `ComponentTypeName` names a
  type no longer in any loaded assembly, `ResolveComponent` returns and the location
  renders an empty page with no log line and no build error. Section 2.5 removed the
  one row that would have hit this rather than fixing the dispatch.

### 16.5 Things measured once, not to be re-measured

- The delivery pass at N=100 shuffled: **357.6 us**, 80% of it the delivery-version
  SHA256, 1% the filters. 2,797 requests/second per core. 90 players cost 0.018% of
  one core normally and 0.32% worst case (section 8.12).
- One publication rebuild at N=100: **120 to 420 ms**, dominated by 200 sequential
  database round-trips, plus 18.6 ms of sidecar parsing memoised nowhere.
- **Memoising the delivery pass is explicitly not needed at current scale.** If it
  is ever needed, the memo key is a metadata combination and it belongs at the
  **delivery** layer, never in the publication cache (section 8.13).

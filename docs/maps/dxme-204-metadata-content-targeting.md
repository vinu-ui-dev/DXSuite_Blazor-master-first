# Map: metadata-driven content targeting and scope for playlist locations

The body of the [DXME-204](https://radianttechnology.atlassian.net/browse/DXME-204)
wayfinder map. Moved out of the Jira epic on 2026-09-15, after that body hit
Jira's description size cap three times in two days and had to be compacted
twice, following the same move made on the
[DXME-159](https://radianttechnology.atlassian.net/browse/DXME-159) and
[DXME-120](https://radianttechnology.atlassian.net/browse/DXME-120) maps.
`git pull` and read this before choosing a ticket.

**Nothing about the mechanics changed.** Tickets are still child issues of the
epic, blocking is still the native Jira link type, and a session still claims a
ticket by assigning it to the map owner before doing any work. The frontier is
still the open, unblocked, unassigned children of the epic, and it still renders
in Jira's own UI. Only the prose moved.

**Editing rule.** Edit this file and commit. Do not restore the body to Jira. A
decision lives in exactly one place, its ticket; this file only gists it and
links. The ADF representation Jira measures is roughly triple the markdown, and
compressing entries to fit cost exactly what makes a map readable to a session
that has never seen the effort.

## Destination

An implementation-ready spec at `DXSuite_Blazor/docs/spec/metadata-content-targeting.md`
on `dev`, with a code-changes-by-file section a build session can execute
without making further design decisions. **The destination is the spec, not the
built feature.**

Two mechanisms, both keyed off metadata, never to be conflated:

- **Content targeting**: which assets reach which devices.
- **Content scope**: which assets a user can see, plus **content ownership**,
  which assets a user may change.

Playlist location types only for **targeting**. **Scope** follows the access
rule wherever one exists, which
[DXME-208](https://radianttechnology.atlassian.net/browse/DXME-208) established
includes the Asset Library.

## Notes

**Repo**: `C:\CODE\DXSuite_Blazor`, branch `dev`. See `CONTEXT.md`.

**Working tree warning.** The main tree is often parked on a prototype or
feature branch and diverged from `dev`. Use a worktree rather than switching it.

**Skills**: every ticket calls `grilling` and `domain-modeling` unless its type
says otherwise. Prototype tickets also call `prototype`; research tickets call
`research`.

**Standing preferences**: no em dashes in UI copy or doc deliverables. New
config UI uses the StyleGuide `dx-panel` structure. No back-compat shims. Match
surrounding comment density. **The spec does not specify modal button order**
([DXME-210](https://radianttechnology.atlassian.net/browse/DXME-210)), because
Save-right-of-Cancel is app-wide remediation Ian is driving separately.

**This map is an INDEX, not a store.** Every entry below is a gist; the named
ticket's resolution comment holds the detail. Do not restate ticket detail here.

### Vocabulary

Belongs in `CONTEXT.md`.

- **Content targeting** / **content scope** / **content ownership** as above.
  Matching a user scope does not grant control; ownership does.
- The location setting reads **"Target content by metadata"**.
- Access levels show **Full / Scoped / View only** while the DB keeps `full` /
  `partial` / `view`.
- Untargeted content is badged **"Shared"**.
- The targeting subject is a **Player**, never a Display
  ([DXME-210](https://radianttechnology.atlassian.net/browse/DXME-210)).
- The uploader's identity control is **"Uploading as"**, naming an identity,
  never a role or persona
  ([DXME-212](https://radianttechnology.atlassian.net/browse/DXME-212)).
- **Preview** always means the content preview; the Layout page's zone geometry
  view is a **zone layout map**
  ([DXME-216](https://radianttechnology.atlassian.net/browse/DXME-216)).
- **Reach** is how many of a location's players receive a given asset. The
  uploader shows it live as values are picked
  ([DXME-222](https://radianttechnology.atlassian.net/browse/DXME-222)).

### Invariants

**Publication versus delivery**
([DXME-216](https://radianttechnology.atlassian.net/browse/DXME-216),
[DXME-213](https://radianttechnology.atlassian.net/browse/DXME-213)): a cached
manifest is a **publication** and must be device-neutral; anything varying by
the asking device belongs to **delivery**. Approval is device-neutral, targeting
is device-dependent, the clock is device-dependent. That is the seam, and it now
holds on all three manifest paths. **A publication is a function of content,
never of configuration**, which makes the invalidation trigger list derivable
rather than remembered.

**The registry is the vocabulary, the rule table is the participation**
([DXME-214](https://radianttechnology.atlassian.net/browse/DXME-214)).
`TblMetadataDefinition` defines what keys exist; `TblLocationMetadataRule`
decides which of them target at a location. Not a partition: a key can be
targeted at one location and URL-injected at another. A key-name column is a
**third role**, a reference into the vocabulary
([DXME-217](https://radianttechnology.atlassian.net/browse/DXME-217)).

**Targeting and scope are separable axes**
([DXME-208](https://radianttechnology.atlassian.net/browse/DXME-208)). Targeting
is asset-side against device-side, Playlist-only; scope is user-side against
asset-side and follows the access rule anywhere.

**Viewing unions, uploading picks**
([DXME-209](https://radianttechnology.atlassian.net/browse/DXME-209)). Scope
values are the user's own unioned with her current groups'; tagging values come
from one picked identity, no union.

## Settled at charting (2026-09-14)

Premises every ticket inherits. Do not re-litigate; if an answer contradicts
one, say so and raise it. **Read the Amendments below before relying on any
premise. Premises 4, 6, 7, 8, 9, 10, 11, 12, 14, 15, 17, 18, 20, 21 and 22 are
amended. Every amendment is settled; nothing is open.**

1. Destination is the spec document, not the built feature.
2. Both targeting and scope are in scope; targeting is the spine.
3. Supersedes [DXME-24](https://radianttechnology.atlassian.net/browse/DXME-24),
   whose hardcode inventory is preserved in the audit ticket.
4. Playback is decided asset-side against device-side only. Location metadata is
   configuration, not a third operand.
5. Approval and metadata are independent gates, ANDed. A location may have
   either, both or neither.
6. Blank handling is two settings per field: *untagged content* and *untagged
   players*. Defaults preserve today's behavior.
7. Rule shape is a per-field matrix plus one location-level ALL / ANY toggle,
   structured so a nested expression builder can land later without migration.
   Not built here.
8. Asset metadata becomes multi-value. Device metadata stays single-value. The
   user side's `.First()` calls get fixed.
9. Rules live in a first-class table, not `TblLocation.ExtendedData`.
   `CustomMetadataFieldDef` folds into it.
10. Preview hides excluded content by default, with a diagnostic toggle showing
    the reason, and the filter-value box is replaced by the device's real
    metadata.
11. Scoped access uses the same evaluator and the same ALL / ANY semantics.
    `MetadataFilterKey` is retired and `GetMetadataFilterKeyForLocationAsync`
    deleted rather than generalized.
12. All inclusion predicates unify onto one evaluator. The approval bypass in
    `ShouldIncludeFileByMetadataAsync` is fixed on this map.
13. Clean cut on `HotelNo`: sidecar reader and every fallback deleted in one
    release. No dual path, no migration tool.
14. Manifest cache variant key becomes a stable hash of the device's values for
    the participating keys. Rule edits invalidate every variant for that
    location. DXME-178 absorbed here.
15. Ownership is new schema: `UploadedByUserId` and `UploadedByUserGroupId` on
    `TblAsset`. Existing assets get NULL.
16. Group-owned content is controllable by any current member of the owning
    group.
17. Scoped users get an upload picker limited to their own values. Full-access
    users get an unrestricted one. Removes the silent `.First()` tagging.
18. Ownership gates edit, replace, delete and re-tag. It does not gate approval.
19. Access matrix: Full sees and controls everything; Scoped sees matching
    content and controls only what it or its group owns; View controls nothing.
    Scoped needs a visible affordance.
20. A device matching nothing plays nothing, legitimately, but never silently:
    the rule builder shows live how many devices would receive zero assets. No
    automatic fallback.
21. Targeting keys must come from `TblMetadataDefinition` on every side.
    Free-text keys survive only for non-targeting uses.
22. The per-location "Target content by metadata" switch is the rollout flag,
    defaulting to off.

## Amendments to the premises

Gists only. **Go to the named ticket before relying on any of these.**

**P4** ([DXME-207](https://radianttechnology.atlassian.net/browse/DXME-207)):
`tblUserGroupMetadata` was never read by `LocationAccessService`, so group
metadata was not a scope operand. DXME-207 made it one.

**P6** ([DXME-207](https://radianttechnology.atlassian.net/browse/DXME-207)):
underdetermined, not wrong. Untagged polarity was **inverted between preview and
device**. Resolved to the device side: untagged content plays on all players.
DXME-211 fixed the preview, DXME-222 carries it to the uploader.

**P7** ([DXME-209](https://radianttechnology.atlassian.net/browse/DXME-209)):
over-specified. `match mode` is not a rule-row column; ALL / ANY is location
level. The rule row holds participation and blank policy only.

**P8** ([DXME-209](https://radianttechnology.atlassian.net/browse/DXME-209)):
wrong on every side. The device index already exists; the asset index is unique
so multi-value is a **drop** plus an upsert rewrite (crash sites at
`StatusSelectorService.cs:182`, `TicketCounterScheduler.razor:1266`); the user
and group sides become genuinely multi-value. Multi-select is **already CSV in
one cell**, split under the registry's authority, illegal afterwards. The index
drop also removes the only database-level single-value guarantee, which
[DXME-217](https://radianttechnology.atlassian.net/browse/DXME-217) closes at
the importer.

**P9** ([DXME-208](https://radianttechnology.atlassian.net/browse/DXME-208),
[DXME-216](https://radianttechnology.atlassian.net/browse/DXME-216)): the rule
table holds the key list for any location type; only Playlist locations add a
matrix. A **Layout** location authors none and needs no rule row. The claim once
recorded here, that nothing authors those rows for a non-playlist location, was
**false** and is corrected by
[DXME-223](https://radianttechnology.atlassian.net/browse/DXME-223): the admin
Metadata modal already authors them for every content-owning type, libraries
included.

**P10** ([DXME-211](https://radianttechnology.atlassian.net/browse/DXME-211)):
reshaped, gained a mode. Two subject modes, Player (real, read only) and Mock
player (editable, single select). **"Any device" is deleted.** With targeting
off the modal collapses to today's shape.

**P11** ([DXME-209](https://radianttechnology.atlassian.net/browse/DXME-209)):
`MetadataFilterKey` is **overloaded**, a metadata key on `partial` rules but a
required `__none__` / `__all__` enum on `view` rules. NULL everywhere only
because zero view rules exist. Migration 3 **replaces** it with `ViewScope
varchar(20) NULL`. Collided with Out of scope at
`PlaylistLocation_Radiant.razor:1408`; both legacy components are **deleted
outright** (DXME-219). Retiring it also takes **access-rule edits off the
invalidation trigger list entirely**
([DXME-213](https://radianttechnology.atlassian.net/browse/DXME-213)).

**P12: ten predicates, not three**
([DXME-205](https://radianttechnology.atlassian.net/browse/DXME-205),
[DXME-208](https://radianttechnology.atlassian.net/browse/DXME-208)). Four in
`SignageController` (one dead, deleted), three in `EffectiveContentResolver`,
`PlaylistManifestRules.ShouldInclude` as the shared leaf,
`RunAsFilterModal.MatchesMetadata` (already implements P7's matrix semantics,
prior art to absorb), and `LocationContentApiController.GetAssets:194-227`,
which emits `CanEdit` / `CanDelete` **from scope rather than ownership**.
DXME-215 must write "ten", and must write **nine ported, one deleted**:
`MatchesMetadata` goes with its modal rather than onto the evaluator
([DXME-222](https://radianttechnology.atlassian.net/browse/DXME-222)).

**P13 holds, with a residue.** `HotelNo` is 8 asset rows, all `67890`, at
locations 1044 and 1046; seven sit on a Playlist location and are deleted rather
than orphaned. Dev could not speak for production, and
[DXME-218](https://radianttechnology.atlassian.net/browse/DXME-218) closed that
gap **on the map owner's authority rather than by a sweep**: `HotelNo` was demo
only and never reached a customer database. No production query was run and none
is needed. **A
second hardcoded key** exists that P13 never named
([DXME-214](https://radianttechnology.atlassian.net/browse/DXME-214)):
`DeviceMetadataService.cs:112-118` throws unless a `zipCode` value is five
digits. Deleted alongside the sweep.

**P14 is deleted**
([DXME-213](https://radianttechnology.atlassian.net/browse/DXME-213)). **There
is no variant key and no hash.** The playlist path takes the same delivery seam
as the layout path, so `BuildCacheKey` drops its filter component everywhere:
one entry per location, one per display, count bounded, no eviction policy
needed. The key gains a `display` / `loc` discriminator, fixing
`StatusSelectorService` invalidating a display's manifest by location id. Rule
config edits invalidate **only a rule cache**, device metadata edits invalidate
**nothing**, and the TTL goes to a uniform thirty minutes. DXME-178 absorbed and
closed.

**P12, 14, 20, 22 at the layout zone path**
([DXME-216](https://radianttechnology.atlassian.net/browse/DXME-216)). The
layout path is the **only one of three parallel implementations** missing the DB
approval check: a **live approval leak**. Playlist-sourced zones **do**
participate, with the **source** location as subject, evaluated **per source
location at delivery time**. P14's converse holds: invalidating a source
location must also invalidate every layout sourcing it, which nothing does
today. P20 reads at zone granularity as a **named dependency**, since a zone
filtering to empty is a blank rectangle on a composed screen. P22's switch
belongs to the **source** location. Also: the cached publication is **not
device-neutral today**, because URL injection runs at publication, so the first
device to poll bakes its own metadata and deviceId into every other device's web
content URLs.

**P15, P17**
([DXME-212](https://radianttechnology.atlassian.net/browse/DXME-212),
[DXME-209](https://radianttechnology.atlassian.net/browse/DXME-209)): reshaped
by Uploading As. The scoped user picks **who she is uploading as**, one entry
per matching `partial` rule; that identity supplies the values **and** becomes
the owner, so tagging and ownership cannot disagree. Cardinality is
**type-driven**. No union across identities. Only participating keys are tagged.
**Nine** `TblAssets.Add` sites, not four, and `CreateLayoutModal` creates no
asset at all. `ContentOverwriteService` copies no tags today, a defect the map
inherits. **The owning group is picked, never derived**, enforced by a
write-time guard, no DDL change.

**P18** gains resubmit
([DXME-209](https://radianttechnology.atlassian.net/browse/DXME-209)): the
`Playlist.razor:570` gate moves onto the ownership columns, widening resubmit to
any current member of the owning group. **Three silent first-wins killed**:
`MediaAssetUploader.razor:1084`, `LocationAccessService.cs:180`,
`ApprovalChainService.cs:66`.

**P21 was read as a partition, and is not one**
([DXME-214](https://radianttechnology.atlassian.net/browse/DXME-214),
[DXME-217](https://radianttechnology.atlassian.net/browse/DXME-217)).
Participation is the gate: an unregistered key can never be targeted because the
rule table cannot reference it, so the editor constraint is a **guardrail**. The
registry **already holds the injection keys**. `DeviceMetadataEditor.razor` is
**dead code** and is deleted; the live editor (`ContentChannelsTab.razor:328-339`)
is **already** registry-backed. **206 of 237 devices have no authoring surface at
all**, so a metadata section is built into `DevicesTab`. **The registry is
unguarded**: no unique index on `MetadataKey`, no entity-type scope, unvalidated
`FieldType`. DXME-214 adds a fifth migration on the Identity context plus
server-side validation.

**P21 again, and the count is six, not four**
([DXME-217](https://radianttechnology.atlassian.net/browse/DXME-217)). A key name
is stored in **six** places and **every live authoring surface for all six is
already a registry dropdown**, so the constraint needs no work. The two the map
never named: `tblAccessSchedule.MetadataKey`, which **outranks**
`tblDisplay.StatusSelectorMetadataKey` at runtime through `metadataKeyOverride`
and is also read by `LocationAccessService`; and
`tblFloorPlanIconSchedule.MetadataKey`, which has **no authoring surface
anywhere**. `tblDisplay.StatusSelectorMetadataKey` was never invisible to the
registry, correcting DXME-214's own handoff line.

**P5 is not contradicted**
([DXME-207](https://radianttechnology.atlassian.net/browse/DXME-207)): approval
moves outside the evaluator and is ANDed by targeting call sites.

**Comparison**
([DXME-207](https://radianttechnology.atlassian.net/browse/DXME-207),
[DXME-214](https://radianttechnology.atlassian.net/browse/DXME-214)). Target is
case-insensitive, trimmed, ordinal. **Today is case-SENSITIVE and untrimmed**:
`PlaylistManifestRules.cs:51`, `SignageController.cs:1809` and `:1857-1865`,
`EffectiveContentResolver.cs:300-304`, `ScheduleAssetPanel.razor:501,532,558`,
and `FloorPlanService.cs:154-159`. No write path normalises anything. Ruled:
trim on write everywhere, and nothing else. The sweep **does not reach
`StatusSelectorService`**
([DXME-217](https://radianttechnology.atlassian.net/browse/DXME-217)).

**Naming**: there is no `ContentMetadataService.cs`; the interface is
`IContentTagService`. The Director metadata table is `tblAssetContentMetadata`
([DXME-216](https://radianttechnology.atlassian.net/browse/DXME-216)).

**RunAsFilterModal was misread at charting**
([DXME-211](https://radianttechnology.atlassian.net/browse/DXME-211)). It is a
bare metadata **value picker** with one caller, reading no user, group, access
rule or ownership. It folds into DXME-211 as the **mock player subject**, not a
scope surface; DXME-211's questions 7, 8 and 10 are void. **Scope and ownership
belong to DXME-212** and must not drift into Preview. DXME-222 owns what the
uploader gets instead.

## Standing requirements from the map owner

**StatusSelector functionality and its metadata key handling are left
unchanged** (Ian, 2026-09-15, on
[DXME-217](https://radianttechnology.atlassian.net/browse/DXME-217)). Binding on
every remaining ticket. Consequences already recorded: the comparison
normalisation sweep skips `StatusSelectorService`; the hardcoded
`"Uncategorized"` group stays and is **not** reconciled with `UntaggedContent`;
no grouping semantics change. Fixes that keep the rendered result identical, such
as replacing a throw with a first-value-wins grouping, are inside the
requirement.

## There is almost no data behind any of this

- 4 device metadata rows across 236 devices. **230 of 237 have a NULL**
  `LocationID`.
- 68 access rules, **all** `full`, `MetadataFilterKey` NULL on every one. Zero
  `partial`, zero `view`. So P11 is free to implement, the approval bypass is
  **latent, not leaking**, and DXME-209 ruled out an ownership backfill.
- 318 live assets, 56 with an approval row (49 Approved, 3 Declined, 4
  Withdrawn, zero Pending), 4 distinct uploaders. `tblUserMetadata` is empty.
- No asset carries more than one key, so P7's ALL mode has no data exercising
  it. 26 asset rows name keys their location does not declare, which the rule
  builder must surface.
- **Zero unregistered device keys**, and **zero unregistered keys in any of the
  three reference stores**. The only unregistered keys anywhere are `HotelNo`
  and `videoWallAssignments`. **Exactly one off-list value**: `Category =
  "Category 2"` on 2 rows, orphaned by a registry edit, which is the real
  mechanism rather than operators typing badly.
- Three literal `"HotelNo"` fallbacks at `SignageController.cs:763, 843, 1669`;
  DXME-216 removes two. **Three separate** `AppendMetadataToUrl`
  implementations fold into one.
- **Exactly one Layout location**
  ([DXME-216](https://radianttechnology.atlassian.net/browse/DXME-216)): 1038, 8
  zones, 4 playlist-sourced (1041-1044) and 4 display-sourced. All 8 access
  rules in that graph are `full`/NULL. The only asset tags in it sit on Playlist
  1044.
- **Key-name reference stores**
  ([DXME-217](https://radianttechnology.atlassian.net/browse/DXME-217)):
  `tblDisplay.StatusSelectorMetadataKey` 50 rows, all `ticketCounterStatus`,
  across 2 VideoWall locations (2162 West Backwall, 3214 Demo Ticket Counter
  Backwall). `tblAccessSchedule.MetadataKey` 4 rows, same key.
  `tblFloorPlanIconSchedule.MetadataKey` 0 rows. Registry: 17 definitions.
- **Dev cannot speak for production on volume either**
  ([DXME-177](https://radianttechnology.atlassian.net/browse/DXME-177)): 4 to 5
  assets per location here, against a working figure of 100. DXME-226 owns
  authored fixtures.

## Decisions so far

One gist per closed ticket; detail lives in the ticket.

- [Audit the inclusion predicates and the HotelNo inventory](https://radianttechnology.atlassian.net/browse/DXME-205):
  Nine predicates, not three, since raised to ten.
  `ShouldIncludeFileByMetadataAsync` never reads approval. HotelNo is 29 refs
  across 10 files, only 5 live. Branch
  `research/metadata-targeting-predicate-audit`.

- [Survey the metadata present in the dev and demo databases](https://radianttechnology.atlassian.net/browse/DXME-206):
  Targeting has almost no data behind it. Multi-select is already CSV in a
  single cell. Branch `research/metadata-targeting-data-survey`.

- [Pin the evaluator truth table](https://radianttechnology.atlassian.net/browse/DXME-207):
  The per-key result is **Match / NoMatch / Abstain**, not a boolean, dissolving
  the ANY-veto, both-blank and unknown-key ambiguities at once. `UntaggedPlayers
  = Nothing` is a pre-matrix device gate. Multi-value is any-of-any. One
  evaluator, exactly three divergences for scope. 12-row truth table. Branch
  `spec/metadata-evaluator-truth-table`.

- [Decide whether scope filters the Asset Library](https://radianttechnology.atlassian.net/browse/DXME-208):
  Yes, against the library location's **own** access rule. The map's stated
  reason for excluding it was false. Established that targeting and scope are
  separable axes; found the tenth predicate.

- [Design the schema](https://radianttechnology.atlassian.net/browse/DXME-209):
  Twenty decisions, then **sixteen more on reopening**. Full DDL, four
  migrations across **two** DbContexts. Participation is **row presence**,
  affordable only because **removing a key stops hard-deleting every asset tag
  for it**. Ownership is two nullable `NO ACTION` FKs, never invented. The
  reopening made **the owning group picked, never derived**, changing no DDL.
  Amended by DXME-214 with a fifth migration, and by DXME-217 with an importer
  fix in migration 2's blast radius.

- [Prototype the rule builder](https://radianttechnology.atlassian.net/browse/DXME-210):
  **Variant E of five.** A matrix table staying one line per field: `Field |
  Assets tagged | Content with no value | Players with no value | Players tagged
  | remove`. ALL / ANY sits above the grid. Explanations are tooltips, which is
  what buys the one-line row. Three coverage counters. Controls disable with a
  reason rather than vanishing. Artifact
  [Mxi45WfjzxjfX8aDFx5Hzh](https://claude.ai/artifact/Mxi45WfjzxjfX8aDFx5Hzh),
  branch `prototype/metadata-rule-builder` at `b4b3037`. Graduated DXME-221.

- [Prototype the preview rework](https://radianttechnology.atlassian.net/browse/DXME-211):
  **Variant A of three.** One list, excluded rows greyed in place with the
  reason beneath. Subject becomes two modes, Player and Mock player. **"Any
  device" is deleted.** **Run is deleted, Preview stays.** With targeting off the
  modal collapses to today's shape. Corrected the map's false Run As premise.
  Artifact
  [S9igR74tmxQs833ZS4F6tb](https://claude.ai/artifact/S9igR74tmxQs833ZS4F6tb),
  branch `prototype/metadata-preview-rework` at `9d55d18`. Graduated DXME-222.

- [Prototype the scoped upload picker and ownership affordances](https://radianttechnology.atlassian.net/browse/DXME-212):
  **Variant B of three**, question reshaped mid-session. The user picks **who
  she is uploading as**, not values from a pool; that identity supplies values
  **and** becomes owner. Cardinality is type-driven, amending P17. Ownership
  surfaces only when it takes a control away, names the group and never the
  person. Artifact
  [LJoC1oBJqxgTLKebcgYAiY](https://claude.ai/artifact/LJoC1oBJqxgTLKebcgYAiY),
  branch `prototype/metadata-upload-identity` at `94cbfa2`. Reopened DXME-209,
  graduated DXME-223.

- [Decide manifest cache variant keying and invalidation](https://radianttechnology.atlassian.net/browse/DXME-213):
  **There is no variant key.** The playlist path takes DXME-216's seam, so the
  filter component leaves `BuildCacheKey` everywhere and P14's hash is deleted
  along with four of the ticket's own eight questions. **A publication is a
  function of content, never of configuration**, which makes the trigger list
  derivable rather than remembered. Absorbed and **closed DXME-178**, raised all
  three TTLs to a uniform thirty minutes, and **overturned three claims in**
  `time-zone-scheduling.md` **section 5** (annotated in place on branch
  `spec/timezone-section5-superseded`, DXME-158/159 commented). Client-side
  targeting was reopened and rejected, decisively because metadata only ever
  changes at the server. DXME-177's measurements confirm the cost argument: the
  filter lands in a 1% slice of a pass costing 0.32% of one core at worst-case
  cadence. **Always check DXME-177 before arguing cost on this map.** Graduated
  DXME-226.

- [Decide how device metadata is constrained to the registry](https://radianttechnology.atlassian.net/browse/DXME-214):
  Thirteen decisions; the framing was corrected twice by fact. **The registry is
  the vocabulary and the rule table is the participation**, so the editor
  constraint is a guardrail. **The device side is always single value.**
  Removing a registry option warns with a cross-side count and proceeds. **Trim
  on write everywhere, nothing else.** **URL injection unchanged**: an allowlist
  was designed and rejected because it fails invisibly.
  `DeviceMetadataEditor.razor` deleted and a metadata section built into
  `DevicesTab`. Bulk tagging ruled out of scope.

- [Decide whether layout zones participate in targeting](https://radianttechnology.atlassian.net/browse/DXME-216):
  Sixteen decisions. **Playlist-sourced zones participate, source location as
  subject, evaluated per source location at DELIVERY time**, because approval is
  device-neutral and targeting is device-dependent and the seam already exists
  for card zones. The layout cache key **drops its filter component**.
  Display-sourced zones are **out of scope**, stated rather than left silent.
  Found a **live approval leak** and a **live cross-device identity leak**. The
  `.json` exemption is deleted, invalidation gains a layout-to-sources map, and
  the runtime branches on the source location's **type**. Two stages, the
  no-migration half able to land ahead.

- [Decide whether the registry constraint governs tblDisplay.StatusSelectorMetadataKey](https://radianttechnology.atlassian.net/browse/DXME-217):
  Eleven decisions, and **three of the ticket's five questions dissolved on
  contact with the code**: `DisplayEditModal` is already registry-backed, the
  Ticket Counter Scheduler writes no key column at all, and `ticketCounterStatus`
  is already a first-class registry key. **A key name is stored in six places,
  not four**, and every live authoring surface for all six is already a registry
  dropdown, so **the constraint needs no work**. The real defect is the registry
  **delete guard**: a hard block whose device, content and access-rule counters
  are hardcoded zero, so on dev every key deletes with no warning. It becomes
  **warn and proceed**, counting all six stores and reporting **tag stores and
  reference stores separately**, because an orphaned tag is invisible while an
  orphaned reference degrades a screen. The counts are gathered by a
  **contributor seam**, each module counting its own tables, which is also where
  DXME-214 decision 4's cross-side count lands. Deleting a definition is
  **runtime-inert**, which is what makes warn-and-proceed affordable. Ian set a
  standing requirement that **StatusSelector functionality is unchanged**, which
  withdrew an earlier multi-value grouping recommendation. Chasing his question
  about Status cardinality exposed a **gap between two settled tickets**:
  DXME-209's index drop removes the only database-level single-value guarantee
  and DXME-212's type-driven rule is an editor rule, so
  `ContentPackageBundleImporter.cs:631`, the one write path not going through
  `ContentTagService`, is fixed to match on `(AssetId, MetadataKey)`. No branch;
  the resolution comment is the store.

- [Sweep a production database for HotelNo before the clean cut deletes it](https://radianttechnology.atlassian.net/browse/DXME-218):
  **Resolved without the sweep.** Ian confirmed directly that `HotelNo` was demo
  only and reached no production database, which is the fact the ticket existed
  to obtain and outranks sampling any one customer database. The clean cut
  proceeds unchanged: reader, all three `SignageController` fallbacks and the
  `zipCode` five-digit throw go in one release, with **no recovery path by
  design**. The **eight dev rows are deleted by the cut**, one `DELETE` beside
  the code deletion rather than a migration, so seven live Playlist tags are not
  left keyed to a key in neither the registry nor any participating key set.
  Also made the ownership backfill's exclusion **unconditional**, since its only
  reopening condition was production evidence that will never be gathered. No
  production Director connection is configured for agent sessions; recorded so a
  later session does not hunt for one. No branch; the resolution comment is the
  store.

- [Delete the legacy Radiant and Omnivex playlist location components](https://radianttechnology.atlassian.net/browse/DXME-219):
  The premise under test, "legacy and used nowhere", **held for Radiant and
  failed for Omnivex**. Radiant was referenced by nothing and was the **only**
  legacy caller of `GetMetadataFilterKeyForLocationAsync`, so **deleting it alone
  fully unblocked P11**. Omnivex referenced no metadata anything and was never in
  P11's radius; it was the registered component for **seeded location type 5**,
  "Playlist Location - Moxie", so deleting it was a location-type scope call
  rather than a step on this route. Raised, and Ian ruled the full clean cut.
  Type 5 leaves `director-seed.sql`, the SLC patch script and existing databases
  via migration `RemoveMoxiePlaylistLocationType`, which deletes the row **only
  where no location references it**. Established that `Location.razor:398`
  dispatches on `ComponentTypeName` by **reflection**, so a missing component
  fails **silently** with a blank page rather than breaking the build, and that
  **no admin surface writes `ComponentTypeName`**: the repo's own seed and patch
  scripts are its only writers. Branch
  `task/dxme-219-delete-legacy-playlist-components` at `996ca32`.

- [Decide whether targeting rules and ownership travel through the content package subsystem](https://radianttechnology.atlassian.net/browse/DXME-220):
  Nine decisions, and the code reframed the ticket's first question entirely.
  **The participating-key list already travels**, because
  `LocationRecord.ExtendedData` is carried verbatim and **overwritten wholesale**
  at `ContentPackageBundleImporter.cs:298`, so registering
  `TblLocationMetadataRule` is regression avoidance rather than gold-plating and
  ticket item 5 resolves to in scope. The rule table **upserts by the natural
  key** `(LocationId, MetadataKey)` with the target keeping its own
  `ContentPackageId`, the same move DXME-217 decision 10 makes on the tag table
  and against every other entity block's convention. Replace mode gains a
  natural-key `keepMetadataRules`. A rule's key **resolves by name against the
  target registry and skips with a warning** when unregistered, because an
  orphaned *rule* is invisible in the builder yet active in targeting, unlike an
  orphaned *tag*, which stays supported. Ownership travels as UPN and group name,
  **each half nulling independently**, never falling back to the system user;
  **user-NULL-with-group-set is legal and only the importer writes it**. Both
  `TblLocation` columns travel and the **import preview gains a zero-device
  targeting check**, since **no device entity is packaged at all**, so targeting's
  device operand can never arrive in a bundle. `MetadataFilterKey` becomes
  `ViewScope` at `SchemaVersion` 4, and a pre-v4 location record is
  **translated**, its `customMetadataFields` folded into rule rows and stripped
  from the blob, with `MetadataMatchMode` coalesced to `'all'` so the import does
  not abort on a NOT NULL column. No branch; the resolution comment is the store.

- [Decide where bulk deletion of asset metadata tags lives](https://radianttechnology.atlassian.net/browse/DXME-221):
  Twelve decisions. **Nothing owns bulk tag deletion.** The capability is
  answered at per-asset granularity and the dangling reference is deleted
  rather than rehomed. The hole was never bulk:
  `AssetMetadataEditModal.Open()` builds its values from the location's
  declared fields and **discards every other row it loaded**, so a tag whose
  key was removed is invisible and unclearable anywhere in the product. The
  modal gains an orphaned-tag group, defined by **rule-row absence** and
  indifferent to registry membership, and is wired into the **Playlist**
  content list beside the Asset Library, ownership-gated and **always shown**
  so the empty state cannot suppress orphans. A registry-side purge was
  refused because deleting a definition is **runtime-inert**, which is exactly
  what makes DXME-217's warn-and-proceed affordable. Also owns a pre-existing
  defect the widening walks into: a `list` value absent from the registry
  Options **renders as unset while targeting still evaluates it**, which
  DXME-206 already found on dev. DXME-207 section 10's delete-row counter is
  deleted, which is **not** DXME-210's three coverage counters; DXME-215 must
  not conflate them. Corrected DXME-210 decision 10's pointer in place. No
  branch; the resolution comment is the store.

- [Decide what the uploader's playlist preview gets in place of RunAsFilterModal](https://radianttechnology.atlassian.net/browse/DXME-222):
  Fourteen decisions. **It gets nothing: the uploader's preview stops being a
  targeting surface.** `RunAsFilterModal` is deleted outright and the preview
  becomes an unfiltered splice, because the same page already carries a correct
  per-player answer one button away. The question the picker was answering badly
  moves onto the upload form as a **reach line**, live as values are picked,
  warning at zero and never blocking. The reach count runs on the **shared
  evaluator** through a new one-asset-many-devices entry point taking values
  rather than rows, so the tenth predicate is **deleted, not ported**. Also
  found: `PlaylistPreviewBuilder`'s doc comment claims a sharing that never
  existed, the uploader previews the **sidecar** store while delivery reads the
  database, `MediaAssetUploader.razor:866` is a live P11 caller the map never
  named, the approval radios are unreachable so **Declined content plays in the
  preview today**, and edit mode writes the targeting value to disk **per
  keystroke**, sidecar only. `locationMetadataFilterKey` dissolves into the
  participating key list and every key renders through `MetadataValueInput`,
  retiring the last unregistered authoring surface. Tagging leaves the
  uploader's edit mode entirely for `AssetMetadataEditModal`, amending DXME-221
  to a single re-tagging surface. No branch; the resolution comment is the
  store.

- [Decide where a non-playlist location's participating keys are authored](https://radianttechnology.atlassian.net/browse/DXME-223):
  Ten decisions, and **the ticket's premise was false**. A screen already
  authors these for a library, and it is the one the rule builder replaces:
  `CustomMetadataFields` has **one store and two writers, both admin-side**, and
  the Metadata button is gated on `LinkedToDisplay == false`, not on Playlist,
  so **8 of 10 Asset Library locations already carry a key list against 2 of 8
  Playlist locations**. The library is the dominant author of this list, not an
  omission from it. The gate is **kept**: Ian challenged removing it and
  `ContentLog.razor:210` uses the same predicate, making it the repo's existing
  shorthand for **"this location owns its own content"**, which is exactly where
  a key list means anything. The reduced shape is the **left half of variant E**
  unchanged, selected on the component type being Playlist through a new
  `IsPlaylist` helper rather than a type id. `BlankSubjectPolicy` is unauthored
  and unread, since DXME-207's first divergence makes scope abstain at subject
  level; `UntaggedContent` **is** read by scope, so it stays settable everywhere
  at `AllPlayers` with warn-and-proceed. Only two copy changes: the modal
  becomes **"Content metadata"** for every type and the targeting switch does
  not render where nothing is targeted. `CreateLayoutModal` is migrated as a
  second write site. Found two defects: `AssetLibrary.razor` **never sets**
  `MediaUploadConfig.UserAccess`, so DXME-212's identity flow has no path into a
  library, and the screen does **no scope filtering at all**, making DXME-208
  new behaviour there rather than a change. Also found that `AssetLibrary` loads
  by `ConstraintId` while `StatusSelectorService` loads the same library by
  `LocationId`, agreeing on dev but needing a deliberate choice in the spec. The
  standing requirement is untouched: all 50 StatusSelector displays point at one
  library, 2072, which has no access rule, so scope never engages there. No
  branch; the resolution comment is the store.

- [Decide how targeting test fixtures are authored and which layer tests them](https://radianttechnology.atlassian.net/browse/DXME-226):
  Nineteen decisions, and **the ticket's own precondition was stale**. CI has run
  `dotnet test` on push and PR to `dev` and `master` since `3ed1609` on
  2026-09-12, so DXME-177's "CI runs no tests at all" is false on `dev`;
  DXME-185 is still open in Jira despite its work having landed. **CI is the
  gate**, which converts into the binding constraint: **no test on this map may
  require a database, a browser or a file share**, because the runner has none of
  the three. **There is no integration layer and no browser layer.** This repo's
  230 tests construct **zero** `DbContext`s and no E2E suite exists anywhere, so
  coverage is unit-plus-extraction: `ResolvePlaylistDelivery` and the layout
  path's decision both come out as pure statics, their queries stay behind, and
  `InternalsVisibleTo` was rejected for hiding the shape problem rather than
  fixing it. After unification there are **one evaluator and nine call sites**,
  so the 12-row table is asserted **once** and each call site gets one test of
  its approval ANDing; re-running the table per call site is forbidden by name.
  The extraction budget **stops**, under the rule *extract where a decision is
  trapped behind a query, not where a query surrounds a decision that is already
  extracted*. Fixtures split three ways by kind and are built **per test, never
  shared**, because the 2x2's exact-set assertion is the most fragile thing to
  hang on shared state. Found what no ticket had named: **`ApprovalGateTests` is
  the only existing test in this path and it pins the very function the map
  dismantles**, so DXME-215 must name `ApprovalGateTests.cs` or a build session
  will make the red test green by restoring the old semantics. No branch; the
  resolution comment is the store.

- [Assemble the metadata content targeting spec](https://radianttechnology.atlassian.net/browse/DXME-215):
  **The map has arrived.** The spec is
  [`docs/spec/metadata-content-targeting.md`](../spec/metadata-content-targeting.md),
  sixteen sections, on branch `spec/metadata-content-targeting`. Only surviving
  decisions appear, with each overturn noted so nobody reopens it from the older
  ticket. **Found two corrections to closed tickets, both from
  reading the database rather than the EF model, both commented on their tickets.**
  (1) DXME-209 decisions 13 and 20 put two migrations on the Identity context,
  which is not executable: `IdentityDbContext` has no migrations folder or
  snapshot, nothing migrates it, in Director `IIdentityDbContext` **is**
  `DirectorDbContext`, and the tables are created by the Director `InitialCreate`,
  so no database can hold them without Director migrations having run. Five
  migrations, one context; no DDL changes and the cross-context ordering
  constraint disappears. (2) **DXME-214 decision 9's premise is false of the
  database**: `UQ_MetadataDefinition_Key` already exists on dev as a unique
  constraint on `MetadataKey`, declared in no model and created by no migration, so
  migration 5 becomes declare-and-create-where-absent rather than additive, and
  DXME-220's by-name-resolve precondition is already met. Root cause of both: **the
  EF model is not a reliable description of this database's metadata indexes**, on
  three of the five tables involved, all three of which this work changes. The spec
  therefore rules every index change is raw SQL behind a `sys` catalog lookup.
  Section 16.3 carries both; **both confirmed by the map owner
  2026-09-15** and commented on DXME-209 and DXME-214. Also
  added the `CONTEXT.md` "Content metadata" section, recorded **a publication is a
  function of content, never of configuration** on the Published-content entry and
  dropped access rules from its input list, and landed DXME-213's annotation of
  the three superseded claims in `time-zone-scheduling.md` section 5 so the
  spec's cross-reference is true. No fog graduated; nothing ruled out of scope.


## Not yet specified

**The route is walked.** Every ticket on this map is closed and the destination is
reached: the spec exists and is implementation-ready. **The build is downstream and
off-map**: [DXME-227](https://radianttechnology.atlassian.net/browse/DXME-227) is the
first build ticket, carrying section 11's no-migration half (the live approval leak
and the cross-device identity leak); the section 8 cache stage and the
five-migration release are not ticketed yet. What remains below is in
scope for the destination but was never sharp enough to ticket, and section 16.4
of the spec carries it so a build session knows it is deliberately absent rather
than forgotten. Nothing here blocks the build.

- How targeting composes with asset schedules and dayparting (DXME-64/65/66) and
  with airline states. Note DXME-216 recorded that playlist-sourced layout zones
  read no clock at all today, and DXME-213 put targeting in the same method as
  the window filter.
- Interaction with location bundles.
- Whether a **Playlist** location ever links an Asset Library through
  `TblDisplay.AssetLibraryLocationId`. None does today, which is the only thing
  keeping library content out of targeting
  ([DXME-208](https://radianttechnology.atlassian.net/browse/DXME-208)).
  `Schedule.razor` graduates with it.

## Out of scope

- **An integration test layer against a real `DbContext`**
  ([DXME-226](https://radianttechnology.atlassian.net/browse/DXME-226)). All 230
  tests in the repo construct none, and CI is a bare `windows-latest` runner with
  no `services:` block, no LocalDB step and no `sqlcmd`, so such a layer would be
  the only tests in the repo that never run. Coverage comes from extraction
  instead.
- **An E2E or browser test suite**
  ([DXME-226](https://radianttechnology.atlassian.net/browse/DXME-226)). None
  exists: no Playwright, Selenium or Puppeteer anywhere, and no login helper. The
  rule builder, preview and uploader are verified by the human checklist their
  prototypes already define. Building the suite is a new capability, not a step
  to the spec.
- **Coverage of `EffectiveContentResolver` orchestration and
  `LocationAccessService` queries**
  ([DXME-226](https://radianttechnology.atlassian.net/browse/DXME-226)). Both
  already delegate the deciding part to the shared leaf, so what is left is query
  plus orchestration and needs a database. Stated as a decision so the silence is
  not read as an oversight.
- **Consolidating the four `ITenantContext` test stubs**
  ([DXME-226](https://radianttechnology.atlassian.net/browse/DXME-226)). A tidy-up
  with no metadata content, by the same test DXME-219 applied to
  `Location.razor`'s silent dispatch. This map's tests reuse an existing stub
  rather than adding a fifth.
- Asset Library as a **targeting** surface. **Not** because it is untied to
  displays: `AssetLibraryLocationId` ties 63 displays across 7 host locations to
  4 libraries. It is out because **no Playlist location links one**. Content
  **scope** does reach it
  ([DXME-208](https://radianttechnology.atlassian.net/browse/DXME-208)), a
  decision on the route rather than a boundary.
- **Display-sourced (card) layout zones as a targeting surface**
  ([DXME-216](https://radianttechnology.atlassian.net/browse/DXME-216)). A Card
  location's content is chosen by the display's schedule rather than by
  metadata, so there is nothing to evaluate without extending targeting to Card
  locations. The per-device seam exists in `ResolveLayoutDeliveryAsync` and is
  **deliberately unused**; the spec says so rather than leaving the silence to
  be read as oversight.
- **A per-zone content preview on the Layout page**
  ([DXME-216](https://radianttechnology.atlassian.net/browse/DXME-216)). A new
  surface rather than DXME-211's rework. The page's existing "Layout Preview"
  heading is a zone geometry map and is renamed rather than extended.
- **A manifest cache inspector panel**
  ([DXME-213](https://radianttechnology.atlassian.net/browse/DXME-213)). A new
  surface; the logging is fixed instead and `Location.razor`'s existing version
  display starts working by construction.
- **Memoising the delivery pass**
  ([DXME-213](https://radianttechnology.atlassian.net/browse/DXME-213),
  [DXME-188](https://radianttechnology.atlassian.net/browse/DXME-188)). If it is
  ever needed the memo key is the device's metadata combination and it belongs
  at the **delivery** layer, never in the publication cache. DXME-177 measured
  it as explicitly not needed.
- **Changing StatusSelector behaviour**
  ([DXME-217](https://radianttechnology.atlassian.net/browse/DXME-217)). The map
  owner's standing requirement. Its `"Uncategorized"` group is not reconciled
  with `UntaggedContent`, its comparison is not normalised, and its grouping
  semantics do not change.
- **An authoring surface for `tblFloorPlanIconSchedule.MetadataKey`**
  ([DXME-217](https://radianttechnology.atlassian.net/browse/DXME-217)). The
  column is read by `FloorPlanService` and nothing in the UI can set it. It is
  counted by the delete guard and otherwise left alone. Deleting it as dead code
  was **declined**: floor plans are live functionality and this reads as
  half-built, so the scope call belongs to whoever owns floor plans.
- **Packaging device metadata**
  ([DXME-220](https://radianttechnology.atlassian.net/browse/DXME-220)). No
  device entity is in the content package manifest at all, and `TblDeviceMetadata`
  carries no `ContentPackageId`. Devices are environment-local hardware, while a
  bundle's contract is location-scoped promotion. The consequence, that a
  promoted targeting-enabled location always arrives half-configured, is made
  visible by the import preview's zero-device check rather than closed.
- **Packaging the metadata registry**
  ([DXME-220](https://radianttechnology.atlassian.net/browse/DXME-220)).
  `TblMetadataDefinition` is an Identity-context, environment-wide table with no
  `ContentPackageId`. Pulling it into a bundle would drag the whole vocabulary
  along with three locations, so a rule naming an unregistered key resolves by
  name and skips instead.
- Content channels and `TblContentChannelMetadata`. Ink field mapping, sharing
  nothing but the word.
- A nested AND/OR expression builder. The evaluator is shaped to allow it later.
- Location types other than playlist, **for targeting**. Scope follows the
  access rule anywhere.
- `Schedule.razor`. Every navigation to it comes from a non-Playlist type or the
  admin tab ([DXME-208](https://radianttechnology.atlassian.net/browse/DXME-208)).
- Moving `videoWallAssignments` off `tblUserGroupMetadata`. That key is
  deliberately CSV and read by `GroupVideoWallAssignments.AppliesTo`; DXME-209
  made the group-side service key-aware instead. Restructuring belongs to the
  Ticket Counter Scheduler.
- **Modal button order**
  ([DXME-210](https://radianttechnology.atlassian.net/browse/DXME-210)),
  inherited from the StyleGuide at build time.
- **A set-valued owning group** on `TblAsset`. Ruled out once Uploading As made
  the owner an explicit pick
  ([DXME-212](https://radianttechnology.atlassian.net/browse/DXME-212)). Returns
  only if the identity picker is abandoned.
- **An ownership backfill** from `TblAssetApproval.UploadedByUserId`
  ([DXME-209](https://radianttechnology.atlassian.net/browse/DXME-209)). It
  cannot backfill the group at all, and with every rule `full` no scoped user
  exists to lose an ability. **Now unconditional**
  ([DXME-218](https://radianttechnology.atlassian.net/browse/DXME-218)): the
  entry used to reopen on production evidence, and no production sweep will ever
  be run, so that condition cannot be met.
- **Bulk device metadata tagging**
  ([DXME-214](https://radianttechnology.atlassian.net/browse/DXME-214)): players
  are provisioned one at a time. Prior art if it returns: `OverrideEditor.razor`
  and `PlayerUpdates.razor`.
- **Bulk asset tag deletion**
  ([DXME-221](https://radianttechnology.atlassian.net/browse/DXME-221)).
  Neither content list has ever had a selection model, so this is a new surface
  rather than a button, weighed against 26 orphaned rows on dev. Clearing is
  answered per asset instead. Prior art if it returns: the silent purge in
  `LocationsTab.SaveCustomMetadataFields` that DXME-209 decision 14 deleted.
- **A registry-side tag purge on definition delete**
  ([DXME-221](https://radianttechnology.atlassian.net/browse/DXME-221)). Cheap
  to build, since `MetadataTab` already counts asset tags, and refused anyway.
  `TblMetadataDefinition` is Identity-context and environment-wide, so a purge
  launched there crosses every location and tenant in one click, and it would
  make definition deletion destructive again, forcing DXME-217's
  warn-and-proceed back to a hard block.
- **An orphaned-tag badge on the content lists**
  ([DXME-221](https://radianttechnology.atlassian.net/browse/DXME-221)). The
  rule builder is the discovery path (DXME-210 decision 9) and the per-asset
  modal is the resolution path. A badge marks the most-used screen in the
  product for a state that is legitimate and usually uninteresting.
- **A general registry validation pattern**
  ([DXME-214](https://radianttechnology.atlassian.net/browse/DXME-214)). Format
  validation is a different feature. Deliberately not ticketed.
- **Changing URL parameter injection**
  ([DXME-214](https://radianttechnology.atlassian.net/browse/DXME-214)). An
  allowlist was designed and rejected: it fails invisibly. Still in scope as
  correctness cleanups: folding the three `AppendMetadataToUrl` implementations
  into one, and moving layout-zone injection to delivery
  ([DXME-216](https://radianttechnology.atlassian.net/browse/DXME-216)), which
  fixes **whose** values are injected rather than which.

- **Reconciling the sidecar and database metadata stores**
  ([DXME-222](https://radianttechnology.atlassian.net/browse/DXME-222)).
  `Playlist.razor` builds its entire content list by parsing `.metadata` XML
  sidecars off disk, while every targeting decision on this map reads
  `tblAssetContentMetadata`. `MediaAssetUploader` writes both, nothing
  reconciles them, and nothing detects drift. It is large, separate, and has no
  metadata content of its own, so ruling it in would swallow the map. DXME-222
  declines to add new drift while leaving the old: its decision 13 moves edit
  mode's per-keystroke sidecar-only write onto the form's Save.

- **`Location.razor`'s silent component dispatch**
  ([DXME-219](https://radianttechnology.atlassian.net/browse/DXME-219)). When
  `ComponentTypeName` names a type no longer in any loaded assembly,
  `ResolveComponent` returns and the location renders an empty page with no log
  line and no build error. DXME-219 removed the one row that would have hit this
  rather than fixing the dispatch: it is a location-type concern with no metadata
  content, and this map's destination is the targeting spec.

`PlaylistLocation_Radiant.razor` and `PlaylistLocation_Omnivex.razor` were listed
here at charting and no longer are:
[DXME-219](https://radianttechnology.atlassian.net/browse/DXME-219) deleted them,
along with location type 5, whose only component Omnivex was.

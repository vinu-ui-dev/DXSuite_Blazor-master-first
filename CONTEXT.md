# Domain glossary

The vocabulary this project uses. When naming a ticket, a test, a class or a
proposal, use the term as defined here rather than a synonym.

This glossary is deliberately partial. It records terms that have been settled
because they were ambiguous or actively misleading, not every noun in the
system. Add to it when a term causes a real misunderstanding.

## Product areas

**Director** — the content management surface: locations, displays, playlists,
schedules, assets, approvals. The bulk of what an operator uses day to day.

**Design** — opportunity and requirements management. A separate surface from
Director with its own access gate.

**Engage** — the player side of the system: the Engage player that runs on
player hardware, in whichever flavour, and the server-side protocol it polls.
Each flavour lives in its own codebase and carries its own version line.

**VideoWall** — rendering content across a wall of physical outputs, including
airline ticket-counter walls, allocations, masks and logo zones.

**Platform** — cross-cutting concerns owned by no single area: tenancy,
identity, roles, navigation, shared infrastructure.

**Aviation** — airline- and flight-specific behaviour: flight feeds, airline
content, dayparting against airline schedules.

**Sentinel** — device monitoring and automation.

**Pulse** — the legacy monitoring system that customers' alerting already runs
on, and its database. DXSuite reports device health into Pulse; it does not
own Pulse's alerts, emails, customers or templates. The `DXSuite.Pulse` module
is a placeholder with no role in that reporting.

**Pulse link** — the explicit association between a Sentinel device and one
Pulse device. A device without a Pulse link reports nothing to Pulse. Device
identifiers are never assumed to match across the two systems.

**Reporting device** — the Sentinel device a Director URL monitor publishes its
issues through. The URL has no Pulse link of its own; it reaches Pulse only
through its reporting device's Pulse link. Distinct from a URL's **linked
device**, which is Director's own loose reference used to name hardware in
Director alerts.

**Issue type** — what an ErrorLevel is configured against: a monitor point at a
severity, a health source going offline while the device is still reachable, or
a Director alert kind. Not a Pulse monitor item, which is where the resulting
reading is filed. A whole device going offline is not an issue type: Pulse sees
it as a stale heartbeat.

**Health source** — one monitoring angle's view of a device: a cloud connector,
the Engage heartbeat, a site gateway probe, or a self-monitoring agent. A device
can carry several at once, each with its own health and last-seen, and the
device's health is a rollup across them. A source going silent is reported per
source, so losing a second source is never hidden by the first.

**Snooze** — Pulse's suppression of down alerts for a device until a set time.
Readings keep flowing while a device is snoozed.

## VideoWall and Pixera

These are **not** synonyms, and the difference matters because the codebase
carries both names on purpose.

**VideoWall** is the vendor-neutral concept: a wall of outputs, its geometry,
allocations, zones and the content composited onto them. This is the canonical
name. New vendor-neutral code uses it.

**Pixera** is a specific third-party media server the walls may run on. A name
carrying "Pixera" is correct only when the thing genuinely talks to that
product.

Some Pixera-branded names in the codebase are vendor-neutral concepts that
predate the distinction and are being renamed. Others are genuinely
Pixera-specific and keep the name permanently. Two things never change
regardless: `/pixera/*` URLs, which player hardware has hand-entered and
Director cannot update remotely, and Pixera-valued strings already stored in
the database.

Read a "Pixera" name as a question, not an answer: does this actually talk to
Pixera?

## Auto Heal and remediation

**Auto Heal** — the operator's per-device switch authorising DXSuite to recover
a device automatically, by whatever means its platform supports. It is a
permission, not a technique: it says nothing about how the recovery happens.
Checked at every actuation rather than latched when a recovery begins, so
turning it off stops the next attempt rather than the one after it.

**Remediation** — Sentinel's internal machinery for carrying out a recovery:
choosing an action, executing it against the device, and logging the attempt.
Gated by Auto Heal and constrained by guardrails.

The distinction matters because Auto Heal once named one specific technique,
TCP power-cycling an LG display, and the name outlived that meaning.

## Engage player flavours

**Engage player** — the player-side application, in any flavour. Use this when
the statement holds regardless of which codebase runs on the hardware.

**Engage applet** — the signageOS flavour of the Engage player. It runs inside
the signageOS Core App on LG webOS, Samsung Tizen and BrightSign, and is
provisioned and updated through signageOS. "Applet" is signageOS's word; it is
wrong for any other flavour.

**Engage for BrightSign** — the flavour that runs natively on BrightSign players
with no signageOS component, provisioned and updated through DXSuite. A
BrightSign player runs one flavour or the other, never both.

**Flavour** — which Engage player a device runs. The same hardware can carry
either, so flavour is a property of the device as enrolled, not of the model.

**Flavour mismatch** — a device whose enrolled flavour differs from the flavour
it declares on its heartbeat. It means the player was reimaged without being
re-enrolled, so it is a device state to surface, not an error to swallow. A
device with no enrolled flavour is not mismatched; it is unenrolled.

**Reflavour** — changing the flavour a device is enrolled as, keeping the same
device. It is a correction, not an adoption path: a player arriving from another
CMS is a takeover, and a player with no history is a fresh provision. Both
flavours identify a BrightSign player by its serial, so a reflavoured device
keeps its row and everything hanging off it.

Prefer "flavour" in anything an operator reads. "Platform" is the schema's word
for the same idea and means other things elsewhere in the codebase.

## Provisioning, enrollment and updates

**Provisioning** — getting an Engage player onto a device and pointing it at
DXSuite. It happens before DXSuite knows the device exists. signageOS owns it
for the applet; DXSuite owns it for Engage for BrightSign.

**Enrollment** — a provisioned device becoming a known device in a tenant and
location. This is the pairing-code flow, which ends in a device token.
Auto-registration on first heartbeat is a legacy path and is not enrollment for
Engage for BrightSign.

**Re-provisioning** — provisioning a device that is already provisioned, in
order to change its configuration. On BrightSign it means booting the player
from a **freshly written card**, never copying a package onto the card it is
already running from: the OS looks for `autorun.brs` before `autorun.zip`, so a
zip dropped beside a running bootloader is ignored silently. Not to be confused
with BSN's own *re-provisioning*, which is BrightSign's cloud restoring its
setup onto a player and is the thing *released* exists to stop.

**Device token** — the credential a device receives when it enrolls, and the
only one it holds afterwards. It authenticates that device's own calls to
DXSuite: content sync, heartbeats and its wake channel. It identifies the
device to DXSuite and is not a credential the device presents anywhere else;
in particular it is never handed to the content the device displays.

**Tenant API key** — a long-lived credential issued to a tenant rather than to
a device, used by services that call DXSuite on their own behalf and by applet
devices configured with one instead of enrolling. Its blast radius is the whole
tenant where a device token's is one device, so the two are not
interchangeable even on the code paths that accept either.

**App bundle** — the HTML and JavaScript half of an Engage player: what the
device runs to show content. It carries the version number a deployment
targets.

**Bootloader** — on Engage for BrightSign only, the program that starts the app
bundle and can fall back to the previous one. It has its own version line, so a
device of this flavour carries two versions that move independently.

**Update** — replacing an enrolled device's app bundle with another version.
Replacing a bootloader is a separate act with its own rules.

**Artifact kind** — which of the two halves a version record describes, an app
bundle or a bootloader. Both kinds file under the same flavour, so a device
carries one version of each and they are targeted separately.

**Artifact store** — where DXSuite keeps the player artifacts it serves, scoped
to the tenant that owns them. Nothing reaches an artifact except through a route
that decides who may have it, which is what distinguishes it from a public
download location.

**Known-good bootloader** — the one bootloader version a tenant's operator has
blessed as the image to rescue a broken player with. It is a standing choice,
deliberately separate from whatever version is currently being rolled out.

**Deployment** — the DXSuite record that targets a version at a scope: all
devices, a location, or one device. A deployment is not the update itself; the
device performs the update on its next poll.

**Provisioning package** — the artifact DXSuite generates for one tenant and
one hardware family, carrying the endpoint URL, network and time configuration,
a bootstrap DWS password, and a pinned firmware file. It never carries an
enrollment secret. It is a persisted record, not a transient download, because
a card in the field has to be reissuable identically.

**Bootstrap password** — the local web-server password a player ships with,
shared by every player made from the same provisioning package. A per-device
password replaces it when the device enrolls, so a device still holding the
bootstrap one has not finished enrolling, which is a state an operator can see
rather than a silent condition.

**Recovery-URL handler** — the DXSuite endpoint a BrightSign player fetches a
BrightScript from, reached through the player's `networking/ru` registry key. It
serves three moments in a device's life: installing Engage, converting a player
during a takeover, and rescuing one whose bootloader will not start.

**Provisioning authorisation** — an operator's standing statement that one named
serial may be converted by the recovery-URL handler, carrying the version that
conversion installs. The verbs are **arm** and **disarm**. It outlives any single
request: the handler keeps answering until the player has actually converted,
because a served response is not proof of a completed install.

**Pairing grant** — the short-lived, single-use, per-serial secret that lets a
player enroll without a human reading its six-digit code. Minted fresh each time
the handler serves a bootstrap, so a copy left on a card is worthless. Distinct
from a provisioning authorisation, which is the operator's decision, where this
is one disposable consequence of it.

**Bootstrap** — the BrightScript the recovery-URL handler returns. It is a
courier rather than the payload: it fetches the real artifact, writes the
storage and reboots. Two variants, an *install bootstrap* and a *recovery
courier*. Distinct from a provisioning package, which is the card artifact; the
package never carries an enrollment secret because the secret rides here.

**Takeover** — converting a player that is already running someone else's CMS
into an Engage player, remotely and with nobody on site. Distinct from
provisioning, which starts from a blank or factory-fresh device. A takeover is
non-destructive by design: the incumbent installation stays on the storage as
the rollback path.

**Reclaim** — deleting what the incumbent installation left behind, as a
separate operation run after a takeover has proven stable. Kept separate
precisely because the thing it deletes is what a failed takeover rolls back to.

**Enrolled with BSN** — a BrightSign player still carrying a live registration
in someone's BSN.cloud organisation, whoever installed it. Say the whole phrase:
*enrollment* on its own already means the pairing flow above, and this is a
different relationship with a different party. A player can be enrolled with BSN
and enrolled with us at the same time, which is exactly the state a takeover
produces.

**Released** — a player removed at the BSN console, ending that organisation's
authority over it. It is the opposite of enrolled with BSN, and it is a
statement about the console's records rather than anything on the player:
nothing done on the device achieves it, and deleting the device's own BSN
registry keys makes it worse rather than better, because the player then asks to
be provisioned again. Released is what a takeover is finished by, and what
reclaim waits for.

These were settled because "provisioning" and "enrollment" were being used
interchangeably, and they have different owners and different failure modes.

## Scheduling

**Schedule** — the set of schedule events attached to a display. A display
without one plays its default content.

**Schedule event** — one row of a schedule: which content, over what date
range, on which days of the week, and within what daily window. All four
conditions must hold for the event to be active.

**Daily window** — a start and end time of day that recurs every day. One rule
governs it everywhere:

- the start is inclusive and the end is exclusive
- a start later than the end wraps midnight, so `21:00 to 03:00` is active from
  21:00 through 03:00 the next morning
- an end at or after 23:59 means through the end of the day, because the
  authoring UI is minute-granular and cannot express 23:59:59
- a missing start means 00:00, a missing end means the end of the day, and a
  window missing both is always open

The wrap rule and the 23:59 rule cannot both apply to the same window: wrapping
requires a start later than the end, which no end at or after 23:59 permits.

A daily window says nothing about which clock it is read against. That is
always the player's local time, and it is the caller's job to supply it.

**Local time** — the player's own local wall time, always. Never the server's,
never the author's browser's. A time an author enters means that wall-clock
time wherever the player is standing, so the same schedule fires at different
instants in different zones. This is the intended behaviour, not a tolerated
imprecision.

**Instant** — a single moment, the same moment everywhere, independent of any
time zone. Every audit and lifecycle stamp is one: when a row was created, last
edited, completed, resolved, last synchronised. An instant is always stored as
UTC.

An instant and a wall-clock bound must never be compared. They are different
kinds of thing, and comparing them is wrong by hours rather than by minutes.
Code that wants to know whether a bound has passed must first resolve that bound
against a particular device's time zone, which produces an instant, and compare
that.

**Wall-clock bound** — a date or time that marks the edge of a schedule, stored
exactly as it was authored and carrying no time zone of its own. A start or end
date, a daily window's start or end time, a message's own from and until. It
names a reading on a wall clock and says nothing about whose wall, so on its own
it does not identify a moment at all. It becomes one only when resolved against
a device's time zone.

Storing a bound without a zone is deliberate rather than an omission: one bound
authored once is meant to fire at a different instant in each time zone it
reaches.

**Display** — a named rendering surface: a size, a shape, a content
assignment. A display is an abstraction over zero or more devices and is not
itself a piece of hardware. It is not a place, and it is not the unit content is
always resolved for: a device that belongs to no display is served from its
location instead.

**Location** — a content role, not a place. `Weather`, `Airline Logos`,
`Center Content`, `Delta Check-In`. A location carries no geography and one
location routinely spans every site in a deployment.

**Device** — a physical player, and the only thing in the model that is
somewhere. A device belongs to at most one display and at most one location, and
may belong to neither. Devices under one display are not guaranteed to be in the
same place.

**Orientation** — a property of a **device**: how the player rotates its output
to match how its panel is mounted. It is one of Landscape, Portrait, Portrait
(flipped) and Landscape (flipped), and is Landscape unless an operator says
otherwise. Portrait means the panel is turned clockwise, viewed from the front.

It belongs to the device and not the display, because it describes how one
piece of hardware is hung. DXSuite tells the player its orientation on every
poll, and the player applies it live. Only Engage for BrightSign acts on it;
players on LG and Samsung take their shape from the panel itself.

**Video path** — a property of a **device**: how an Engage for BrightSign player
draws video. It is Hardware, on the player's hardware video plane, unless an
operator turns on Page, drawing video in the page. Page cuts from one clip to
the next with no dark frame, which the hardware plane cannot; it is Full HD only
and needs a player that decodes two clips at once. Series 4 players are always
sent Hardware whatever is stored, because they ignore page-drawn video and would
show nothing. Shown to operators as "Seamless transitions". Like Orientation,
DXSuite sends it on every poll and the player applies it from the next clip.

**Time zone** — always two words, and never shortened to "zone", which in
DXSuite means a rectangular region of a video wall layout. A time zone is a
property of a **device**, because a device is the only thing that is anywhere.
It is stored as an IANA identifier (`America/Chicago`, `America/Phoenix`) and is
never absent: a device is given one when its row is created and a person corrects
it when the player is commissioned.

Every schedule window is resolved against the requesting device's time zone,
by the server, before content reaches a player. So one schedule authored once
fires as a rolling wave across a deployment that spans several time zones, and
a player's own clock does not decide what it plays.

Which clock is used follows the **authentication scheme** of the request. A
caller holding a device token is a device, and its stored time zone decides. A
caller holding an API key is an external integration with no device, and the
server's clock decides. One endpoint accepts both schemes and branches on it.

**Time zone source** — where a device's time zone came from, kept beside the
time zone itself and never absent. A time zone is **seeded** when the system
wrote it at row creation because a device had just appeared and nobody had said
where it was; it is **set by an operator** when a person chose it; and one day it
will be **discovered**, when a device reports its own.

Nothing that decides what plays ever reads it. Every schedule window is resolved
against the device's time zone whatever its source, because a seeded time zone is
still better than a single server clock. The source governs one thing: **DXSuite
tells a device its time zone only when an operator set it.** A seeded value is a
guess, and asserting a guess onto a device is worse than leaving the device where
it already is.

**OS time zone** — the setting a player's own operating system keeps for its
local clock, and a different thing from the device's time zone. The device's time
zone is what the server resolves schedules against; the OS time zone is what the
player itself reads when something on the device needs to know the local hour.

Nothing about what plays depends on it, because the server resolves every window
before content reaches a player. It matters for the few things a player does on
its own wall clock, such as rebooting nightly at a civilised hour.

DXSuite tells a player its OS time zone only when an operator set the device's
time zone, and says nothing at all otherwise. Saying nothing means the player
keeps whatever it already had, which is the safe answer when the only value on
offer is a guess.

The instruction is carried in whatever form the player's own platform accepts,
which is not the IANA identifier the model stores, so a curated list holds an
authored translation for each time zone an operator can choose. A time zone
missing from that list carries no translation and nothing is sent, which leaves
the player where it is rather than sending it somewhere invented.

**Unresolvable time zone** — a stored identifier that the runtime cannot turn
into a time zone, because it was mistyped, because it names a zone this
platform does not carry, or because it is the placeholder written when nobody
has yet said where a device is.

It is not an absent time zone. A device always has a value; this one simply does
not resolve. The system neither guesses a substitute nor refuses to answer:
it records the failure and serves the device its content **unfiltered**, so
everything plays, including content that is outside its window. That is
deliberate. Choosing another clock would produce wrong content that looks right,
which is the failure the model exists to prevent, while serving nothing would
turn a data-entry mistake into a dark screen at a customer site.

**Site** — a physical place that devices are at: a service center, a building, a
campus. It carries a name and a postal address and nothing else. A site is the
answer to "where is this player", which no other entity in the model can give,
because a Location is a content role and a Display is a rendering surface.

A site carries **no time zone**. The time zone stays on the device, so that a
device can one day report its own. Nothing derives a time zone from a site's
address either, because that would resolve a time zone at evaluation time and
because a state is not a time zone: Arizona and Colorado share a mountain offset
for part of the year and not the rest.

Sites are optional. A deployment whose players are all in one building, such as
an airport, leaves them unset.

**Request-scoped evaluation** — deciding what should be playing at the moment a
device asks for it. A device is in scope, so its time zone is known and exactly
one answer is correct. Every schedule window is resolved this way.

**Tick-scoped evaluation** — a background service waking on a timer and deciding
for a whole set of things at once, with no device in scope. It has no single
correct local time to read, so it does not evaluate a daily window at all; that
decision belongs to request-scoped evaluation. A tick service may still do work
that cannot be deferred, such as pre-encoding content or driving an external
system.

**Operator preview** — a surface in the admin UI that shows what a display would
be playing. It reads the server's clock, not the display's, and it shares no code
path with any player, so a change to one is never a change to the other. Never
player-facing: a player only ever receives content over the HTTP APIs.

**Published content** — the set of content a location publishes, with no clock
and no time zone involved. It changes when media, sidecar metadata or approvals
change, and it is the same for every device at that location. **A publication is
a function of content, never of configuration**, which is what makes the list of
things that invalidate one derivable rather than remembered: anything that varies
by the asking device, including content targeting and the clock, belongs to
delivery instead. Access rules are not an input, because they govern what a user
sees rather than what a device receives.

**Delivered content** — what one device should be showing right now: published
content with content targeting applied for that device, and the day-of-week and
daily-window conditions applied against that device's time zone. The two are
independent conditions that must both hold, and a time zone that cannot be
resolved lifts only the second. Two devices at one location in different time zones receive
different delivered content from identical published content, and the same
device's delivered content changes at every window boundary without anything
being published.

**Publication version** — an identity for a location's published content, used
to tell whether what a location publishes has changed. It is the version an
audit trail of content changes records. A window opening or closing is not a
publication and never changes it.

**Delivery version** — an identity for one device's delivered content, used to
tell a player whether what it should be showing has changed. It moves at every
window and day boundary as well as on publication, and two devices in different
time zones can hold different delivery versions at the same moment.

**Daylight saving transitions** — converting a local time to an instant is the
only direction that can fail, and two cases do. A nonexistent local time, on the
day a zone springs forward, resolves to the first instant after the gap. An
ambiguous local time, on the day a zone falls back, resolves to the first of its
two occurrences. Arizona observes no daylight saving, so it tracks Mountain in
winter and Central in summer and no fixed offset is correct for it year-round.

Nothing in DXSuite converts a local time to an instant today. Resolving a daily
window only converts an instant to a local time, which is total and cannot fail.
The rule is recorded because the first question that needs it, "when does this
window next open", will not be answered correctly by guessing.

**Site** — where a device physically is: a service center, a terminal, a branch.
A site is optional, is assigned one device at a time, and carries no time zone of
its own. A time zone is a property of the device even when the device sits at a
site, and nothing derives one from an address.

**Time zone provenance** — how a device's time zone came to be what it is, and
the only thing that decides whether it is sent to the player. `Seeded` is the
value row creation wrote from the Director host's own zone, which nobody looked
at. `Operator` is any value a human chose, including the upgrade backfill.
`Discovered` is reserved for a device reporting its own and is not shipped. No
schedule evaluator reads provenance: a window resolves against the stored zone
whatever its provenance.

**Time zone sentinel** — the literal `UNSET`, written where a real identifier
cannot be produced: by the migration for existing rows, and by the seed helper on
a host whose zone has no IANA equivalent. It is deliberately a value that does
not resolve, so the device plays unfiltered and is visibly wrong rather than
being given a plausible zone nobody chose.

**Time zone verification** — comparing a player's reported UTC offsets at two
fixed instants against the zone it was told to run, rather than comparing zone
names, which a player under a POSIX literal cannot report. Two instants and not
one, because January alone cannot separate Arizona from Mountain: both sit at -7
in winter. A device is `Verified`, `Pending` when the player has not applied the
zone yet, `Mismatch` when it has and the rules disagree, and not reported when it
sends no fingerprint at all.

## Content metadata

Metadata keys and values live in a single registry, `tblMetadataDefinition`, and
are attached to assets, devices, users and user groups. Two separate mechanisms
read them, and conflating the two is the misunderstanding this section exists to
prevent.

**Content targeting** — deciding which assets reach which devices, by comparing
an asset's metadata values against a device's. Playlist locations only, plus the
playlist locations that a Layout location draws zones from, where the source
location is the subject.

**Content scope** — deciding which assets a user can see, by comparing an asset's
metadata values against the user's. It follows the access rule wherever one
exists, which includes the Asset Library. Targeting is asset-side against
device-side; scope is user-side against asset-side.

**Content ownership** — deciding which assets a user may change. A separate
question from scope: matching a scope does not grant control. Group-owned content
is controllable by any current member of the owning group, so ownership pins the
group and the roster is read live.

**Playlist location** — a location of the Playlist type. Its players play the
location's playlist and nothing else. It has no displays, so no schedule, status
selector or asset library ever reaches it. The only kind of location content
targeting governs directly. Avoid calling any location that shows a sequence of
content a playlist location: Concourse Entry Playlist is not one.

**Player** — the targeting subject. Targeting evaluates device metadata, which
hangs off a device, so the subject is a player and never a Display. `TblDisplay`
is a different table carrying its own separate metadata key. Use "Player" in UI
copy, in tests and in tickets.

**Participating key** — a metadata key named in a location's metadata rules that
also exists in the registry. Participation is row presence; there is no flag. The
registry is the vocabulary and the rule table is the participation, and neither is
a partition: a key can be targeted at one location and injected as a URL parameter
at another.

**Verdict** — what one participating key returns for one asset: Match, NoMatch or
Abstain. Not a boolean, because "this key does not match" and "this key has
nothing to say" are different claims. Abstain is what lets untagged content,
unconfigured keys and unpopulated subjects be expressed without any of them
pretending to be a match or a mismatch.

**Shared** — content carrying no value for a participating key, and therefore
reaching every player at the location. The user-facing name for untagged content.

**Orphaned tag** — an asset metadata row whose key the location does not declare,
or declares but the registry no longer holds.
A legitimate, supported, visible state, not corruption. Re-adding the key restores
its targeting exactly.

**Uploading as** — the uploader's identity control. It names an identity, one per
matching scoped access rule, never a role and never a persona. The picked identity
supplies the tag values and becomes the owner. Viewing unions and uploading picks:
scope reads a user's own values unioned with their groups', while tagging comes from
one picked identity with no union.

**Reach** — how many of a location's players would receive a given asset.

**Location bundle** — a web page, uploaded once and hosted on any number of
locations, that signed-in users see on a location's page in place of its built-in
screen. It acts through the same content, schedule and upload rules as the user
viewing it, and never plays on a device, so it is a surface for content scope and
never for content targeting. Not an app bundle, and not a content package, even
though the content package code also says "bundle".

**Preview** — always the content preview. The Layout page's zone geometry view is
a **zone layout map** and is not a preview of anything.

**Access levels** — shown to users as Full, Scoped and View only, while the
database keeps `full`, `partial` and `view`. Do not surface the database words.

## Systems

The tracker spans more than this repository.

**DXSuite** — this repository. Director, Design, VideoWall, Platform, and the
server half of Engage.

**Engage applet** — the signageOS flavour of the Engage player, a separate
codebase with its own version line. Work on it cannot be picked up from this
repository.

**Engage for BrightSign** — the native BrightSign flavour of the Engage player,
a separate codebase with its own version line. Work on it cannot be picked up
from this repository either.

**CoreAPI** — the on-prem monitoring and integration API, including flight
feed routing.

**DSC** — the MSP reporting tool.

# Logo Layer Feature — Design Discussion

## Problem

Airlines need a way to display their logo on the video wall mask layer for their allocated ticket counter section. Not every airline has an HTML development team, so a simple "upload a logo" experience is desirable.

The specific layout for this wall has **1073px-wide sections** behind the ticket counter workstations (with baggage scales on either side). The airline logo should appear in this center section, automatically driven by the counter allocation — no extra operator steps.

## Ideas Evaluated

### Idea 1 — Treat logo zones as schedulable counter displays
Schedule logos on dedicated counter displays the same way flight status is scheduled.

**Verdict:** Functionally sound, zero new development. But creates operational coupling — operator must update the logo schedule in sync with the counter allocation. Fragile. Good fallback if Idea 3 proves too complex.

### Idea 2 — Pre-built HTML template, upload a new instance per airline
Build an HTML template with a fixed logo position; save new copies per airline.

**Verdict:** Discarded. Creates a proliferating set of near-identical files, no connection to the allocation system, and updating the template means touching every airline's file.

### Idea 3 — "Logo Layer" group type, auto-driven by counter allocation (preferred)
A new group role type in the panorama layout. Each display in the logo group is a declared position (X, width, height) on the canvas. The allocation service already knows which airline owns which X range; it looks up the airline's configured logo asset and includes it in the rendered output automatically. No additional operator action after initial counter allocation.

**Verdict:** Architecturally correct. Natural extension of the existing DisplayCluster / AllocationResult pattern. Logo visibility derives from counter allocation — single source of truth.

## Open Questions (need answers before implementation)

1. **One logo zone per cluster, or multiple?**
   e.g. If Spirit gets 4 counters across two groups of 2 workstations, are there two logo positions or one?

2. **Are logo zone positions fixed/known up front, or must they be configurable per-wall in the layout editor?**
   The "somewhat irregular" spacing suggests they may not be derivable automatically from counter display positions.

3. **Which Pixera layer does the logo render into?**
   Mask layer (on top of everything), or a dedicated layer composited between background video and mask overlay?

4. **Is the logo asset purely an image, or might airlines want text (name, "check in here", etc.)?**
   Determines whether a small HTML wrapper is needed or just an `<img>` tag.

## Proposed Implementation Sketch (pending answers above)

- Add `"logo"` as a new `Role` value on `PanoramaGroup` in `PanoramaConfiguration`
- Each display in the logo group = one logo slot, positioned explicitly in the layout editor
- Constraint on that display defines expected asset size (e.g. 1073 × 648px)
- Allocation service checks each logo group display's X position against active counter clusters; if it falls within a cluster owned by an airline, that airline's logo asset is included in the `DisplayCluster` payload
- Add `LogoPath` / `LogoAssetId` to `TblUserGroupLocationDefault` (alongside existing `MaskPath`, `BackgroundPath`)
- Counter layer shell HTML (or a new logo layer URL) renders the logo image at the correct absolute X position
- Airline content upload UI surfaces a logo upload field alongside the existing mask/background uploaders

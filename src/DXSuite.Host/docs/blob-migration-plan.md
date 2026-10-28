# Blob Storage Migration Plan

Status: audit complete, execution deferred. The IMediaStore abstraction and AzureBlobMediaStore
are built and wired (AzureBlob is selected when Storage:BlobEndpoint is configured, LocalDisk
otherwise). Adopting them across the media code is the work described here.

## Summary

Media storage is moving from local disk (wwwroot/MediaAssets) to Azure Blob: one storage account
per environment, one container per tenant (container name equals tenant id). The plumbing is done.
The remaining work is routing the application's media file I/O through IMediaStore, which today it
bypasses entirely.

## Current state

IMediaStore has zero consumers. It is defined, implemented twice (LocalDisk and AzureBlob),
registered, and documented as swappable, but no component or service actually calls it. Every media
read and write goes directly to the filesystem. An audit of 28-plus files confirmed this.

## The two findings that shape the work

### 1. Serving is a static-file URL model (the keystone)

SignageController does not stream bytes. It returns a JSON manifest of URLs like
`{SiteUrl}/MediaAssets/{locationId}/{file}`, and ASP.NET static-file middleware serves the bytes
from wwwroot/MediaAssets. The database stores AssetPath as that `/MediaAssets/...` URL. Moving bytes
to blob breaks serving for the whole app and every player, so the serve path must change first.
Nothing else works end to end until it does.

### 2. The .metadata sidecar is the central data problem

A per-asset `{file}.metadata` XML file is the source of truth for PlayOrder, schedule, location,
custom fields, approval status, and Ink IDs. It is read and written by at least seven components and
discovered by Directory.GetFiles plus a FileSystemWatcher. IMediaStore is get/put/list/delete today;
it has no sidecar concept, and blob has no FileSystemWatcher.

## Scope

In scope, route through IMediaStore (tenant media):

| File | Difficulty | Why |
|---|---|---|
| ConcourseMediaAssetUploader, TvSourceUploader, TicketCounterScheduler, OverrideEditor, CanvasImageGenerationService | Low | Single byte or stream write, swap to SaveAsync |
| LocationsTab (location thumbnails) | Low-Med | Write plus delete plus old-file cleanup, separate images namespace |
| ImageUploader, CreateLayoutModal, WelcomeMessages, ContentPackageBundleBuilder | Medium | Contract change, multiple write sites, or read-for-zip |
| MediaAssetUploader | High | Central upload; temp plus final plus mask plus thumbnail plus sidecar; ffmpeg and System.Drawing need local paths |
| Playlist | High | The anchor: directory enumeration, sidecar read/modify/write, thumbnails, lock-retry delete |
| MediaCreator, TemplateCreator | High | DiskPath contract, sidecars, recursive folder scans for state |
| VideoThumbnailService | High | ffmpeg in and out, needs download-to-temp shim |
| ContentPackageBundleImporter | High | Writes and deletes media plus sidecars from a zip |
| SignageController | High | The serve keystone: URL build plus .json and .metadata reads |
| MediaFolderCacheService | High | Directory.GetFiles plus FileSystemWatcher, no blob equivalent |
| InkContentSyncService | High | Reads media bytes for cloud upload, reads and writes sidecars |

Out of scope, leave on local disk (not tenant media): ScreenshotService (device screenshots),
BrandingStorage (global chrome), ContentPackageController and ContentPackageTab (ephemeral export
zips), UpdateController and PlayerUpdates (player update binaries), BatchDeploymentService (WebOS
IPK), CameraStreamController (live transcode), SessionState (no file I/O).

## Path conventions to preserve

These are persisted in the database and must be preserved or migrated when the blob key scheme is
chosen: `MediaAssets/{loc}[/{constraint}]`, `/OverrideContent/...`, `MediaAssets/AirlineContent/...`,
`images/Location Images/...`, `images/Visual Backgrounds/` and `models/`, and the `Thumbnails/`
subfolder.

## Design decisions

### Serving model: stream-through-app first, CDN later
A controller maps `/MediaAssets/{**path}` (and the other prefixes) to IMediaStore.OpenReadAsync, with
tenant resolution and access control. This keeps the existing URLs, so the database, manifests, and
UI img tags are unchanged, and blob stays private behind managed identity. For production scale,
layer a CDN or SAS URLs later to take byte egress off the app.

### Metadata sidecars: store as sibling blobs
Store each `.metadata` as a sibling blob next to its asset rather than moving the metadata into the
database now. This keeps the existing read/modify/write logic almost intact (swap File for
IMediaStore) and avoids a second large refactor. Folder enumeration moves to IMediaStore.ListAsync,
and FileSystemWatcher invalidation becomes invalidate-on-write plus the existing TTL.

### Blob key scheme: preserve DB paths
The blob name should map cleanly from the stored AssetPath so existing rows keep working, or a
one-time backfill rewrites stored paths. Decide this in Phase 0.

## Phased plan

- Phase 0, design: lock the two decisions above, define the blob key scheme, add any small
  IMediaStore capability needed.
- Phase 1, serve: build the streaming media endpoint, keeping static-file serving as a fallback for
  files still on disk during the transition. This unblocks everything.
- Phase 2, core write and listing: route MediaAssetUploader's final save and sidecar through
  IMediaStore, and move MediaFolderCacheService and Playlist enumeration to ListAsync. After this,
  upload, list, and serve work end to end for the main flow. This is the smallest working slice.
- Phase 3, local-path shims: download-to-temp for VideoThumbnailService and the ffmpeg, re-encode,
  and System.Drawing paths in MediaAssetUploader.
- Phase 4, remaining writers: the low and medium uploaders, the content-package importer and builder,
  and InkContentSyncService.
- Phase 5, data migration: azcopy existing media up, backfill, then turn off static serving.

## Effort

Multi-week, not days. Phases 0 through 2 are the spine. The Key Vault work is done, the blob
plumbing is correct, and this adoption is the real remaining work.

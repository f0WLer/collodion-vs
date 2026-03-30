# Collodion Architecture

This document is a map of how the mod is organized and how core gameplay systems interact.

If you are making a change and do not know where to start, begin with the entry points in section 2.

## 1) What this mod does

Collodion adds a wet-plate photography workflow to Vintage Story:

1. Prepare and sensitize plates.
2. Capture an exposure with the camera/viewfinder.
3. Develop and fix in the tray.
4. Display photos as items and mounted framed blocks.
5. Sync photo image files between client and server when missing.

## 2) Top-level runtime entry points

- `src/ModSystem/CollodionModSystem.cs`
  - Registers items, blocks, block entities, and network packet types for channel `collodion`.
- `src/ModSystem/CollodionModSystem.Client.cs`
  - Client startup: input hooks, capture renderer setup, viewfinder/latch ticks, client packet handlers.
- `src/ModSystem/CollodionModSystem.Server.cs`
  - Server startup: authoritative packet handlers, photo-seen index, server-side config authority.

Most cross-cutting behavior eventually routes through these files.

## 3) Folder ownership (current structure)

- `src/Blocks/`
  - Block interaction rules and world-side behavior.
- `src/BlockEntities/`
  - Runtime state ownership and serialization for placed blocks.
- `src/Items/`
  - Held-item behavior and interaction flow.
- `src/Items/Plates/`
  - Plate state helpers and plate rendering utilities.
- `src/ModSystem/`
  - Mod lifecycle partials, command/viewfinder partials, config POCOs.
- `src/Network/`
  - Packet DTOs only.
- `src/Photograph/`
  - Photograph metadata models and constants.
- `src/PhotoSync/`
  - Chunked transfer and on-disk synchronization of photo PNGs.
- `src/Rendering/`
  - Rendering helpers and capture renderer.
- `src/Effects/`
  - Wet-plate visual effect pipeline.

## 4) Core gameplay flows

### A. Camera capture flow

1. Camera usage starts from `ItemWetplateCamera` / `ItemWetplateCamera.Client`.
2. Viewfinder and capture timing is managed client-side via mod-system viewfinder logic.
3. Capture metadata/packets are handled via the `collodion` channel.
4. Resulting photo IDs are stored on relevant plate/photo entities.

Primary files:
- `src/Items/ItemWetplateCamera.cs`
- `src/Items/ItemWetplateCamera.Client.cs`
- `src/ModSystem/CollodionModSystem.Viewfinder.cs`
- `src/ModSystem/CollodionModSystem.Client.cs`

### B. Plate processing flow

1. Plate state and wet timer helpers live in `WetPlateAttrs`.
2. Tray interactions gate timed actions (developer/fixer/water).
3. Tray block entity stores current plate stack and facing/state for rendering.

Primary files:
- `src/Items/Plates/WetPlateAttrs.cs`
- `src/Blocks/BlockDevelopmentTray.cs`
- `src/Blocks/BlockDevelopmentTray.Server.cs`
- `src/BlockEntities/BlockEntityDevelopmentTray.cs`
- `src/BlockEntities/BlockEntityDevelopmentTray.Client.cs`
- `src/BlockEntities/BlockEntityDevelopmentTray.ClientMesh.cs`

### C. Photograph display flow

1. Photograph block entities persist photo IDs, captions, and frame metadata.
2. Client mesh builders create frame/photo meshes.
3. Missing local image files trigger sync requests and dirty/rebuild once data arrives.

Primary files:
- `src/BlockEntities/BlockEntityPhotograph.cs`
- `src/BlockEntities/BlockEntityPhotograph.Client.cs`
- `src/BlockEntities/BlockEntityPhotograph.ClientMesh.cs`
- `src/BlockEntities/BlockEntityPhotograph.MeshGen.cs`
- `src/Photograph/PhotographAttrs.cs`

### D. Photo file sync flow

1. Photo blobs are chunked using shared helpers.
2. Client uploads newly created photos and requests missing ones.
3. Server reassembles uploads, validates PNG signature, stores to disk, and serves requests.

Primary files:
- `src/PhotoSync/WetplatePhotoSync.cs`
- `src/PhotoSync/WetplatePhotoSync.Client.cs`
- `src/PhotoSync/WetplatePhotoSync.Server.cs`
- `src/Network/PhotoBlobRequestPacket.cs`
- `src/Network/PhotoBlobChunkPacket.cs`
- `src/Network/PhotoBlobAckPacket.cs`

## 5) Network protocol overview

Channel name: `collodion`

Packet families:
- Camera/sling actions:
  - `CameraLoadPlatePacket`
  - `CameraSlingTogglePacket`
  - `CameraAttachSlingPacket`
- Photo metadata/state:
  - `PhotoTakenPacket`
  - `PhotoCaptionSetPacket`
  - `PhotoSeenPacket`
- Photo binary sync:
  - `PhotoBlobRequestPacket`
  - `PhotoBlobChunkPacket`
  - `PhotoBlobAckPacket`
- Capture config sync:
  - `PhotoCaptureConfigRequestPacket`
  - `PhotoCaptureConfigPacket`

Handler wiring:
- Client handlers are bound in `CollodionModSystem.Client.cs`.
- Server handlers are bound in `CollodionModSystem.Server.cs`.

## 6) Config and authority boundaries

Primary config root:
- `src/ModSystem/CollodionConfig.cs`

Key defaults live in:
- `src/ModSystem/PlateProcessingConfig.cs`
- `src/ModSystem/DevelopmentTrayInteractionConfig.cs`
- `src/ModSystem/ViewfinderConfig.cs`

Lookup helper:
- `src/ModSystem/CollodionConfigAccess.cs`

Authority rules:
- Server is authoritative for gameplay-affecting state in multiplayer.
- Client should own presentation/input behavior unless explicitly synchronized.

## 7) Important persisted keys

Wet plate keys (`WetPlateAttrs`):
- `collodionWetCreatedTotalHours`
- `collodionWetDurationHours`
- `collodionStoredRemainingWetHours`
- `photoId`
- `collodionPlateStage`
- `collodionDevelopPours`

Photograph BE keys (`PhotographAttrs`):
- `photoId`, `photoId2`
- `caption`, `caption2`
- `framePlank`, `framePlank2`

Treat key renames as save-data migrations, not normal refactors.

## 8) Change impact checklist

Before merging a behavior change, confirm:

1. Registration still includes your changed type/packet in `CollodionModSystem.cs`.
2. Packet handler wiring exists on both sides when protocol changed.
3. Save keys remain backward compatible.
4. Rendering changes trigger both mesh invalidation and block dirtying where required.
5. Multiplayer logic keeps server authority and bounded retries/timeouts.

## 9) Related docs

- `docs/GUIDELINES.md` for architectural rules and boundaries.
- `docs/CONFIGURATION.md` for user-facing config behavior.
- `docs/AGENT_CONTEXT.md` for compact agent-oriented implementation map.

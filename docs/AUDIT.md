# Collodion — Guidelines Compliance Audit

Each finding references the relevant GUIDELINES.md section(s).
Findings are ordered by remediation impact (highest first).

---

## Critical — Structural Misplacement

### A1 · `Commands/` and `Viewfinder/` folders are partials of `CollodionModSystem` (§1, §3)

All 8 files in these two folders are `public partial class CollodionModSystem`, not standalone classes.
The folder they live in is wrong and their file names don't follow the `ClassName.Concern.cs` partial convention.

| Current file | Target location | Suggested name |
|---|---|---|
| `Commands/Handler.cs` | `ModSystem/` | `CollodionModSystem.Commands.cs` |
| `Commands/Effects.cs` | `ModSystem/` | `CollodionModSystem.CommandEffects.cs` |
| `Commands/Misc.cs` | `ModSystem/` | `CollodionModSystem.CommandMisc.cs` |
| `Commands/Pose.cs` | `ModSystem/` | `CollodionModSystem.CommandPose.cs` |
| `Viewfinder/Mouse.cs` | `ModSystem/` | `CollodionModSystem.ViewfinderMouse.cs` |
| `Viewfinder/RuntimeFov.cs` | `ModSystem/` | `CollodionModSystem.ViewfinderFov.cs` |
| `Viewfinder/Viewfinder.cs` | `ModSystem/` | `CollodionModSystem.Viewfinder.cs` |
| `Viewfinder/ZoomRenderer.cs` | `ModSystem/` | `CollodionModSystem.ZoomRenderer.cs` |

Once moved, `Commands/` and `Viewfinder/` folders can be deleted.

---

### A2 · Packet classes scattered across three folders (§1, §2)

All `[ProtoContract]` packet types must live in `Network/`. Currently they live in four different places.

| Current location | Classes | Target |
|---|---|---|
| `ModSystem/CollodionModSystem.cs` lines 11–62 | `PhotoTakenPacket`, `CameraLoadPlatePacket`, `CameraAttachSlingPacket`, `CameraSlingTogglePacket`, `PhotoCaptureConfigRequestPacket`, `PhotoCaptureConfigPacket` | `Network/` (one file per class) |
| `Wetplate/PhotoSync/PhotoSync.Packets.cs` | `PhotoBlobRequestPacket`, `PhotoBlobChunkPacket`, `PhotoBlobAckPacket` | `Network/` (one file per class, or group into `Network/PhotoSync.Packets.cs`) |
| `Photograph/Caption/Caption.Packets.cs` | `PhotoCaptionSetPacket` | `Network/PhotoCaptionSetPacket.cs` |
| `Photograph/Seen/Seen.Packets.cs` | `PhotoSeenPacket` | `Network/PhotoSeenPacket.cs` |

---

### A3 · `Blocks/Types/` and `Blocks/Entities/` subfolder split (§1)

| Current | Target |
|---|---|
| `Blocks/Types/*.cs` | `Blocks/*.cs` (flat) |
| `Blocks/Entities/*.cs` | `BlockEntities/*.cs` (top-level folder) |

---

### A4 · `Wetplate/` folder name (§1)

Rename `Wetplate/` → `Chemistry/`.
Sub-folders `Effects/` and `PhotoSync/` stay as-is inside it.

---

## High — Multiple Types Per File

### B1 · `Items/Plates/WetPlates.cs` — 4 types in one file (§2)

```
WetPlateAttrs         (static utility class)
ItemSilveredPlate     (: ItemPlateBase)
ItemExposedPlate      (: ItemPlateBase)
ItemDevelopedPlate    (: ItemPlateBase)
```

Split into four files. `WetPlateAttrs` can stay in `Items/Plates/`; the three `Item*` classes go to `Items/Plates/` as well.

### B2 · `Items/Plates/ItemPlateBase.cs` — 2 types (§2)

`ItemGenericPlate` (line 95) is defined inside `ItemPlateBase.cs`. Extract to `Items/Plates/ItemGenericPlate.cs`.

### B3 · `ModSystem/CollodionConfig.cs` — 7 POCOs (§2)

```
CollodionConfig, PhotographConfig, PlateProcessingConfig, PhotoSyncConfig,
PhotoCapturePipelineConfig, DevelopmentTrayInteractionConfig,
TimedInteractionConfig, ViewfinderConfig
```

Option A (strict): one file per class in `ModSystem/`.
Option B (pragmatic): group tightly related POCOs together — e.g., `DevelopmentTrayInteractionConfig` + `TimedInteractionConfig` into one file since one composes the other — but `CollodionConfig` itself goes in its own file.

### B4 · `Photograph/PhotoLastSeenIndex.cs` — 2 types (§2)

`PhotoLastSeenEntry` (line 55) is defined after `PhotoLastSeenIndex`. Extract to `Photograph/PhotoLastSeenEntry.cs` or keep co-located since it's a private implementation detail — but either way the file name is misleading when it contains two public types.

---

## High — Inheritance Depth

### C1 · `Item → ItemPhotograph → ItemFramedPhotograph` — 3 levels (§10)

`ItemFramedPhotograph : ItemPhotograph : Item` exceeds the two-level maximum.

`ItemPhotograph` provides: `OnHeldInteractStart` override (legacy no-op), plus client partial rendering logic.
`ItemFramedPhotograph` extends that with placement, crafting hook, and tooltip.

Resolution options ranked:
1. **Flatten**: If `ItemPhotograph` contains only a stub override and client rendering, merge the stub into `ItemFramedPhotograph` and pull the client partial code into `ItemFramedPhotograph.Client.cs`.
2. **Justify and document**: If `ItemPhotograph` is genuinely a reusable shared base (multiple photograph item types), it is justified — document the reason explicitly in a code comment so reviewers know it's intentional.

---

## Medium — Dependency Inversion

### D1 · Concrete `CollodionModSystem` stored as field in `Block*` classes (§9)

| File | Line | Issue |
|---|---|---|
| `Blocks/Types/BlockDevelopmentTray.cs` | 31 | `private CollodionModSystem? modSys` field |
| `Blocks/Types/BlockGlassPlate.cs` | 16 | `private CollodionModSystem? modSys` field |
| `Blocks/Entities/BlockEntityPlateBox.Client.cs` | 114 | `readonly CollodionModSystem? modSys` field |

These classes hold a permanent reference to the concrete mod system to access config values and channels. The fix is to extract the config values they actually need (a float, an int) and pass them at `OnLoaded`/registration time, or expose a narrow interface.

### D2 · Transient `CollodionModSystem` lookups inside Block/BlockEntity methods (§9)

| File | Line |
|---|---|
| `Blocks/Types/BlockPlateBox.cs` | 57 |
| `BlockEntities/BlockEntityDevelopmentTray.Client.cs` | 671, 684 |
| `BlockEntities/BlockEntityPhotograph.ClientMesh.cs` | 218 |
| `BlockEntities/BlockEntityPhotograph.cs` | 111 |

Transient lookups are lower risk than stored fields, but still couple these classes to the concrete mod system type. Each should be passed the specific thing it needs rather than resolving the whole mod system.

### D3 · `Wetplate/Effects/Effects.cs` resolves `CollodionModSystem` for config access (§9)

Line 12: `CollodionModSystem.ClientInstance ?? capi.ModLoader.GetModSystem<CollodionModSystem>()`.
The config needed should be passed in by the caller, not fetched internally.

---

## Medium — Method Length

### E1 · `BlockDevelopmentTray.OnBlockInteractStart` — ~195 lines (§5)

Spans lines 311–505 approximately. Handles all interaction cases (plate insert, developer pour, fixer pour, water rinse) and both client and server sides in a single method body.

Should be split into:
- `OnBlockInteractStart_Client` (or a private `HandleInteractClient`)
- `OnBlockInteractStart_Server`
- Per-case server handlers already partially exist (`TryApplyDeveloperPourServer`, etc.) but are not used at the start level

### E2 · Large files warrant method-level audit during refactor

`PhotoPlateRenderUtil.cs` (1169 lines), `BlockEntityDevelopmentTray.Client.cs` (943 lines), `CollodionModSystem.Server.cs` (794 lines), `Viewfinder.cs` (694 lines) are all candidates for method-length violations that should be reviewed during the refactor pass.

---

## Low — Naming

### F1 · `Wetplate/Effects/Config.cs` — class name `WetplateEffectsConfig`, file name `Config.cs` (§2)

File name does not match class name. Rename to `WetplateEffectsConfig.cs`.

### F2 · `Photograph/Caption/CaptionDialog.cs` — class is `GuiDialogPhotographCaption` (§2)

File name `CaptionDialog.cs` does not match class name `GuiDialogPhotographCaption`. Rename to `GuiDialogPhotographCaption.cs`.

---

## No Violations Found

- `#if CLIENT` — none present anywhere in `src/` ✓
- `Wetplate/Effects/` partials — all correctly named `ClassName.Concern.cs` for `WetplateEffects` partials ✓
- `BlockEntityDevelopmentTray`, `BlockEntityPhotograph`, `BlockEntityPlateBox` serialization — all use `ToTreeAttributes`/`FromTreeAttributes` ✓
- Thread safety — lock objects present and paired with dictionaries ✓
- Nullable reference types — enabled, `?.`/`??` used consistently ✓
- Network channel registration — uses `IClientNetworkChannel`/`IServerNetworkChannel` ✓

---

## Remediation Roadmap

| Phase | Scope | Risk | Guideline |
|---|---|---|---|
| 1 | Move `Commands/` and `Viewfinder/` partials into `ModSystem/` with correct names; delete empty folders | Low — rename only, no logic change | §1, §3 |
| 2 | Move all packet types into new `Network/` folder; update `CollodionModSystem.cs` (remove the 6 inline packets) | Medium — file moves + registration site update | §1, §2 |
| 3 | Flatten `Blocks/Types/` → `Blocks/`; create `BlockEntities/` from `Blocks/Entities/`; rename `Wetplate/` → `Chemistry/` | Low — file moves + .csproj glob update | §1 |
| 4 | Split multi-class files (`WetPlates.cs`, `ItemPlateBase.cs`, `CollodionConfig.cs`, `PhotoLastSeenIndex.cs`) | Low — extract, no logic change | §2 |
| 5 | Rename `Config.cs` → `WetplateEffectsConfig.cs`; `CaptionDialog.cs` → `GuiDialogPhotographCaption.cs` | Trivial | §2 |
| 6 | Resolve `ItemPhotograph` inheritance depth — flatten or document | Low–medium | §10 |
| 7 | Extract config passing from Block* classes; remove stored/transient `CollodionModSystem` fields | Medium — touches lifecycle wiring | §9 |
| 8 | Split `BlockDevelopmentTray.OnBlockInteractStart`; audit remaining large files for >25-line methods | Medium | §5 |

Phases 1–5 are pure file moves and splits with no logic changes; they can be done in a single `refactor` commit batch with no gameplay risk.
Phases 6–8 involve actual code restructuring and should each be a separate commit with a build+test verification.

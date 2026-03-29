# Collodion Mod — Code & Structure Guidelines

These guidelines describe the **target** state of the codebase. Where the current code diverges, it should be refactored to match.

---

## 1. Folder Structure

### Top-level `src/` folders — one concern per folder

| Folder | What belongs here |
|---|---|
| `Blocks/` | `Block*` classes directly — no subfolders |
| `BlockEntities/` | `BlockEntity*` classes directly — no subfolders |
| `Items/` | `Item*` classes directly |
| `Items/Plates/` | Plate item subhierarchy (`ItemPlateBase`, subclasses, render util) |
| `ModSystem/` | `CollodionModSystem` partials and config POCOs |
| `Photograph/` | Photo metadata, caption, seen-index domain — no block/item references |
| `Network/` | All `[ProtoContract]` packet classes — nothing else |
| `Rendering/` | Pure rendering utilities — no game-state writes |
| `Viewfinder/` | Viewfinder FOV, zoom, mouse, zoom renderer |
| `Chemistry/` | Wet-plate chemical effect logic and photo sync networking |
| `Commands/` | `/collodion` command handlers |

**Rules:**
- Do not add files to a folder that crosses its stated concern boundary.
- When a new concern does not fit an existing folder, create a new named folder rather than stretching an existing one.
- Folder names are PascalCase to match VS API namespace conventions (`Blocks`, `Items`, etc.).

### Current divergences to fix

| Current path | Target path | Reason |
|---|---|---|
| `Blocks/Types/*.cs` | `Blocks/*.cs` | "Types" is a meaningless qualifier |
| `Blocks/Entities/*.cs` | `BlockEntities/*.cs` | `Block` and `BlockEntity` are different hierarchies; co-locating them hides that |
| `Wetplate/` | `Chemistry/` | "Wetplate" is a material property, not a concern name; "Chemistry" is clearer |
| Packet `[ProtoContract]` classes in `ModSystem/CollodionModSystem.cs` | `Network/` | Packet definitions have no business in the mod system entry point |

---

## 2. File Naming

- **One public type per file.** The file name must exactly match the class/struct/enum name.
- This applies everywhere, including command handlers and viewfinder classes.

| Wrong | Right |
|---|---|
| `Commands/Handler.cs` | `Commands/CollodionCommandHandler.cs` |
| `Commands/Effects.cs` | `Commands/EffectsCommand.cs` |
| `Viewfinder/Mouse.cs` | `Viewfinder/ViewfinderMouse.cs` |
| `Viewfinder/RuntimeFov.cs` | `Viewfinder/RuntimeFov.cs` ← fine if class is `RuntimeFov` |

- Partial file suffix follows the pattern `ClassName.Concern.cs` — see §3.

---

## 3. Partial Class Splitting

Large classes **must** be split into partial files by concern:

```
ClassName.cs               ← core state, construction/init, serialization
ClassName.Client.cs        ← all client-only logic (rendering, input, GUI)
ClassName.Server.cs        ← all server-only logic (world writes, packet dispatch)
ClassName.MeshGen.cs       ← mesh generation (if substantial)
ClassName.Overlay.cs       ← texture overlay logic (if substantial)
ClassName.Hud.cs           ← HUD / GUI logic (if substantial)
```

A single file exceeding ~300 lines of logic is a signal to split.
Client-only code must live in a `.Client.cs` partial and only be invoked on `EnumAppSide.Client`.
Never use `#if CLIENT` — use the partial split instead.

---

## 4. Naming

- **Classes:** `PascalCase`. VS-type prefix required: `Block*`, `BlockEntity*`, `Item*`.
- **Methods:** `PascalCase`. Name what it does, not how: `TryInsertPlate`, not `DoPlateInsertLogic`.
- **Private fields:** `camelCase`, no underscore prefix.
- **Constants / static readonly:** `PascalCase`.
- **Local variables:** `camelCase`. No single-letter names except `i`, `j` in loops.
- **No unexplained abbreviations.** Permitted VS-established shorthands: `api`, `capi`, `sapi`, `be`, `pos`, `dsc`, `inv`.

---

## 5. Single Responsibility

Each method does exactly one thing. Split when:
- Method body exceeds ~25 lines of logic (switch/case tables that are purely data are exempt).
- The method name contains "And."
- It both reads state **and** writes to the world in the same body.

Each class has exactly one reason to change:
- `Block*` — interaction rules, collision, drop logic, placement.
- `BlockEntity*` — runtime data ownership, serialization, tick registration.
- `Item*` — held-item behavior, crafting hooks, tooltip.
- Render helpers — pure transformation and draw calls, no state ownership.
- Packet types — data transport shape only.

---

## 6. Open / Closed

Classes should be **open for extension, closed for modification**.

In practice:
- Add a new photograph block type by subclassing `BlockPhotographBase` — not by adding a switch/if to it.
- Add a new plate type by subclassing `ItemPlateBase` — not by modifying the base.
- If you find yourself modifying a base class to accommodate one subclass, the logic belongs in the subclass as an `override`.

---

## 7. Liskov Substitution

Every subclass must be a valid substitute for its base in all contexts.

- A subclass of `ItemPlateBase` must honour all preconditions and postconditions of the base methods it overrides.
- If an override needs to **weaken** what the base promises (e.g. returning `false` from a method the base always returns `true` from), that is a design signal that the hierarchy is wrong — consider composition instead.

---

## 8. Interface Segregation

Implement only the interfaces whose contract a class actually fulfils.

- Don't bolt `IRenderer` onto a class that also owns inventory state — split the renderer into a separate type.
- When VS provides a broad interface with many optional members, only call the members your code needs; don't let callers depend on the full interface when a narrower one exists.

---

## 9. Dependency Inversion

Depend on **abstractions**, not concrete types.

- Method parameters and field types should use VS interfaces (`IWorldAccessor`, `IPlayer`, `IBlockAccessor`, `IClientNetworkChannel`) rather than concrete classes wherever the interface exists.
- Don't reach for a concrete `CollodionModSystem` reference inside a `BlockEntity` — pass the specific data or channel it needs at registration time.

---

## 10. Composition Over Inheritance

Prefer calling a helper or utility over extending a class.

- Maximum two levels of inheritance: `VS base → mod concrete` (e.g. `Block → BlockPhotographBase → BlockFramedPhotograph`). A third level is a design smell.
- When two classes need shared logic but aren't truly the same *kind* of thing, extract the logic to a static helper or a small collaborator class — don't invent a common base just to share code.

---

## 11. Code Reuse Over Duplication

Before writing a new helper:
1. Search `src/` for an existing method that already does it.
2. Check established bases and utilities: `ItemPlateBase`, `BlockPhotographBase`, `PhotoPlateRenderUtil`, `WetPlateAttrs`.
3. If the same logic appears in two places, extract it **before** writing a third copy.

Shared helpers live in the folder of the concern they serve:
- Plate rendering → `Items/Plates/PhotoPlateRenderUtil.cs`
- Crop / UV math → `Rendering/PhotoCropMath.cs`
- Pose utilities → `Rendering/RenderPoseUtil.cs`

---

## 12. Separation of Concerns

| Layer | May call | Must NOT call |
|---|---|---|
| `Block*` / `Item*` | `IWorldAccessor`, `IBlockAccessor`, `IPlayer`, sounds, particles | Client renderer, GUI, shader APIs |
| `BlockEntity*` | `IWorldAccessor`, tick registration, packet send | Direct rendering or input handling |
| `.Client.cs` partials | Rendering, input, HUD, sound | Server-side world writes |
| `.Server.cs` partials | World writes, packet send | Client rendering, GUI |
| `Rendering/` helpers | Atlas, mesh, shader APIs | `IWorldAccessor` writes, tick registration |
| `Network/` packet types | — (data only) | Any API or game logic |

Cross-layer communication goes through packets or registered game ticks — never direct calls.

---

## 13. VS Engine API Rules

- **Always confirm signatures** in `decompiled/VintagestoryAPI/` before using an engine method in new code. That workspace is the authoritative source of truth.
- `BlockSounds.GetBreakSound()` / `GetHitSound()` return `SoundAttributes` (struct) in VS ≥ 1.22 — chain `.Location` to get `AssetLocation`.
- Use `Entity.Pos`, not `Entity.SidedPos` (deprecated 1.22+).
- `OnCreatedByCrafting` takes `IRecipeBase`, not `GridRecipe` (changed 1.22+).
- Network: use `IClientNetworkChannel` / `IServerNetworkChannel`; both expose `SendPacket<T>`.
- Block entity serialization uses `ToTreeAttributes` / `FromTreeAttributes` — never `ToBytes` / `FromBytes` on a `BlockEntity`.

---

## 14. Error Handling & Nullability

- Nullable reference types are enabled (`<Nullable>enable</Nullable>` in csproj).
- Use `?.` and `??` rather than explicit null guards where intent is clear.
- Null-guard only at system boundaries: packet handlers, deserialized JSON, player inventory slots.
- Never swallow exceptions silently. Log via `api.Logger.Warning` / `Error`. An empty `catch` block is always wrong.

---

## 15. Thread Safety

- Any `Dictionary` or `List` accessed from both the game tick thread and a network thread requires a dedicated `readonly object` lock declared next to it.
- Disk I/O (photo file writes) uses a `writeLock` — never write files on the game tick thread without it.

---

## 16. Commits

- Atomic: one logical change per commit.
- Format: `type(scope): short imperative description`
  - `fix(vs-1.22): ...` for version-specific API fixes
  - `fix(multiplayer): ...` for MP hardening
  - `feat(...): ...` for new features
  - `refactor(...): ...` for structural moves with no behavior change
  - `chore(...): ...` for build/tooling/docs
- Branch `vs-1.22` carries only VS 1.22 / .NET 10 compatibility changes. New features go on `main` then rebase onto `vs-1.22`.

---

## 17. Build Conventions

| Action | Command |
|---|---|
| Build + deploy (main) | `dotnet build -c Debug Collodion.csproj` from `collodion-vs/` |
| Build + deploy (1.22) | `git checkout vs-1.22` then same command |
| Output zip name (main) | `collodion-{version}-Debug.zip` |
| Output zip name (1.22) | `collodion-{version}-1.22.0.zip` |
| Release zip (no deploy) | `dotnet build -c Release Collodion.csproj` |

Paths are configured via `.env` (not committed). See `CONFIGURATION.md` for key names.

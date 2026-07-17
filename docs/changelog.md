# Collodion -- v2.0.4 Changelog

## Camera
- **Fixed pausing a mounted-camera exposure being unreliable in multiplayer.** Right-clicking to pause would flip the camera to its paused pose for a split second and then go straight back to exposing, so the exposure never actually stopped.

## Rendering
- **Fixed objects disappearing when viewed through a held glass plate.** With a transparent plate held in first person, anything seen through it -- other players, dropped items, framed photos -- would vanish. The plate was filling in the depth buffer as if it were solid, culling everything drawn behind it. It now blends over what's behind without hiding it, and is still correctly covered by anything in front of it.

## Plate box
- **Fixed plates not showing inside the plate box.** A plate placed into an open box slotted in but stayed invisible -- the slot renderer was drawing from the wrong texture atlas page. Plates now appear in their slots again.
- **Reworked loading and unloading the plate box.** Placing plates had stopped working after the v2.0.3 hitbox change -- clicks just closed the box instead of inserting. Now a right-click anywhere on an open box stores the held plate or takes the nearest one, with no need to aim a specific slot. Shift + right-click opens or closes the lid, and Shift + Ctrl + right-click picks the box up with its contents.
- **Setting down a loaded plate box now sounds the part.** Alongside the existing wood thud, each stored plate adds its own randomized glass thud at a random pitch, staggered slightly so a full box settles in with an audible clatter instead of landing silent.

## Sound
- **Opening the viewfinder now plays a cap-removal sound** instead of the placeholder cloth-rustle sound.
- **Polishing a rough plate with cloth now plays a dedicated glass-polishing sound** instead of the vanilla chalk-drawing sound it borrowed before.
- **Scrolling to zoom the viewfinder now ticks with each step**, like a mechanical lens ring.
- **Attaching a tripod to a field camera now plays a sound.**
- **Setting down a mounted camera on its tripod now plays a sound others nearby can hear too.**
- **Inserting or removing a plate from a development tray now plays a sound others nearby can hear too.**

## Plates
- **Rough glass plates can now be polished with any base-game cloth**, not just plain cloth -- mordanted cloth and every dyed variant (red, blue, and so on) work too. The consume amount and "not enough cloth" checks apply the same way regardless of which cloth you use.
- **You can now wash exposed, developed, and finished plates back down to rough glass.** Previously only a plate that had dried out mid-process could be reclaimed. Now pouring water over a plate at any of those stages recovers the glass on demand, wet or dry -- useful for a shot you don't want to keep. It costs double the usual chemical amount and pour time, same regardless of how far along the plate is. **This permanently destroys the photo** -- the image file is deleted, not just detached from the plate.
- **Reclaiming someone else's exposure is allowed by default, and servers can turn it off.** The new `AllowReclaimingOthersExposures` setting only covers plates that have been exposed but not yet developed, since that's the only point where a plate still records who exposed it. Once a plate has been developed, anyone can reclaim it either way.
- **Glass plates now remember how many times they've been reclaimed.** Nothing uses this yet -- it's groundwork for wear and breakage on heavily-reused glass. The count survives the full cycle: reclaiming, re-polishing, re-sensitizing, and being placed down and picked back up.

## Photo storage
- **Photos are now stored per world instead of all sharing one folder.** Previously every singleplayer world, every world you've hosted yourself, and every photo downloaded while visiting someone else's server all landed in the same `ModData/photocore/photos/` folder on your machine -- so photos from completely unrelated worlds piled up together. Each world now gets its own folder under `ModData/photocore/photos/<world-id>/`, so unrelated sessions no longer mix. Photos you already have keep working exactly as before (no action needed, nothing to move) -- they're read from their old shared location, and quietly relocated into the right world's folder the first time that world uses them.
- **Old photos tidy themselves away as you play.** The first time a world shows or serves a photo that's still sitting in the old shared folder, that file is moved into the world's own folder -- so over time the shared folder empties on its own and `/photoadmin` can see those photos again (until it's moved, a photo still in the shared folder renders fine but is invisible to `stats`/`audit`/`delete`). Only the world a photo actually belongs to ever claims it, so nothing gets mixed up. Anything left in the shared folder is a photo no world references anymore -- safe to delete or sort by hand in your file explorer if you like.
- **`/photoadmin` now only sees the current world's photos.** `stats`, `audit`, and `delete` operate on the world you're currently in; they can no longer see or accidentally delete another world's photos. If you kept more than one copy of the same world on this machine (e.g. a downloaded map you also host yourself), those copies still share one photo store, and `/photoadmin delete oldest`/`delete olderthan` will warn you about that before running -- use `delete id` if you see the warning, since it always targets exact photos and is unaffected.

## Modding
- **`IPhotoStore` now resolves photos within the current world/session.** Following the photo storage change above, a photoId taken in one world isn't visible from another -- `TryGetPhotoAsync` and `EnumerateIds` operate on the world you're currently in. Existing consumers need no changes; ids obtained from in-world blocks/items resolve as before.

# Collodion -- v2.0.3 Changelog

## Migration
- **Worlds carried over from before the v2.0.2 domain rename now get their placed blocks and held items remapped automatically** (development trays, plates, cameras, tripods, frames, and the chemistry portion items) instead of just the ModData photo folder. Previously only renaming the ModData folder was documented; anything already placed or held under the old `collodion:*` ids would have shown up as missing/unknown blocks after upgrading. Fresh worlds are unaffected.

## Photo ids
- **New photos get short ids** like `exposure_g8x4m2kd` instead of the old timestamped names. Existing photos keep their old ids and filenames -- everything already taken keeps working with no migration.
- **`/photoadmin delete id` now accepts any unique part of an id**, so `delete id g8x4m2kd` (or the tail of an old-style id) works without typing the whole thing. If a fragment matches more than one photo, it's skipped and reported instead of guessing.
- **The server no longer overwrites an existing photo file on upload.** Uploads for an id that already exists are acknowledged and ignored, so no upload -- accidental or malicious -- can replace another player's photo.

## Configuration
- **Added optional support for the [ConfigLib](https://mods.vintagestory.at/configlib) mod.** If you have it installed, a chunk of `photocore.json`'s settings show up in its in-game GUI with tooltips and proper range validation, and changes apply immediately -- no relog needed. Nothing changes if it's not installed, just edit `photocore.json` by hand.
- **In multiplayer, the host now decides the settings that shape a photo, rather than whichever client happened to take it.** A photo is captured on one player's machine and then shared with everyone, so any of these left to the client meant one player was quietly producing photos unlike everyone else's -- different look, different resolution, or extra sync traffic. Connecting to a server now applies its values for the session, and your own file is left untouched for when you go back to singleplayer. The settings are:
  - `Viewfinder.ApplyFinishingEffects` -- whether the film-look effects get baked into a developed photo.
  - `Viewfinder.PhotoCaptureMaxDimension` -- the resolution photos are captured at, which is also what everyone else has to download.
  - `PhotoSync.PhotoSeenPingIntervalSeconds` -- how often clients report a photo as still being viewed.
  - Chemistry profiles (`chemistry-profiles.json`) -- the emulsion characteristics baked into a photo at capture time.
- **`PhotoSeenPingIntervalSeconds` moved from `Client` to `PhotoSync`** in `photocore.json`, since it's really about photo-sync traffic rather than a general client preference. If you had it tuned away from the 300s default, you'll need to re-set it under `PhotoSync`; it won't carry over on its own.
- **`ConsumePlainClothOnPolish` and `PlainClothConsumedPerPolish` are now just one setting, `ClothConsumedPerPolish`.** Set it to 0 to turn cloth consumption off (still the default), or to whatever amount you want consumed per polish. If you'd enabled the old pair, re-set the amount under the new name.
- **Some `photocore.json` settings are gone, and some moved.** `photocore.json` gets rewritten the first time the new version loads it, so anything below is dropped from your file without a warning -- if you'd tuned one of these, note it down before updating. Nothing here changes how the mod behaves out of the box.
  - **Removed from `Viewfinder`** (they were dev-only knobs with no in-game effect worth exposing): `ZoomMultiplier`, `ExposureReadbackMaxDimension`, and the seven `DebugPreview*` fields (`DebugPreviewPeak`, `DebugPreviewRefreshMs`, `DebugPreviewMaxDimension`, `DebugPreviewWidth`, `DebugPreviewHeight`, `DebugPreviewAnchor`, `DebugPreviewMargin`). The `DebugPreview*` group still works live through `.photocore preview`, it just resets when you relaunch instead of persisting.
  - **Moved from `PhotoSync` to `PhotoSync.Advanced`** (network internals, separated from the two settings operators actually tune): `ChunkSizeBytes`, `MaxTransferBytes`, `ClientStateCleanupIntervalMs`, `ClientRequestRetainSeconds`, `ClientIncomingStaleMs`, `ServerPruneIntervalMs`, `ServerUploadStaleMs`, `ServerMaxOpenUploadsPerPlayer`. Values left at the old flat paths are ignored and reset to defaults -- re-set them under `PhotoSync.Advanced` if you'd changed any.
- **`PhotoDeleteGraceHours` is now editable from the ConfigLib menu**, next to `PhotoSeenPingIntervalSeconds`.
- **`PhotoCaptureMaxDimension` now actually changes the photo's output resolution.** It was silently capped at 640 internally, so any higher value produced an identical photo. It now takes effect up to its new maximum of 1024 (previously 2048, which was never reachable and would have made oversized photos that fail to sync). If you'd set it above 1024, it'll be pulled down to 1024 on first load.

## Camera
- **Fixed mounted-camera exposures silently failing to capture anything during dusk, dawn, a moonless or low-phase night, or heavy fog.** The virtual camera re-renders shadows every exposure tick to match its own heading, but didn't check whether shadows were worth drawing at all in that lighting -- so it crashed when they weren't, permanently ending the exposure with zero frames captured and no error shown. It now skips that work under the same conditions the game itself would, which also avoids some wasted rendering during those same moments.
- **Handheld cameras no longer create a photo the moment an exposure finishes.** Finishing a handheld exposure used to write the photo file and hand it to the server right there in the field -- with no tray involved at all, so the develop whitelist never actually gated it the way it gates every other camera. A finished handheld exposure now becomes a plate that's ready to develop, same as a mounted camera's; only pouring developer on it in a tray creates the photo. Trying to re-expose an already-finished plate now shows the same clear message ("this plate has been fully exposed and cannot be exposed further") on both handheld and mounted cameras, instead of mounted cameras silently doing nothing.
- **Fixed exposures locking as fully-exposed far earlier than they should have.** An exposure was being marked done as soon as its accumulated frames passed the chemistry's `SampleCount` -- a value meant only to describe exposure character and resource cost, not when to stop. Whether an exposure is actually finished (and locked from further resuming) now depends solely on whether it hit the hard accumulation-frame cap (`MaxAccumulatedFrames`) -- the same rule for every camera type -- so `SampleCount` is free to be tuned purely for image character without affecting how long an exposure can run.
- **Finished photographs and framed photos now show "Taken on *month* *day*, Year *year*"**, stamped from the in-game calendar the moment the shutter first opens. Plates exposed before this update simply won't show this.
- **Finished photographs and framed photos now also show "Captured by *player*"**, stamped from whoever opened the shutter, the same moment as the capture date above. Plates exposed before this update won't show this either.
- **Fixed standing photo frames placing rotated one step off** from the direction the player was actually facing.

## Modding
- **Other mods can now read Photocore/Collodion photos through a new `IPhotoStore` API.** Get it from `PhotocoreModSystem.PhotoStore` (resolve via `capi.ModLoader.GetModSystem<PhotocoreModSystem>(withInheritance: true)` so it works whether Collodion or the Kos superset is installed). It's read-only: fetch a photo's bytes by id (waiting on the sync download client-side if needed), check whether one's cached locally, and enumerate known ids by type. Writing to the store isn't exposed.

## Misc
- Default drying duration for plates was bumped to a 0.75 (45 minutes in-game).
- The exposure profile was tweaked to allow for better low-light photography.
- Exposures can run a little longer before locking. `MaxAccumulatedFrames` -- the accumulated-frame count at which an exposure locks as fully exposed -- now defaults to 600 instead of 400, and its highest configurable value is 1000 instead of 600.
- Polishing a plate with too little cloth now fails immediately with a message instead of silently looping. With `ClothConsumedPerPolish` set above 1, holding right-click while carrying less cloth than that used to play the polish out and then repeat forever, consuming nothing and changing nothing; it now rejects the polish up front and says how much cloth you need.
- Cleaned up and reorganized the English language file to make translating it easier, which also fixed some text that never displayed -- plate boxes facing any direction other than north had no name, and framed photos never showed their inventory description.
- Tightened hitboxes on the plate box (both open and closed), the mounted camera, and the standing photo frame -- all were harder to click on than they should have been.
- Tightened held/ground/GUI transforms for the development trays, the standing and wall photo frames, the plate box, and the glass/sensitized/photo plate items.
- The tripod and plate box now show a description when you hover over them in your inventory -- the tripod never had one, and the plate box's existed but wasn't actually wired up to the tooltip.

## Under the hood
- Some internal restructuring (naming, file organization) -- no gameplay changes.
- Fixed a texture-atlas leak in photo sync. Every synced photo used to permanently claim a new atlas region, growing video-memory use over a play session and, in rare cases, making a placed photo render another photo's image. Each photo now reuses a single region.

# Collodion -- v2.0.2 Changelog

## Migration

The mod domain was renamed as part of a Core library split in anticipation of starting work on a separate, more in-depth photography mod. Two things have changed for existing players:

- **Commands are now `.photocore ...` instead of `.collodion ...`.**.
- The ModData folder has moved from `collodion/` to `photocore/`. **If you have photos from a previous version that you want to keep visible in-game, rename the `collodion` folder inside your game's `ModData` directory to `photocore` before launching.**

Players starting fresh do not need to do anything.

## Photo Exposure

- **Finishing effects are now baked into developed photos.** Grain, vignette, halation, lens softness, coating unevenness, dust and scratch textures, and edge toning are composited onto the final plate image by default. These can be disabled via `Viewfinder.ApplyFinishingEffects` in the config to revert to a clean, unprocessed output.
- **Handheld camera no longer overexposes when reusing a primed buffer.** Starting a new exposure on a plate that had already been primed in the same camera session could produce a blown-out result. The primed state is now cleared correctly before each capture.
- **Shutter timing is now applied correctly from the mounted camera and the plate-removal button.** Both paths were falling through to defaults instead of using the configured chemistry values.

## Server Administration

New `/photoadmin` commands for server operators to inspect and manage the server's on-disk photo storage. All destructive commands are dry-run by default and require `confirm` to execute.

- `/photoadmin stats` -- total photo count, disk usage, and a breakdown of seen / never-seen / stale-index rows.
- `/photoadmin audit [count]` -- lists the N least-recently-seen photos with age, first-seen timestamp, and size. Default 20.
- `/photoadmin delete oldest <n> [confirm]` -- removes the N least-recently-seen photos (grace period applies).
- `/photoadmin delete olderthan <days> [confirm]` -- removes every photo not seen within the last N days, including never-seen files.
- `/photoadmin delete id <ids> [confirm]` -- removes specific photos by id (comma-separated). Bypasses the grace period.
- `/photoadmin prune-index [confirm]` -- drops index rows whose backing file no longer exists.

Deletion cascades to derived mask files. Photos still referenced by placed plates render blank after deletion.

**Develop whitelist.** When enabled, only players on the whitelist (and operators) may develop exposed plates. Players not on the list cannot enter the development step but they remain able to expose plates.

- `/photoadmin whitelist enable` / `disable` -- toggle the whitelist on or off.
- `/photoadmin whitelist add <player>` / `remove <player>` -- grant or revoke develop permission by name.
- `/photoadmin whitelist list` -- show all current entries.
- `/photoadmin whitelist status` -- show whether the whitelist is active and how many players it holds.

The whitelist is off by default. Turning it on does not affect server ops.

## Misc.
- Added a photography guide back to the Handbook.

## Under the hood

- The mod was split into a shared Core library with the Collodion head layered on top. This is transparent to players but is the foundation for the separate Kosphotography companion mod.
- Sensitization is now data-driven through `SensitizationRecipe` attributes on `BlockEntityGlassPlate` rather than hardcoded per-block values.
- Declined-interaction events on the server now emit a diagnostic log entry (rate-limited, gated on a debug flag) to help diagnose cases where plates or cameras silently refuse interaction.
- Significant internal cleanup: flattened over-split namespaces, removed dead code, consolidated duplicated helpers, and stripped noisy comments across all feature systems.

---

# Collodion -- v2.0.1 Changelog

## Mounted Camera

- **Other mounted cameras now appear in photographs.** Taking a mounted exposure of another player who is using a deployed camera previously left that camera invisible in the final photo. Now every mounted camera in view is captured including the player's own idle ones.
- **Exposures auto-pause when the photographer disconnects.** A mounted camera left mid-exposure can no longer become permanently stuck in an exposing state. This is reconciled both at the moment of disconnect and when the camera is next loaded, so it also covers crashes and server restarts.
- **Paused cameras are no longer locked for other players.** Once an exposure is paused, any player can unload the plate (Shift+Right-click) or recover the camera (Shift+Ctrl+Right-click). Previously a camera carrying someone else's exposure could be left effectively dead for everyone. An *actively running* exposure is still protected.
- Recovering a camera (Shift+Ctrl+Right-click) no longer leaves a stale state that could cause a new camera placed at the same position to be incorrectly hidden in subsequent handheld shots.

## Plates

- Loading a plate that has dried out now shows a warning. Attempting to expose it is blocked outright.
- Dried plates in an exposing or paused-exposure state can now be reclaimed with water in a development tray. Previously they were stuck and could not be recovered.

## Frames & Photographs

- Frames placed facing east, south, or west now drop and pick up as the standard frame and stack correctly with crafted ones.
- **Framed photos now prefetch their image at load instead of on first render**, so they no longer show blank while downloading on multiplayer clients.
- Retrieved plates should no longer render invisible after reloading a single-player world.

## Configuration

- Development tray chemistry amounts and water pour duration are now driven by config (`DevelopmentTrayChemicalUnitsPerUse` and `DevelopmentTrayInteractions.Water`) instead of hardcoded values.
- Removed several config fields that were defined but had no effect.
- Image-effect commands and the captured baseline now route to `wetplate.json`.

## Under the hood
- Finished plates no longer carry a leftover development-step attribute.

---

# Collodion -- v2.0.0 Changelog

## Photo Exposure

- Exposing a plate onto glass now happens in real time, rather than capturing a single instant.
- Players control the shutter manually -- the longer it's left open, the more light the plate accumulates.
- The wet plate collodion process has an optimal exposure window of around 8 seconds; exposing for less or more than this affects the tonal quality of the developed photograph.
- Accumulated light is run through models borrowed from real photographic chemistry (linearised light accumulation, a Hurter-Driffield density curve, and Schwarzschild reciprocity) in pursuit of an authentic exposure response rather than a simple brightness average.

##### Partial Exposures
- An exposure builds up in memory as a player shoots. When it leaves the camera, that accumulated light is written to disk as a partial exposure carried by the plate.
- Loading the same plate into any camera picks up exactly where the partial exposure left off.
- This works across camera types -- a plate started in a mounted camera can be resumed in a handheld field camera and vice versa.
- A partial exposure is locked to the photographer who started it, other players cannot keep exposing it or develop it.
- **If a plate carrying a partial exposure is lost or destroyed, its saved data remains on disk as an orphaned file. Orphaned partials do not affect gameplay but accumulate over time and can be cleaned up via commands.**

## Mounted Camera

- The field camera can now be deployed on a wooden tripod for fixed-perspective, hands-free capture.

**Deploying:**
1. With a tripod in the offhand, Shift+Right-click to attach it to the camera.
2. Open the viewfinder (Right-click) and frame the shot.
3. Left-click to set the camera down as a block at the current position, locking in the framing.

- Once placed, the camera renders its own view of the scene, independent of where the player looks or moves.
- That view is locked to the exact position and facing of the player when it was set. Players can walk into view and appear in the resulting photograph.
- Partial exposures from a deployed camera behave exactly like handheld ones, including resuming across camera types.
- **If the game window is resized while a deployed camera exposure is in progress, the accumulated frames are discarded and the exposure restarts from zero.**

## Developing & Fixing

Exposed plates are turned into photographs in the development tray by pouring chemistry over them one step at a time.

- Pour **developer** to bring the image up. Full development takes five pours, and the image appears stronger with each one.
- Once fully developed, pour **fixer** once to make the photograph permanent.
- Only the original photographer can develop or fix their own exposed plate. After fixing, the finished photograph can be handled by anyone.
- Plates are wet throughout this process and will **dry out if left too long**. A dried plate can no longer be developed or fixed -- pour **water** over it to wash it back down to a blank glass plate and start over.

## Photographs

### Silver density simulation

- Developed photographs are no longer just a post-processed image -- the physical behavior of silver is simulated.
- Each pixel's brightness determines how much metallic silver was deposited at that point on the glass plate: bright areas of a scene produce dense, opaque silver, while dark areas leave the glass nearly clear.
- The resulting plate is a realistic silver-on-glass negative image rather than a normal positive.

### Development progression

- A freshly exposed plate holds only a latent image; it emerges gradually as developer is poured over the plate.
- The most silver-dense areas (the brightest parts of the scene) appear first, while shadows and corners fill in last.
- This emergence plays out live, so nearby players can watch the image develop in multiplayer.

### Ambrotypes (viewing in frames)

- A developed plate is still a negative, so to view it as a normal image it must first be placed into a frame, which mounts it over a black backing.
- The transparent glass in the shadow areas shows only black; the opaque silver in the bright areas reads as a light tone against that black.
- The result is a positive image -- this emulates the ambrotype technique used in historical wet plate collodion photography.

### Under the hood
- The plate lifecycle was rebuilt around a single stage value carried on the plate, rather than swapping the plate between separate items at each step. Behavior is the same, but plate state is now tracked more reliably across exposure, development, and storage.
- Plate drying now runs on the game engine's own drying system instead of a custom timer, so wet plates dry consistently with the rest of the game's wet/dry mechanics.
- The selection outline on hovered blocks is now suppressed during any active capture (mounted or handheld) so it does not appear in the photograph.

**Commands:**
- `.collodion clearpex` -- reports how many partial exposure files are on disk and how many are orphaned (not associated with any plate in the player's inventory). Run `.collodion clearpex confirm` to delete only the orphaned files; any partials still linked to plates the player is carrying are left untouched.
- `.collodion export` -- exports the developed or finished plate the player is currently holding as a viewable composite PNG file to `ModData/collodion/photos/exports/`.
- `.collodion clearcache` -- clears local photo render caches. Use this if a synced photo is not appearing correctly on plates or frames.

**Configuration:**
- Most timings, costs, and behavior -- pour durations, chemical amounts, how long plates stay wet, plate-box drying, the film look, and capture limits -- are tunable in `collodion.json` (generated in the game's `ModConfig` folder). In multiplayer the server's values are authoritative for gameplay limits.

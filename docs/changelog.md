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

# Collodion — Changelog

## Photo Exposure

- Exposing a plate onto glass now happens in real time, rather than capturing a single instant.
- You control the shutter manually — the longer it's left open, the more light the plate accumulates.
- The wet-plate collodion process has an optimal exposure window of around 8 seconds; exposing for less or more than this affects the tonal quality of the developed photograph.
- Accumulated light is run through models borrowed from real photographic chemistry -- linearised light accumulation, a Hurter–Driffield density curve, and Schwarzschild reciprocity -- in pursuit of an authentic exposure response rather than a simple brightness average.

##### Partial Exposures
- An exposure builds up in memory as you shoot; when it leaves the camera, that accumulated light is written to disk as a partial exposure carried by the plate.
- Loading the same plate into any camera picks up exactly where the partial exposure left off.
- This works across camera types — a plate started in a mounted camera can be resumed in a handheld field camera and vice versa.
- A partial exposure is locked to the photographer who started it; other players cannot keep exposing it or develop it.
- **If a plate carrying a partial exposure is lost or destroyed, its saved data remains on disk as an orphaned file. Orphaned partials do not affect gameplay but accumulate over time and can be cleaned up via commands.**

## Mounted Camera

- The field camera can now be deployed on a wooden tripod for fixed-perspective, hands-free capture.

**Deploying:**
1. With a tripod in your offhand, Shift+Right-click to attach it to the camera.
2. Open the viewfinder (Right-click) and frame your shot.
3. Left-click to set the camera down as a block at your current position, locking in the framing.

- Once placed, the camera renders its own view of the scene, independent of where you look or move.
- That view is locked to the exact position and facing you had when you set it down — which is why you appear in the resulting photograph.
- Partial exposures from a deployed camera behave exactly like handheld ones, including resuming across camera types.
- **If the game window is resized while a deployed camera exposure is in progress, the accumulated frames are discarded and the exposure restarts from zero.**

## Photographs

### Silver density simulation

- Developed photographs are no longer just a post-processed image — the physical behaviour of silver is simulated.
- Each pixel's brightness determines how much metallic silver was deposited at that point on the glass plate: bright areas of a scene produce dense, opaque silver, while dark areas leave the glass nearly clear.
- The resulting plate is a negative: a silver-on-glass image rather than a normal positive.

### Development progression

- A freshly exposed plate holds only a latent image; it emerges gradually as developer is poured over the plate.
- The most silver-dense areas (the brightest parts of the scene) appear first, while shadows and corners fill in last.
- This emergence plays out live, so nearby players can watch the image develop in multiplayer.

### Ambrotypes (viewing in frames)

- A developed plate is still a negative, so to read it as a normal image you place it into a frame, which mounts it over a black backing.
- The transparent glass in the shadow areas shows only black; the opaque silver in the bright areas reads as a light tone against that black.
- The result is a positive image — this is the ambrotype technique used in historical wet-plate collodion photography.

### Under the hood
- The plate lifecycle was rebuilt around a single stage value carried on the plate, rather than swapping the plate between separate items at each step. Behaviour is the same, but plate state is now tracked more reliably across exposure, development, and storage.
- Plate drying now runs on the game engine's own drying system instead of a custom timer, so wet plates dry consistently with the rest of the game's wet/dry mechanics.
- The selection outline on hovered blocks is suppressed during any active capture (mounted or handheld) so it does not appear in the photograph.

**Commands:**

- `.collodion clearpex` — reports how many partial exposure files are on disk and how many are orphaned (not associated with any plate in your inventory). Run `.collodion clearpex confirm` to delete only the orphaned files; any partials still linked to plates you are carrying are left untouched.
- `.collodion export` — exports the developed or finished plate you are holding as a viewable composite PNG file to `ModData/collodion/photos/exports/`.
- `.collodion clearcache` — clears local photo render caches. Use this if a synced photo is not appearing correctly on plates or frames.

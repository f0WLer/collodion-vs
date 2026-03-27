# Collodion Update Notes
This update adds a large round of new features, rendering improvements, performance work, and general polish across the wet plate workflow. Old world saves shouldn't be affected but it's always best to back them up.

## New Features
- Added camera sling for carrying and hanging the camera.
- Added the Plate Box block and block entity for plate storage and display.
- Added support for placing adjacent framed photos by clicking existing frames.
- Added long-exposure groundwork and improved timed capture flow.
- Added configurable wetplate effects.
- Added configurable gameplay, sync, and capture pipeline settings.

### The Camera Sling
- You can now craft a Camera Sling to store your camera and quickly take it out when you're ready to capture an image.
    - Place the sling item in Combat Overhaul's left shoulder slot and press R while holding the camera to wear it over your shoulder and free up an inventory slot.
    - Pressing R while looking at a wall surface will instead nail the camera and sling onto the wall for later use.

### The Plate Box
- Plates can now dry up from the time they've been silvered until they've eventually been exposed, developed, and treated with fixer.
    - Currently, plates are unusable after they've dried up.
- You can now craft a Plate Box to carry up to 8 plates around and keep them from drying up.

### Photo Exposure
- Exposing images onto plates now takes time (5 seconds by default).
- Any movement (walking, turning) during this exposure time will affect the quality of the photo, leading to blurring or smearing of the image.

### Framed Photos
- You can now place two framed photos on the same block.
- Alternate frame textures from boards persist on double-photo blocks.

## Gameplay And Mechanics
- Plates that cannot dry out can now stack up to 8.
- Reduced the time to pour collodion onto clean plates from 5 seconds to 2.5 seconds.
- Development tray interaction text and tips were adjusted to better match the intended processing flow.

## Visual And Rendering Improvements
- In-hand framed photos now crop more like their placed versions.
- Movement artifacts and related wetplate effects were refined and made configurable.
- Fixed mirrored-image issues on developed plates, finished plates, and framed photos.
- Improved third-person transforms for frames and plates, plus GUI transforms for framed photos.
- Improved in-hand plate presentation.
- Adjusted the unfired tray transform so it sits properly in the pit kiln.

## Audio
- Wired new camera, Plate Box, and viewfinder sounds.

## Guide And UX
- Expanded the handbook guide to cover newer features.
- Removed image-dependent framed photo items and blocks from creative inventory.

## Performance
- Improved photo rendering efficiency so placed and displayed images update with less overhead.
- Improved cleanup of old multiplayer photo-transfer data during long play sessions.
- Improved image capture performance to reduce unnecessary slowdowns while taking photos.
- Reduced memory buildup during long sessions.
- Improved frame rendering efficiency in areas with lots of placed photos.

## Fixes And Stability
- Reduced stuttering after each development tray pour stage.
- Improved placement behavior so framed photos and related items behave more reliably when placed.
- Improved cleanup when the mod loads and unloads to help prevent lingering issues during play.
- Reduced several crash cases and startup related errors.
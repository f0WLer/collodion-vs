# Collodion Changelog

## 1.2.2
This update is focused on internal cleanup and stability. Gameplay behavior is intended to stay the same.

### Highlights
- Improved multiplayer reliability for photo sync and related networking paths.
- Improved overall stability with additional null-safety and safer error handling.
- Fixed plate box slot plate visuals so inserted plates align better.

### Performance and reliability
- Reduced duplicated logic across plate rendering, tray processing, and image handling.
- Improved consistency of photo rendering and mesh rebuild paths.
- Continued cleanup of long-session state handling for photo transfer.

### Under-the-hood refactor
- Large source reorganization to make future updates safer and easier to maintain.
- Development tray logic split into clearer client/server paths.
- Shared helpers introduced for plate chemistry, UV/mesh math, and image processing.
- Config and mod-system access standardized across the codebase.

### Documentation
- Added a new architecture guide and updated internal documentation structure.
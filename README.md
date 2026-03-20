# Collodion (Vintage Story Mod)

Wet-plate / classic photography mod for Vintage Story.

## Repo layout
- `src/` C# mod source
- `assets/` JSON assets (items/blocks/shapes/textures/lang/recipes)
- `modinfo.json` mod metadata

## Prerequisites
- .NET SDK 8.0
- A local Vintage Story installation (for the referenced DLLs)

## Configure build (required)
This project references game DLLs directly. Set the install path either by:
- Editing `VINTAGE_STORY_PATH` in `Collodion.csproj`, or
- Passing it at build time: `dotnet build -c Debug /p:VINTAGE_STORY_PATH=F:\\Path\\To\\Vintagestory`

## Build
- Debug: `dotnet build -c Debug`
- Release: `dotnet build -c Release`

## In-game commands
- `.collodion clearcache`
- `.collodion hud (hide|show)`
- `.collodion pose ...`
- `.collodion effects ...`

## Configuration
- Main config file: `collodion.json`
- Configuration guide: `docs/CONFIGURATION.md`

## License
MIT License. See LICENSE.

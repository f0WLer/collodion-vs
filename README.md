# Collodion (Vintage Story Mod)

Wet plate / classic photography mod for Vintage Story.

## Prerequisites
- .NET SDK 10.0
- A local Vintage Story installation (for the referenced DLLs)

## Configure build (required)
This project references game DLLs directly. Set the install path either by:
- Adding `VINTAGE_STORY_PATH` in `.env`, or
- Passing it at build time: `dotnet build -c Debug /p:VINTAGE_STORY_PATH=F:\\Path\\To\\Vintagestory`

## Configuration
- Main config file: `photocore.json` (generated in the game's `ModConfig` folder on first run)
- Most values can be edited live in-game via the `.photocore` admin commands

## License
MIT License. See LICENSE.

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
- Main config file: `collodion.json`
- Configuration guide: `docs/CONFIGURATION.md`

## License
MIT License. See LICENSE.

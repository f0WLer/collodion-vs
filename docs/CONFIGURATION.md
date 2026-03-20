# Collodion Configuration Reference

This document explains key config values in `collodion.json`, what they control, and how to tune them.

## Notes
- Default values below match current in-code behavior.
- In multiplayer, server operators should treat `PhotoSync` limits as authoritative policy.
- Increasing quality/safety often increases CPU or memory cost.

## Photograph

### `Photograph.CaptionMaxLength` (default: `200`)
Maximum caption length accepted/saved for photograph block entities.

- Lower values: smaller packet/data footprint, stricter moderation control.
- Higher values: more expressive captions, larger payloads and save data.

## Plate Processing

### `PlateProcessing.DevelopmentTrayChemicalUnitsPerUse` (default: `40`)
How many portion units are consumed per developer/fixer pour in the development tray.

- Lower values: cheaper chemistry, faster progression.
- Higher values: more expensive chemistry, slower resource progression.

### `PlateProcessing.PolishSeconds` (default: `2.0`)
Hold duration to polish rough plates.

- `0`: instant polish.
- Higher values: slower manual processing pace.

### `PlateProcessing.CoatSeconds` (default: `2.5`)
Hold duration to coat clean plates with collodion.

- `0`: instant coating.
- Higher values: slower manual processing pace.

### `PlateProcessing.CollodionUnitsPerCoat` (default: `5`)
Collodion units consumed per coating action.

- Lower values: cheaper coating.
- Higher values: more expensive coating.

### `PlateProcessing.ConsumePlainClothOnPolish` (default: `false`)
Whether polishing consumes plain cloth.

- `false`: no cloth consumed.
- `true`: consumes `PlainClothConsumedPerPolish` per polish.

### `PlateProcessing.PlainClothConsumedPerPolish` (default: `1`)
Cloth stack amount consumed per polish when consumption is enabled.

## Viewfinder

### `Viewfinder.HoldStillLookContributionScale` (default: `2.0`)
Multiplier for camera look movement contribution to hold-still movement score.

- Lower values: look movement penalizes less.
- Higher values: look movement penalizes more.

### `Viewfinder.ExposureDurationSeconds` (default: `4.0`)
Timed exposure duration for camera capture.

- `0`: instant capture completion.
- Higher values: longer timed exposure window.

## Photo Sync

### `PhotoSync.ChunkSizeBytes` (default: `24576`)
Per-packet payload size used to split image transfers.

- Lower values: more packets, lower per-packet burst size.
- Higher values: fewer packets, larger per-packet bursts.

### `PhotoSync.MaxTransferBytes` (default: `2097152`)
Maximum accepted image byte size for upload/download.

- Lower values: tighter anti-abuse and bandwidth limits.
- Higher values: larger captures allowed, higher bandwidth/memory risk.

### `PhotoSync.ClientStateCleanupIntervalMs` (default: `15000`)
How often the client prunes request/download bookkeeping state.

- Lower values: more frequent cleanup work.
- Higher values: less cleanup overhead, stale state persists longer.

### `PhotoSync.ClientRequestRetainSeconds` (default: `300`)
How long request dedupe records remain on client.

- Lower values: retries can happen sooner.
- Higher values: fewer duplicate requests, longer dedupe memory.

### `PhotoSync.ClientIncomingStaleMs` (default: `120000`)
Client timeout for incomplete incoming image assemblies.

- Lower values: stale/slow downloads dropped sooner.
- Higher values: more tolerance for slow transfers, longer memory retention.

### `PhotoSync.ServerPruneIntervalMs` (default: `30000`)
How often server checks incomplete upload assemblies for staleness.

- Lower values: more frequent cleanup CPU work.
- Higher values: less cleanup overhead, stale uploads survive longer.

### `PhotoSync.ServerUploadStaleMs` (default: `120000`)
Server timeout for incomplete upload assemblies.

- Lower values: abort slow uploads faster.
- Higher values: tolerate slower links, retain memory longer.

## Photo Capture Pipeline

### `PhotoCapturePipeline.BlankDetectSampleDivisor` (default: `32`)
Controls sample density for blank-frame detection fallback.
Sampling step is `pixelCount / divisor` (clamped).

- Lower values: fewer sample points, less CPU, higher false-negative risk.
- Higher values: more sample points, more CPU, more robust blank detection.

### `PhotoCapturePipeline.PngCompressionQuality` (default: `90`)
PNG encode quality/compression parameter used during capture save.

- Lower values: faster encode, usually larger files.
- Higher values: slower encode, usually smaller files.

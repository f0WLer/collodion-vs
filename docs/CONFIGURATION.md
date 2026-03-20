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

## Wetplate Effects (Advanced)

These options expose internal tuning constants used by the wetplate post-process passes.
Defaults preserve current visual behavior.

### Vignette internals
- `Effects.VignetteRadius` (default: `0.78`): radial gradient radius multiplier.

### Sky blowout internals
- `Effects.SkyTopFractionMin` (default: `0.10`): minimum top-area fraction used by sky passes.
- `Effects.SkyBlowoutBlurSigmaBase` (default: `0.60`) and `Effects.SkyBlowoutBlurSigmaScale` (default: `2.20`): bloom blur sigma formula components.
- `Effects.SkyBlowoutLiftScale` (default: `0.06`): additive lift applied in blowout blend.
- `Effects.SkyStreakAmount` (default: `0.02`) and `Effects.SkyStreakFrequency` (default: `2.0`): top-region streak modulation.

### Grain internals
- `Effects.GrainNoiseMaxDimension` (default: `256`): max procedural noise resolution.
- `Effects.GrainBlurSigmaBase` (default: `0.6`) and `Effects.GrainBlurSigmaScale` (default: `1.4`): clump blur sigma formula components.
- `Effects.GrainToneStart` (default: `0.20`) and `Effects.GrainToneEnd` (default: `0.90`): tone range where grain is emphasized.
- `Effects.GrainDeltaScale` (default: `0.22`): multiplier for grain density variation amplitude.
- `Effects.GrainNoiseRangeBase` (default: `40`), `Effects.GrainNoiseRangeScale` (default: `120`), `Effects.GrainNoiseBaseValue` (default: `128`): base/noise span for raw noise generation.

### Dust and scratch internals
- `Effects.DustRadiusMin` (default: `0.4`) and `Effects.DustRadiusRange` (default: `1.8`): dust speck size distribution.
- `Effects.DustLargeFleckInterval` (default: `37`) and `Effects.DustLargeFleckScale` (default: `2.2`): periodic larger flecks.
- `Effects.DustEdgeBiasChanceScale` (default: `0.65`), `Effects.EdgeBiasMaxDistanceFraction` (default: `0.22`), `Effects.EdgeBiasDistanceScaleDivisor` (default: `3.0`): edge-biased dust placement behavior.
- `Effects.ScratchAngleMin` (default: `-0.15`) and `Effects.ScratchAngleRange` (default: `0.30`): scratch direction distribution.
- `Effects.ScratchLengthMin` (default: `0.35`) and `Effects.ScratchLengthRange` (default: `0.85`): scratch length distribution.
- `Effects.ScratchWidthMin` (default: `0.4`) and `Effects.ScratchWidthRange` (default: `1.2`): scratch width distribution.
- `Effects.DarkScratchInterval` (default: `5`), `Effects.DarkScratchOpacityScale` (default: `0.65`), `Effects.DarkScratchWidthScale` (default: `0.7`), `Effects.DarkScratchOffsetPx` (default: `1.0`): occasional dark companion scratch behavior.

### Sepia and micro-blur internals
- `Effects.SepiaEdgeWidthMinPx` (default: `4.0`) and `Effects.SepiaEdgeWidthFraction` (default: `0.18`): edge-warmth mask width.
- `Effects.EdgeWarmthBlendScale` (default: `0.60`): sepia blend amplification at edges.
- `Effects.MicroBlurSigmaBase` (default: `0.10`) and `Effects.MicroBlurSigmaScale` (default: `0.85`): micro-blur sigma formula components.
- `Effects.MicroBlurEdgeKeepScale` (default: `2.2`): edge-preserving keep factor.

### Uneven density internals
- `Effects.PoolingScale` (default: `0.030`): one-sided pooling amplitude scale.
- `Effects.SkyDensityScale` (default: `0.030`), `Effects.SkyMottleScale` (default: `0.020`), `Effects.SkyBandScale` (default: `0.010`): top-region unevenness component scales.
- `Effects.SkyMottleGrid` (default: `24`): low-frequency noise grid size for mottling.
- `Effects.SkyDensityTopScale` (default: `0.6`), `Effects.SkyMottleTopScale` (default: `0.9`), `Effects.SkyBandTopScale` (default: `0.6`): top-falloff influence per unevenness component.
- `Effects.SkyBandFrequency` (default: `3.0`): faint sky band repetition frequency.
- `Effects.PoolingEdgeBiasCenter` (default: `0.55`) and `Effects.PoolingEdgeBiasDenominator` (default: `1.10`): pooling edge bias curve parameters.

### Tone curve internals
- `Effects.ToneSigmoidScale` (default: `6.0`): primary sigmoid slope scale.
- `Effects.HighlightShoulderScale` (default: `12.0`): highlight shoulder strength scaler.
- `Effects.ContrastBlendEnd` (default: `0.85`): luminance where full high-contrast curve is reached.
- `Effects.ShadowContrastReductionScale` (default: `0.25`): reduction factor applied to shadow contrast slope.
- `Effects.ContrastStartMin` (default: `0.02`) and `Effects.ContrastStartMax` (default: `0.75`): safety clamp window for `ContrastStart`.

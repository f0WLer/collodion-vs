using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

using Photochemistry.AdminTooling;
using Photochemistry.Plates.Rendering;
namespace Photochemistry.Tray
{
    
    public sealed partial class BlockEntityDevelopmentTray
    {
        private const byte DeveloperOverlayAlpha = 210;
        private const float DeveloperOverlayScale = 1.32f;
        private const float DeveloperOverlayAlphaStart = 1.0f;
        private const float DeveloperOverlayAlphaEnd = 0.35f;
        private static readonly AssetLocation _developerOverlayTextureAsset = new("photochemistry", "textures/block/liquid/developer.png");
        private static readonly AssetLocation _developerOverlayAtlasKey = new("photochemistry", "devtray-developer-overlay");
        private static readonly AssetLocation _fixerOverlayTextureAsset = new("photochemistry", "textures/block/liquid/fixer.png");
        private static readonly AssetLocation _fixerOverlayAtlasKey = new("photochemistry", "devtray-fixer-overlay");
        private static readonly AssetLocation _waterOverlayTextureAsset = new("survival", "textures/block/liquid/waterportion.png");
        private static readonly AssetLocation _waterOverlayAtlasKey = new("photochemistry", "devtray-water-overlay");
        private bool TryBuildPourOverlayMesh(ICoreClientAPI capi, out MeshData? mesh)
        {
            mesh = null;
            if (!_clientDeveloperOverlayActive) return false;
            if (!TryGetOverlayTexture(capi, _clientDeveloperOverlayAlpha, _clientOverlayAction, out TextureAtlasPosition devTex)) return false;

            try
            {
                ITexPositionSource baseSource = capi.Tesselator.GetTextureSource(Block);
                ITexPositionSource texSource = new PlatePhotoTextureSource(baseSource, devTex);

                var shape = Block?.Shape?.Clone();
                if (shape == null) return false;

                shape.IgnoreElements = ["base", "wall-n", "wall-s", "wall-e", "wall-w"];
                capi.Tesselator.TesselateShape(
                    "photochemistry-devtray-devoverlay",
                    Block?.Code ?? new AssetLocation("photochemistry", "developmenttray-red"),
                    shape,
                    out mesh,
                    texSource
                );

                mesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), DeveloperOverlayScale, 1f, DeveloperOverlayScale);

                int placementYawDeg = GetPlacementFacingYawDeg();
                if (placementYawDeg != 0)
                    mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, placementYawDeg * GameMath.DEG2RAD, 0f);

                // Above both the plate (0.0006) and the silver image layer (0.0012).
                mesh.Translate(0f, 0.0018f, 0f);

                PhotoMeshUtil.SetTransparentRenderPass(mesh);
                ApplyOverlayAlpha(mesh, _clientDeveloperOverlayAlpha);
                return true;
            }
            catch
            {
                mesh = null;
                return false;
            }
        }

        private bool TryGetPourOverlayAlpha(ICoreClientAPI capi, out float alpha, out string action)
        {
            alpha = DeveloperOverlayAlphaStart;
            action = string.Empty;
            try
            {
                if (capi?.World?.Player?.Entity?.Attributes == null) return false;

                ITreeAttribute? tree = capi.World.Player.Entity.Attributes.GetTreeAttribute(TrayTimedInteractionState.TimedAttrKey);
                if (tree == null) return false;

                string timedAction = tree.GetString(TrayTimedInteractionState.TimedActionKey, "");
                bool isDeveloper = string.Equals(timedAction, BlockDevelopmentTray.ActionDeveloper, StringComparison.Ordinal);
                bool isFixer = string.Equals(timedAction, BlockDevelopmentTray.ActionFixer, StringComparison.Ordinal);
                bool isWater = string.Equals(timedAction, BlockDevelopmentTray.ActionWater, StringComparison.Ordinal);
                if (!isDeveloper && !isFixer && !isWater) return false;

                if (tree.GetInt(TrayTimedInteractionState.TimedXKey) != Pos.X
                    || tree.GetInt(TrayTimedInteractionState.TimedYKey) != Pos.Y
                    || tree.GetInt(TrayTimedInteractionState.TimedZKey) != Pos.Z)
                {
                    return false;
                }

                long startMs = 0;
                int durationMs = 0;
                try
                {
                    startMs = tree.GetLong(TrayTimedInteractionState.TimedStartMsKey, 0);
                    durationMs = tree.GetInt(TrayTimedInteractionState.TimedDurationMsKey, 0);
                }
                catch
                {
                    startMs = 0;
                    durationMs = 0;
                }

                if (durationMs <= 0)
                {
                    float fallbackSeconds = isWater
                        ? GetWaterPourSeconds(capi)
                        : (isFixer ? GetFixerPourSeconds(capi) : GetDeveloperPourSeconds(capi));
                    durationMs = (int)Math.Round(fallbackSeconds * 1000f);
                }

                long nowMs;
                try
                {
                    nowMs = (long)capi.World.ElapsedMilliseconds;
                }
                catch
                {
                    nowMs = Environment.TickCount64;
                }

                float t = 0f;
                if (startMs > 0 && durationMs > 0)
                {
                    t = (nowMs - startMs) / (float)durationMs;
                }

                if (t < 0f) t = 0f;
                if (t > 1f) t = 1f;

                alpha = DeveloperOverlayAlphaStart + (DeveloperOverlayAlphaEnd - DeveloperOverlayAlphaStart) * t;
                action = timedAction;
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(capi.Logger, "TryGetPourOverlayAlpha failed: {0}", ex.Message);
                return false;
            }
        }

        private static float GetFixerPourSeconds(ICoreClientAPI capi)
        {
            float seconds = GetDevelopmentTrayInteractionConfig(capi)?.Fixer?.DurationSeconds ?? 1.25f;
            if (seconds < 0.05f) seconds = 0.05f;
            if (seconds > 30f) seconds = 30f;
            return seconds;
        }

        private static float GetDeveloperPourSeconds(ICoreClientAPI capi)
        {
            float seconds = GetDevelopmentTrayInteractionConfig(capi)?.Developer?.DurationSeconds ?? 1.25f;
            if (seconds < 0.05f) seconds = 0.05f;
            if (seconds > 30f) seconds = 30f;
            return seconds;
        }

        private static float GetWaterPourSeconds(ICoreClientAPI capi)
        {
            float seconds = GetDevelopmentTrayInteractionConfig(capi)?.Water?.DurationSeconds ?? 1.25f;
            if (seconds < 0.05f) seconds = 0.05f;
            if (seconds > 30f) seconds = 30f;
            return seconds;
        }

        private static DevelopmentTrayInteractionConfig? GetDevelopmentTrayInteractionConfig(ICoreClientAPI capi)
        {
            try
            {
                var modSys = PhotochemistryConfigAccess.ResolveClientModSystem(capi);
                return modSys?.Config?.DevelopmentTrayInteractions;
            }
            catch (Exception ex)
            {
                Log.Debug(capi?.Logger, "development tray interaction config lookup failed: {0}", ex.Message);
                return null;
            }
        }

        private static void ApplyOverlayAlpha(MeshData mesh, float alpha)
        {
            if (mesh?.Rgba == null || mesh.Rgba.Length == 0) return;

            if (alpha < 0f) alpha = 0f;
            if (alpha > 1f) alpha = 1f;

            byte alphaByte = (byte)(alpha * 255f);
            for (int index = 0; index < mesh.Rgba.Length; index += 4)
            {
                mesh.Rgba[index + 0] = 255;
                mesh.Rgba[index + 1] = 255;
                mesh.Rgba[index + 2] = 255;
                mesh.Rgba[index + 3] = alphaByte;
            }
        }

        private static bool TryGetOverlayTexture(ICoreClientAPI capi, float alpha, string? action, out TextureAtlasPosition texPos)
        {
            texPos = capi.BlockTextureAtlas.UnknownTexturePosition;

            const int alphaSteps = 40;
            int alphaStep = (int)Math.Round(alpha * alphaSteps);
            if (alphaStep < 0) alphaStep = 0;
            if (alphaStep > alphaSteps) alphaStep = alphaSteps;
            if (alpha >= 0.999f) alphaStep = alphaSteps;

            float stepScale = alphaStep / (float)alphaSteps;
            byte effectiveAlpha = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(DeveloperOverlayAlpha * stepScale)));

            bool isFixer = string.Equals(action, BlockDevelopmentTray.ActionFixer, StringComparison.Ordinal);
            bool isWater = string.Equals(action, BlockDevelopmentTray.ActionWater, StringComparison.Ordinal);
            AssetLocation baseAtlasKey = isWater ? _waterOverlayAtlasKey : (isFixer ? _fixerOverlayAtlasKey : _developerOverlayAtlasKey);
            AssetLocation baseAsset = isWater ? _waterOverlayTextureAsset : (isFixer ? _fixerOverlayTextureAsset : _developerOverlayTextureAsset);

            string atlasPrefix = isWater ? "devtray-water-overlay" : (isFixer ? "devtray-fixer-overlay" : "devtray-developer-overlay");
            AssetLocation atlasKey = alphaStep == alphaSteps
                ? baseAtlasKey
                : new AssetLocation("photochemistry", atlasPrefix + $"-a{alphaStep}");

            try
            {
                capi.BlockTextureAtlas.GetOrInsertTexture(
                    atlasKey,
                    out int _,
                    out texPos,
                    () =>
                    {
                        try
                        {
                            var asset = capi.Assets.TryGet(baseAsset);
                            if (asset != null)
                            {
                                var bmp = capi.Render.BitmapCreateFromPng(asset.Data);
                                try { bmp?.MulAlpha(effectiveAlpha); }
                                catch { /* intentional: MulAlpha is best-effort; broken bitmap falls back to null return */ }
                                return bmp;
                            }
                        }
                        catch { /* intentional: asset load / bitmap decode failure falls back to null overlay */ }

                        return null;
                    },
                    0.05f
                );

                return texPos != null && texPos != capi.BlockTextureAtlas.UnknownTexturePosition;
            }
            catch
            {
                return false;
            }
        }

    }
}

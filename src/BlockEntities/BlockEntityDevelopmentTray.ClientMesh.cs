using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public sealed partial class BlockEntityDevelopmentTray
    {
        private void RequestClientMeshRebuild()
        {
            if (Api?.Side != EnumAppSide.Client) return;

            ICoreClientAPI capi = (ICoreClientAPI)Api;
            lock (clientMeshLock)
            {
                if (clientMeshQueued) return;
                clientMeshQueued = true;
            }

            try
            {
                capi.Event.EnqueueMainThreadTask(() =>
                {
                    lock (clientMeshLock) clientMeshQueued = false;
                    BuildClientMesh(capi);
                }, "collodion-devtray-rebuild");
            }
            catch (Exception ex)
            {
                capi.Logger.Warning("[Collodion] RequestClientMeshRebuild enqueue failed: {0}", ex.Message);
                lock (clientMeshLock) clientMeshQueued = false;
            }
        }

        private void BuildClientMesh(ICoreClientAPI capi)
        {
            if (capi == null) return;

            ItemStack? plate;
            string? sig;
            lock (plateLock)
            {
                plate = PlateStack?.Clone();
                sig = BlockEntityDevelopmentTray.ComputePlateSignature(PlateStack);
            }

            if (plate?.Collectible?.Code == null)
            {
                lock (clientMeshLock)
                {
                    clientTrayBodyMesh = null;
                    clientPhotoMesh = null;
                    clientDeveloperOverlayMesh = null;
                }
                clientRenderSignature = sig;
                MarkDirty(true);
                return;
            }

            bool builtTrayBody = TryBuildTrayBodyMesh(capi, out MeshData? trayBodyMesh);
            bool builtPhotoMesh = TryBuildPlateMesh(capi, plate, out MeshData? photoMesh);

            lock (clientMeshLock)
            {
                if (builtTrayBody) clientTrayBodyMesh = trayBodyMesh;
                clientPhotoMesh = builtPhotoMesh ? photoMesh : null;
            }

            bool builtOverlayMesh = TryBuildPourOverlayMesh(capi, out MeshData? devOverlayMesh);

            lock (clientMeshLock)
            {
                if (builtOverlayMesh)
                    clientDeveloperOverlayMesh = devOverlayMesh;
                else if (!clientDeveloperOverlayActive)
                    clientDeveloperOverlayMesh = null;
            }

            clientRenderSignature = sig;
            MarkDirty(true);
        }

        private bool TryBuildTrayBodyMesh(ICoreClientAPI capi, out MeshData? mesh)
        {
            mesh = null;
            try
            {
                ITexPositionSource bodySource = capi.Tesselator.GetTextureSource(Block);
                var bodyShape = Block?.Shape?.Clone();
                if (bodyShape == null) return false;

                bodyShape.IgnoreElements = new[] { "plate" };
                capi.Tesselator.TesselateShape(
                    "collodion-devtray-body",
                    Block?.Code ?? new AssetLocation("collodion", "developmenttray-red"),
                    bodyShape,
                    out mesh,
                    bodySource
                );
                return mesh != null;
            }
            catch
            {
                mesh = null;
                return false;
            }
        }

        private bool TryBuildPlateMesh(ICoreClientAPI capi, ItemStack plate, out MeshData? mesh)
        {
            mesh = null;
            bool showPhoto = ShouldShowTrayPhoto(plate);

            if (showPhoto && PhotoPlateRenderUtil.TryGetPhotoBlockTexture(capi, plate, out TextureAtlasPosition photoTex, out float photoAspect, Pos))
            {
                try
                {
                    ITexPositionSource baseSource = capi.Tesselator.GetTextureSource(Block);
                    ITexPositionSource texSource = new PlatePhotoTextureSource(baseSource, photoTex);

                    var shape = Block?.Shape?.Clone();
                    if (shape == null) return false;

                    shape.IgnoreElements = new[] { "base", "wall-n", "wall-s", "wall-e", "wall-w" };
                    capi.Tesselator.TesselateShape(
                        "collodion-devtray-platephoto",
                        Block?.Code ?? new AssetLocation("collodion", "developmenttray-red"),
                        shape,
                        out mesh,
                        texSource
                    );

                    PhotoMeshUtil.StampUvByRotationCropped(mesh, photoTex, 0, photoAspect, PhotoMeshUtil.PhotoTargetAspect);

                    int placementYawDeg = GetPlacementFacingYawDeg();
                    if (placementYawDeg != 0)
                        mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, placementYawDeg * GameMath.DEG2RAD, 0f);

                    mesh.Translate(0f, 0.0006f, 0f);
                    return true;
                }
                catch
                {
                    mesh = null;
                    return false;
                }
            }

            if (!showPhoto)
            {
                try
                {
                    ITexPositionSource baseSource = capi.Tesselator.GetTextureSource(Block);
                    var shape = Block?.Shape?.Clone();
                    if (shape == null) return false;

                    shape.IgnoreElements = new[] { "base", "wall-n", "wall-s", "wall-e", "wall-w" };
                    capi.Tesselator.TesselateShape(
                        "collodion-devtray-plainplate",
                        Block?.Code ?? new AssetLocation("collodion", "developmenttray-red"),
                        shape,
                        out mesh,
                        baseSource
                    );

                    int placementYawDeg = GetPlacementFacingYawDeg();
                    if (placementYawDeg != 0)
                        mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, placementYawDeg * GameMath.DEG2RAD, 0f);

                    mesh.Translate(0f, 0.0006f, 0f);
                    return mesh != null;
                }
                catch
                {
                    mesh = null;
                    return false;
                }
            }

            return false;
        }

        private bool TryBuildPourOverlayMesh(ICoreClientAPI capi, out MeshData? mesh)
        {
            mesh = null;
            if (!clientDeveloperOverlayActive) return false;
            if (!TryGetOverlayTexture(capi, clientDeveloperOverlayAlpha, clientOverlayAction, out TextureAtlasPosition devTex)) return false;

            try
            {
                ITexPositionSource baseSource = capi.Tesselator.GetTextureSource(Block);
                ITexPositionSource texSource = new PlatePhotoTextureSource(baseSource, devTex);

                var shape = Block?.Shape?.Clone();
                if (shape == null) return false;

                shape.IgnoreElements = new[] { "base", "wall-n", "wall-s", "wall-e", "wall-w" };
                capi.Tesselator.TesselateShape(
                    "collodion-devtray-devoverlay",
                    Block?.Code ?? new AssetLocation("collodion", "developmenttray-red"),
                    shape,
                    out mesh,
                    texSource
                );

                mesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), DeveloperOverlayScale, 1f, DeveloperOverlayScale);

                int placementYawDeg = GetPlacementFacingYawDeg();
                if (placementYawDeg != 0)
                    mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, placementYawDeg * GameMath.DEG2RAD, 0f);

                mesh.Translate(0f, 0.0012f, 0f);

                ForceTransparentPass(mesh);
                ApplyOverlayAlpha(mesh, clientDeveloperOverlayAlpha);
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

                ITreeAttribute? tree = capi.World.Player.Entity.Attributes.GetTreeAttribute(BlockDevelopmentTray.TimedAttrKey);
                if (tree == null) return false;

                string timedAction = tree.GetString(BlockDevelopmentTray.TimedActionKey, "");
                bool isDeveloper = string.Equals(timedAction, BlockDevelopmentTray.ActionDeveloper, StringComparison.Ordinal);
                bool isFixer = string.Equals(timedAction, BlockDevelopmentTray.ActionFixer, StringComparison.Ordinal);
                bool isWater = string.Equals(timedAction, BlockDevelopmentTray.ActionWater, StringComparison.Ordinal);
                if (!isDeveloper && !isFixer && !isWater) return false;

                if (tree.GetInt(BlockDevelopmentTray.TimedXKey) != Pos.X
                    || tree.GetInt(BlockDevelopmentTray.TimedYKey) != Pos.Y
                    || tree.GetInt(BlockDevelopmentTray.TimedZKey) != Pos.Z)
                {
                    return false;
                }

                long startMs = 0;
                int durationMs = 0;
                try
                {
                    startMs = tree.GetLong(BlockDevelopmentTray.TimedStartMsKey, 0);
                    durationMs = tree.GetInt(BlockDevelopmentTray.TimedDurationMsKey, 0);
                }
                catch
                {
                    startMs = 0;
                    durationMs = 0;
                }

                if (durationMs <= 0)
                {
                    float fallbackSeconds = isWater ? 1.25f : (isFixer ? GetFixerPourSeconds(capi) : GetDeveloperPourSeconds(capi));
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

                alpha = Lerp(DeveloperOverlayAlphaStart, DeveloperOverlayAlphaEnd, t);
                action = timedAction;
                return true;
            }
            catch (Exception ex)
            {
                capi.Logger.Warning("[Collodion] TryGetPourOverlayAlpha failed: {0}", ex.Message);
                return false;
            }
        }

        private static bool IsMultiplayerClient(ICoreClientAPI capi)
        {
            if (capi == null) return false;

            if (TryGetBoolProperty(capi, out bool isSingle, "IsSinglePlayer", "IsSingleplayer"))
            {
                return !isSingle;
            }

            object? world = capi.World;
            if (world != null)
            {
                if (TryGetBoolProperty(world, out bool worldSingle, "IsSinglePlayer", "IsSingleplayer"))
                {
                    return !worldSingle;
                }

                if (TryGetBoolProperty(world, out bool isRemote, "IsRemoteServer", "IsRemote"))
                {
                    return isRemote;
                }
            }

            object? worldData = null;
            try { worldData = capi.World?.Player?.WorldData; }
            catch { /* intentional: best-effort worldData probe via reflection compatibility path */ worldData = null; }
            if (worldData != null && TryGetBoolProperty(worldData, out bool dataSingle, "IsSinglePlayer", "IsSingleplayer"))
            {
                return !dataSingle;
            }

            return false;
        }

        private static bool TryGetBoolProperty(object obj, out bool value, params string[] names)
        {
            value = false;
            if (obj == null || names == null) return false;

            var t = obj.GetType();
            const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                try
                {
                    var prop = t.GetProperty(name, Flags);
                    if (prop != null && prop.PropertyType == typeof(bool))
                    {
                        object? v = prop.GetValue(obj);
                        if (v is bool b)
                        {
                            value = b;
                            return true;
                        }
                    }
                }
                catch { /* intentional: best-effort reflection probe */ }

                try
                {
                    var field = t.GetField(name, Flags);
                    if (field != null && field.FieldType == typeof(bool))
                    {
                        object? v = field.GetValue(obj);
                        if (v is bool b)
                        {
                            value = b;
                            return true;
                        }
                    }
                }
                catch { /* intentional: best-effort reflection probe */ }
            }

            return false;
        }

        private static float GetFixerPourSeconds(ICoreClientAPI capi)
        {
            float seconds = 1.25f;
            var cfg = GetDevelopmentTrayInteractionConfig(capi);
            seconds = cfg?.Fixer?.DurationSeconds ?? seconds;

            return seconds < 0.05f ? 0.05f : seconds;
        }

        private static float GetDeveloperPourSeconds(ICoreClientAPI capi)
        {
            float seconds = 2.00f;
            var cfg = GetDevelopmentTrayInteractionConfig(capi);
            seconds = cfg?.Developer?.DurationSeconds ?? seconds;

            return seconds < 0.05f ? 0.05f : seconds;
        }

        private static DevelopmentTrayInteractionConfig? GetDevelopmentTrayInteractionConfig(ICoreClientAPI capi)
        {
            try
            {
                var modSys = CollodionModSystem.ClientInstance ?? capi.ModLoader.GetModSystem<CollodionModSystem>();
                return modSys?.Config?.DevelopmentTrayInteractions;
            }
            catch (Exception ex)
            {
                capi?.Logger?.Warning("[Collodion] development tray interaction config lookup failed: {0}", ex.Message);
                return null;
            }
        }

        private static void ApplyOverlayAlpha(MeshData mesh, float alpha)
        {
            if (mesh?.Rgba == null || mesh.Rgba.Length == 0) return;

            if (alpha < 0f) alpha = 0f;
            if (alpha > 1f) alpha = 1f;

            byte a = (byte)(alpha * 255f);
            for (int i = 0; i < mesh.Rgba.Length; i += 4)
            {
                mesh.Rgba[i + 0] = 255;
                mesh.Rgba[i + 1] = 255;
                mesh.Rgba[i + 2] = 255;
                mesh.Rgba[i + 3] = a;
            }
        }

        private static float Lerp(float a, float b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return a + (b - a) * t;
        }

        private static bool TryGetOverlayTexture(ICoreClientAPI capi, float alpha, string? action, out TextureAtlasPosition texPos)
        {
            texPos = capi.BlockTextureAtlas.UnknownTexturePosition;

            const int AlphaSteps = 40;
            int alphaStep = (int)Math.Round(alpha * AlphaSteps);
            if (alphaStep < 0) alphaStep = 0;
            if (alphaStep > AlphaSteps) alphaStep = AlphaSteps;
            if (alpha >= 0.999f) alphaStep = AlphaSteps;

            float stepScale = alphaStep / (float)AlphaSteps;
            byte effectiveAlpha = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(DeveloperOverlayAlpha * stepScale)));

            bool isFixer = string.Equals(action, BlockDevelopmentTray.ActionFixer, StringComparison.Ordinal);
            bool isWater = string.Equals(action, BlockDevelopmentTray.ActionWater, StringComparison.Ordinal);
            AssetLocation baseAtlasKey = isWater ? WaterOverlayAtlasKey : (isFixer ? FixerOverlayAtlasKey : DeveloperOverlayAtlasKey);
            AssetLocation baseAsset = isWater ? WaterOverlayTextureAsset : (isFixer ? FixerOverlayTextureAsset : DeveloperOverlayTextureAsset);

            string atlasPrefix = isWater ? "devtray-water-overlay" : (isFixer ? "devtray-fixer-overlay" : "devtray-developer-overlay");
            AssetLocation atlasKey = alphaStep == AlphaSteps
                ? baseAtlasKey
                : new AssetLocation("collodion", atlasPrefix + $"-a{alphaStep}");

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
                                catch { /* intentional: alpha modulation is optional for fallback rendering */ }
                                return bmp;
                            }
                        }
                        catch { /* intentional: texture fallback returns null when asset load fails */ }

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

        private static void ForceTransparentPass(MeshData mesh)
        {
            if (mesh == null) return;

            int quadCount = mesh.VerticesCount / 4;
            if (quadCount <= 0) return;

            short[] passes = mesh.RenderPassesAndExtraBits;
            if (passes == null || passes.Length < quadCount)
            {
                passes = new short[quadCount];
            }

            ushort passVal = (ushort)EnumChunkRenderPass.Transparent;
            for (int i = 0; i < quadCount; i++)
            {
                passes[i] = (short)passVal;
            }

            mesh.RenderPassesAndExtraBits = passes;
            mesh.RenderPassCount = quadCount;
        }

        private static bool ShouldShowTrayPhoto(ItemStack plate)
        {
            if (plate?.Attributes == null) return false;

            int pours = 0;
            try
            {
                pours = plate.Attributes.GetInt(WetPlateAttrs.DevelopPours, 0);
            }
            catch
            {
                pours = 0;
            }

            if (pours > 0) return true;

            string stage = plate.Attributes.GetString(WetPlateAttrs.PlateStage) ?? string.Empty;
            if (stage.Equals("developing", StringComparison.OrdinalIgnoreCase)) return true;
            if (stage.Equals("developed", StringComparison.OrdinalIgnoreCase)) return true;
            if (stage.Equals("finished", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private int GetPlacementFacingYawDeg()
        {
            string facing = PlacementFacingCode;
            return facing switch
            {
                "south" => 90,
                "west" => 0,
                "north" => 270,
                _ => 180
            };
        }
    }
}

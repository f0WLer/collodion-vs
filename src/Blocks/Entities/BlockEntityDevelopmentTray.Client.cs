using Vintagestory.API.Client;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.API.Datastructures;

namespace Collodion
{
    public sealed partial class BlockEntityDevelopmentTray
    {
        private const float PhotoTargetAspect = 10f / 11f;
        private const byte DeveloperOverlayAlpha = 210;
        private const float DeveloperOverlayScale = 1.32f;
        private const float DeveloperOverlayAlphaStart = 1.0f;
        private const float DeveloperOverlayAlphaEnd = 0.35f;

        private static readonly AssetLocation DeveloperOverlayTextureAsset = new AssetLocation("collodion", "textures/block/liquid/developer.png");
        private static readonly AssetLocation DeveloperOverlayAtlasKey = new AssetLocation("collodion", "devtray-developer-overlay");
        private static readonly AssetLocation FixerOverlayTextureAsset = new AssetLocation("collodion", "textures/block/liquid/fixer.png");
        private static readonly AssetLocation FixerOverlayAtlasKey = new AssetLocation("collodion", "devtray-fixer-overlay");

        private sealed class PlatePhotoTextureSource : ITexPositionSource
        {
            private readonly ITexPositionSource baseSource;
            private readonly TextureAtlasPosition photoTex;

            public PlatePhotoTextureSource(ITexPositionSource baseSource, TextureAtlasPosition photoTex)
            {
                this.baseSource = baseSource;
                this.photoTex = photoTex;
            }

            public TextureAtlasPosition this[string textureCode]
            {
                get
                {
                    if (string.Equals(textureCode, "plate", StringComparison.OrdinalIgnoreCase))
                    {
                        return photoTex;
                    }

                    return baseSource[textureCode];
                }
            }

            public Size2i AtlasSize => baseSource.AtlasSize;
        }

        private readonly object clientMeshLock = new object();
        private MeshData? clientPhotoMesh;
        private MeshData? clientDeveloperOverlayMesh;
        private bool clientMeshQueued;
        private bool clientNeedsRebuild;
        private string? clientRenderSignature;
        private bool clientDeveloperOverlayActive;
        private float clientDeveloperOverlayAlpha = 1.0f;
        private long clientDeveloperOverlayLastRebuildMs;
        private string? clientOverlayAction;

        partial void ClientInitialize(ICoreAPI api)
        {
            if (api?.Side != EnumAppSide.Client) return;

            // Poll local player state on the main thread and request re-tesselation when it changes.
            try
            {
                RegisterGameTickListener(_ =>
                {
                    if (Api?.Side != EnumAppSide.Client) return;
                    ICoreClientAPI capi = (ICoreClientAPI)Api;
                    long nowMs;
                    try
                    {
                        nowMs = (long)capi.World.ElapsedMilliseconds;
                    }
                    catch
                    {
                        nowMs = Environment.TickCount64;
                    }

                    bool shouldShow = TryGetPourOverlayAlpha(capi, out float alpha, out string action);
                    if (shouldShow)
                    {
                        bool actionChanged = !string.Equals(action, clientOverlayAction, StringComparison.Ordinal);
                        bool alphaChanged = Math.Abs(alpha - clientDeveloperOverlayAlpha) > 0.001f;
                        bool stale = nowMs - clientDeveloperOverlayLastRebuildMs > 200;
                        if (clientDeveloperOverlayActive && !alphaChanged && !stale && !actionChanged) return;

                        clientDeveloperOverlayActive = true;
                        clientDeveloperOverlayAlpha = alpha;
                        clientDeveloperOverlayLastRebuildMs = nowMs;
                        clientOverlayAction = action;
                        clientNeedsRebuild = true;
                        RequestClientMeshRebuild();

                        try { capi.World.BlockAccessor.MarkBlockDirty(Pos); } catch { }
                        return;
                    }

                    if (!clientDeveloperOverlayActive) return;

                    clientDeveloperOverlayActive = false;
                    clientDeveloperOverlayLastRebuildMs = 0;
                    clientOverlayAction = null;
                    clientNeedsRebuild = true;
                    RequestClientMeshRebuild();

                    try { capi.World.BlockAccessor.MarkBlockDirty(Pos); } catch { }
                }, 50);
            }
            catch { }
        }

        partial void ClientPlateChanged(bool markBlockDirty)
        {
            if (Api?.Side != EnumAppSide.Client) return;

            clientNeedsRebuild = true;
            lock (clientMeshLock)
            {
                // Keep existing meshes until the rebuild completes to avoid brief transparency.
                if (!clientDeveloperOverlayActive)
                {
                    clientDeveloperOverlayMesh = null;
                }
            }
            clientRenderSignature = null;
            RequestClientMeshRebuild();

            if (!markBlockDirty) return;
            try
            {
                ((ICoreClientAPI)Api).World.BlockAccessor.MarkBlockDirty(Pos);
            }
            catch { }
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (Api?.Side != EnumAppSide.Client)
            {
                return base.OnTesselation(mesher, tessThreadTesselator);
            }

            // OnTesselation runs on the tesselation thread: only read cached meshes here.
            string? sig = null;
            try
            {
                lock (plateLock)
                {
                    sig = BlockEntityDevelopmentTray.ComputePlateSignature(PlateStack);
                }
            }
            catch
            {
                sig = null;
            }

            if (!string.Equals(clientRenderSignature, sig, StringComparison.Ordinal))
            {
                clientNeedsRebuild = true;
            }

            if (clientNeedsRebuild)
            {
                clientNeedsRebuild = false;
                RequestClientMeshRebuild();
            }

            MeshData? photoMesh;
            MeshData? overlayMesh;
            lock (clientMeshLock)
            {
                photoMesh = clientPhotoMesh;
                overlayMesh = clientDeveloperOverlayMesh;
            }

            if (photoMesh != null)
            {
                mesher.AddMeshData(photoMesh.Clone());
            }

            if (overlayMesh != null)
            {
                mesher.AddMeshData(overlayMesh.Clone());
            }

            // Return false so the normal tray block mesh still renders.
            return base.OnTesselation(mesher, tessThreadTesselator);
        }

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
            catch
            {
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
                    clientPhotoMesh = null;
                    clientDeveloperOverlayMesh = null;
                }
                clientRenderSignature = sig;
                MarkDirty(true);
                return;
            }

            bool showPhoto = ShouldShowTrayPhoto(plate);

            // If we're not showing the photo, we may still want to show the temporary developer overlay
            // (e.g., the very first developer pour on an exposed plate).
            if (!showPhoto && !clientDeveloperOverlayActive)
            {
                lock (clientMeshLock)
                {
                    clientPhotoMesh = null;
                    clientDeveloperOverlayMesh = null;
                }
                clientRenderSignature = sig;
                MarkDirty(true);
                return;
            }

            // Re-tesselate the tray shape but replace the "plate" texture with the photo.
            MeshData? photoMesh = null;
            bool builtPhotoMesh = false;
            if (showPhoto && PhotoPlateRenderUtil.TryGetPhotoBlockTexture(capi, plate, out TextureAtlasPosition photoTex, out float photoAspect, Pos))
            {
                try
                {
                    ITexPositionSource baseSource = capi.Tesselator.GetTextureSource(Block);
                    ITexPositionSource texSource = new PlatePhotoTextureSource(baseSource, photoTex);

                    var shape = Block?.Shape?.Clone();
                    if (shape != null)
                    {
                        // Only keep the plate element so we don't duplicate the tray walls.
                        shape.IgnoreElements = new[] { "base", "wall-n", "wall-s", "wall-e", "wall-w" };
                        capi.Tesselator.TesselateShape(
                            "collodion-devtray-platephoto",
                            Block?.Code ?? new AssetLocation("collodion", "developmenttray-red"),
                            shape,
                            out photoMesh,
                            texSource
                        );

                        StampUvByRotationCropped(photoMesh, photoTex, 270, photoAspect, PhotoTargetAspect);

                        // Nudge up slightly to avoid z-fighting with the base plate.
                        photoMesh.Translate(0f, 0.0006f, 0f);
                        builtPhotoMesh = true;
                    }
                }
                catch
                {
                    photoMesh = null;
                }
            }

            lock (clientMeshLock)
            {
                if (builtPhotoMesh)
                {
                    clientPhotoMesh = photoMesh;
                }
                else if (!showPhoto)
                {
                    clientPhotoMesh = null;
                }
            }

            // Optional: developer liquid overlay (local-only, only while RMB held).
            MeshData? devOverlayMesh = null;
            bool builtOverlayMesh = false;
            if (clientDeveloperOverlayActive)
            {
                if (TryGetOverlayTexture(capi, clientDeveloperOverlayAlpha, clientOverlayAction, out TextureAtlasPosition devTex))
                {
                    try
                    {
                        ITexPositionSource baseSource = capi.Tesselator.GetTextureSource(Block);
                        ITexPositionSource texSource = new PlatePhotoTextureSource(baseSource, devTex);

                        var shape = Block?.Shape?.Clone();
                        if (shape != null)
                        {
                            // Keep the overlay on the plate element, then scale it up slightly.
                            shape.IgnoreElements = new[] { "base", "wall-n", "wall-s", "wall-e", "wall-w" };
                            capi.Tesselator.TesselateShape(
                                "collodion-devtray-devoverlay",
                                Block?.Code ?? new AssetLocation("collodion", "developmenttray-red"),
                                shape,
                                out devOverlayMesh,
                                texSource
                            );

                            // Scale up around center so it extends past the plate edges.
                            devOverlayMesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), DeveloperOverlayScale, 1f, DeveloperOverlayScale);

                            // Put it above the photo mesh.
                            devOverlayMesh.Translate(0f, 0.0012f, 0f);

                            ForceTransparentPass(devOverlayMesh);
                            ApplyOverlayAlpha(devOverlayMesh, clientDeveloperOverlayAlpha);
                            builtOverlayMesh = true;
                        }
                    }
                    catch
                    {
                        devOverlayMesh = null;
                    }
                }
            }

            lock (clientMeshLock)
            {
                if (builtOverlayMesh)
                {
                    clientDeveloperOverlayMesh = devOverlayMesh;
                }
                else if (!clientDeveloperOverlayActive)
                {
                    clientDeveloperOverlayMesh = null;
                }
            }
            clientRenderSignature = sig;

            MarkDirty(true);
            try
            {
                capi.World.BlockAccessor.MarkBlockDirty(Pos);
            }
            catch { }
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
                if (!isDeveloper && !isFixer) return false;

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
                    durationMs = (int)Math.Round((isFixer ? GetFixerPourSeconds(capi) : GetDeveloperPourSeconds(capi)) * 1000f);
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
            catch
            {
                return false;
            }
        }

        private static float GetFixerPourSeconds(ICoreClientAPI capi)
        {
            float seconds = 1.25f;
            try
            {
                var modSys = capi.ModLoader.GetModSystem<CollodionModSystem>();
                seconds = modSys?.Config?.DevelopmentTrayInteractions?.Fixer?.DurationSeconds ?? seconds;
            }
            catch { }

            return seconds < 0.05f ? 0.05f : seconds;
        }

        private static float GetDeveloperPourSeconds(ICoreClientAPI capi)
        {
            float seconds = 2.00f;
            try
            {
                var modSys = capi.ModLoader.GetModSystem<CollodionModSystem>();
                seconds = modSys?.Config?.DevelopmentTrayInteractions?.Developer?.DurationSeconds ?? seconds;
            }
            catch { }

            return seconds < 0.05f ? 0.05f : seconds;
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
            AssetLocation baseAtlasKey = isFixer ? FixerOverlayAtlasKey : DeveloperOverlayAtlasKey;
            AssetLocation baseAsset = isFixer ? FixerOverlayTextureAsset : DeveloperOverlayTextureAsset;

            AssetLocation atlasKey = alphaStep == AlphaSteps
                ? baseAtlasKey
                : new AssetLocation("collodion", (isFixer ? "devtray-fixer-overlay" : "devtray-developer-overlay") + $"-a{alphaStep}");

            try
            {
                // We intentionally insert into the *block* atlas, because this overlay is rendered as terrain mesh.
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
                                // Terrain transparency is primarily driven by per-texture alpha.
                                // Multiply the source alpha so we don't require a bespoke semi-transparent PNG.
                                // (MulAlpha is safe even if the PNG is fully opaque.)
                                try { bmp?.MulAlpha(effectiveAlpha); } catch { }
                                return bmp;
                            }
                        }
                        catch { }

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

        private static void StampUvByRotationCropped(MeshData mesh, TextureAtlasPosition texPos, int rotationDeg, float sourceAspect, float targetAspect)
        {
            if (mesh?.Uv == null || mesh.VerticesCount < 4) return;

            GetCroppedTexRect(texPos, sourceAspect, targetAspect, rotationDeg, out float x1, out float x2, out float y1, out float y2);

            int rot = ((rotationDeg % 360) + 360) % 360;
            int quadCount = mesh.VerticesCount / 4;
            for (int q = 0; q < quadCount; q++)
            {
                int v0 = (q * 4 + 0) * 2;
                int v1 = (q * 4 + 1) * 2;
                int v2 = (q * 4 + 2) * 2;
                int v3 = (q * 4 + 3) * 2;
                if (v3 + 1 >= mesh.Uv.Length) break;

                switch (rot)
                {
                    case 90:
                        mesh.Uv[v0] = x2; mesh.Uv[v0 + 1] = y2;
                        mesh.Uv[v1] = x2; mesh.Uv[v1 + 1] = y1;
                        mesh.Uv[v2] = x1; mesh.Uv[v2 + 1] = y1;
                        mesh.Uv[v3] = x1; mesh.Uv[v3 + 1] = y2;
                        break;
                    case 180:
                        mesh.Uv[v0] = x2; mesh.Uv[v0 + 1] = y1;
                        mesh.Uv[v1] = x1; mesh.Uv[v1 + 1] = y1;
                        mesh.Uv[v2] = x1; mesh.Uv[v2 + 1] = y2;
                        mesh.Uv[v3] = x2; mesh.Uv[v3 + 1] = y2;
                        break;
                    case 270:
                        mesh.Uv[v0] = x1; mesh.Uv[v0 + 1] = y1;
                        mesh.Uv[v1] = x1; mesh.Uv[v1 + 1] = y2;
                        mesh.Uv[v2] = x2; mesh.Uv[v2 + 1] = y2;
                        mesh.Uv[v3] = x2; mesh.Uv[v3 + 1] = y1;
                        break;
                    default:
                        mesh.Uv[v0] = x1; mesh.Uv[v0 + 1] = y2;
                        mesh.Uv[v1] = x2; mesh.Uv[v1 + 1] = y2;
                        mesh.Uv[v2] = x2; mesh.Uv[v2 + 1] = y1;
                        mesh.Uv[v3] = x1; mesh.Uv[v3 + 1] = y1;
                        break;
                }
            }

            for (int i = 0; i < mesh.Rgba.Length; i++) mesh.Rgba[i] = 255;
        }

        private static void GetCroppedTexRect(TextureAtlasPosition texPos, float sourceAspect, float targetAspect, int rotationDeg, out float x1, out float x2, out float y1, out float y2)
        {
            x1 = texPos.x1;
            x2 = texPos.x2;
            y1 = texPos.y1;
            y2 = texPos.y2;

            if (sourceAspect <= 0 || targetAspect <= 0) return;

            int rot = ((rotationDeg % 360) + 360) % 360;
            bool rot90 = rot == 90 || rot == 270;

            float effectiveSourceAspect = rot90 ? (1f / sourceAspect) : sourceAspect;
            if (effectiveSourceAspect <= 0) return;

            if (effectiveSourceAspect > targetAspect)
            {
                float keep = targetAspect / effectiveSourceAspect;
                if (keep < 0f) keep = 0f;
                if (keep > 1f) keep = 1f;
                float trim = (1f - keep) * 0.5f;

                if (!rot90)
                {
                    float xr = x2 - x1;
                    x1 += xr * trim;
                    x2 -= xr * trim;
                }
                else
                {
                    float yr = y2 - y1;
                    y1 += yr * trim;
                    y2 -= yr * trim;
                }

                return;
            }

            float keep2 = effectiveSourceAspect / targetAspect;
            if (keep2 < 0f) keep2 = 0f;
            if (keep2 > 1f) keep2 = 1f;
            float trim2 = (1f - keep2) * 0.5f;

            if (!rot90)
            {
                float yr = y2 - y1;
                y1 += yr * trim2;
                y2 -= yr * trim2;
            }
            else
            {
                float xr = x2 - x1;
                x1 += xr * trim2;
                x2 -= xr * trim2;
            }
        }
    }
}

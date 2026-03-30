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
        private const byte DeveloperOverlayAlpha = 210;
        private const float DeveloperOverlayScale = 1.32f;
        private const float DeveloperOverlayAlphaStart = 1.0f;
        private const float DeveloperOverlayAlphaEnd = 0.35f;

        private static readonly AssetLocation DeveloperOverlayTextureAsset = new AssetLocation("collodion", "textures/block/liquid/developer.png");
        private static readonly AssetLocation DeveloperOverlayAtlasKey = new AssetLocation("collodion", "devtray-developer-overlay");
        private static readonly AssetLocation FixerOverlayTextureAsset = new AssetLocation("collodion", "textures/block/liquid/fixer.png");
        private static readonly AssetLocation FixerOverlayAtlasKey = new AssetLocation("collodion", "devtray-fixer-overlay");
        private static readonly AssetLocation WaterOverlayTextureAsset = new AssetLocation("survival", "textures/block/liquid/waterportion.png");
        private static readonly AssetLocation WaterOverlayAtlasKey = new AssetLocation("collodion", "devtray-water-overlay");

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
        private MeshData? clientTrayBodyMesh;
        private MeshData? clientFallbackTrayMesh; // body-only mesh built once at init; never nulled
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

            // Pre-build a tray body mesh (no plate element) so it is always available
            // during the threading race gap between a block-type change triggering chunk
            // retesselation and FromTreeAttributes finishing the full BuildClientMesh.
            // Without this the tray walls go blank for 1–2 frames while suppressing the
            // base block to prevent the sideways-plate flash.
            if (api is ICoreClientAPI capiInit && BlockTypeHasStaticPlate())
            {
                try
                {
                    ITexPositionSource bodySource = capiInit.Tesselator.GetTextureSource(Block);
                    var bodyShape = Block?.Shape?.Clone();
                    if (bodyShape != null)
                    {
                        bodyShape.IgnoreElements = new[] { "plate" };
                        capiInit.Tesselator.TesselateShape(
                            "collodion-devtray-body-fallback",
                            Block?.Code ?? new AssetLocation("collodion", "developmenttray-red"),
                            bodyShape,
                            out MeshData? fallbackMesh,
                            bodySource
                        );
                        lock (clientMeshLock)
                        {
                            clientFallbackTrayMesh = fallbackMesh;
                        }
                    }
                }
                catch (Exception ex)
                {
                    capiInit.Logger.Warning("[Collodion] ClientInitialize: body mesh tessellation failed: {0}", ex.Message);
                }
            }

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

                        try { capi.World.BlockAccessor.MarkBlockDirty(Pos); }
                        catch (Exception ex) { capi.Logger.Warning("[Collodion] overlay tick: MarkBlockDirty failed: {0}", ex.Message); }
                        return;
                    }

                    if (!clientDeveloperOverlayActive) return;

                    clientDeveloperOverlayActive = false;
                    clientDeveloperOverlayLastRebuildMs = 0;
                    clientOverlayAction = null;
                    clientNeedsRebuild = true;
                    RequestClientMeshRebuild();

                    try { capi.World.BlockAccessor.MarkBlockDirty(Pos); }
                    catch (Exception ex) { capi.Logger.Warning("[Collodion] overlay tick: MarkBlockDirty failed: {0}", ex.Message); }
                }, 50);
            }
            catch (Exception ex)
            {
                api.Logger.Warning("[Collodion] ClientInitialize: failed to register overlay tick listener: {0}", ex.Message);
            }
        }

        partial void ClientPlateChanged(bool markBlockDirty)
        {
            if (Api?.Side != EnumAppSide.Client) return;
            if (Api is not ICoreClientAPI capi) return;

            lock (clientMeshLock)
            {
                if (!clientDeveloperOverlayActive)
                    clientDeveloperOverlayMesh = null;
            }

            // Build ALL client meshes (body + plate/photo) fully and synchronously on the
            // main thread right now, before MarkDirty causes any tesselation. This ensures
            // the tesselation thread always sees both clientTrayBodyMesh and clientPhotoMesh
            // populated from the very first frame — no flash of the base block shape or
            // a missing plate while the async rebuild catches up.
            clientNeedsRebuild = false;
            clientRenderSignature = null;
            BuildClientMesh(capi);
            // BuildClientMesh calls MarkDirty(true) internally — no need to call it here.
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

            MeshData? trayBodyMesh;
            MeshData? photoMesh;
            MeshData? overlayMesh;
            lock (clientMeshLock)
            {
                trayBodyMesh = clientTrayBodyMesh;
                photoMesh = clientPhotoMesh;
                overlayMesh = clientDeveloperOverlayMesh;
            }

            // When we have a tray body mesh we've taken full ownership of rendering:
            // add our custom meshes and return true to suppress the base block mesh.
            // clientTrayBodyMesh is built synchronously in ClientPlateChanged so it is
            // always ready by the time MarkBlockDirty triggers this method.
            if (trayBodyMesh != null)
            {
                mesher.AddMeshData(trayBodyMesh.Clone());
                if (photoMesh != null)
                {
                    mesher.AddMeshData(photoMesh.Clone());
                }
                if (overlayMesh != null)
                {
                    mesher.AddMeshData(overlayMesh.Clone());
                }
                return true;
            }

            // clientTrayBodyMesh is null — our sync build hasn't completed yet (a genuine
            // threading race: VS queues a retesselation the moment the block type changes,
            // which can fire before FromTreeAttributes finishes on the main thread).
            //
            // If the block code tells us a plate *must* be present (any loaded stage),
            // suppress the base block entirely and request a rebuild immediately.
            // This gives a blank tray for at most one or two frames — far less noticeable
            // than the static plate element snapping from sideways to upright.
            if (BlockTypeHasStaticPlate())
            {
                RequestClientMeshRebuild();
                // Render the pre-built fallback body so the tray walls stay visible
                // during the brief gap before the full rebuild completes.
                MeshData? fallback;
                lock (clientMeshLock) { fallback = clientFallbackTrayMesh; }
                if (fallback != null) mesher.AddMeshData(fallback.Clone());
                return true;
            }

            // No plate loaded — fall back to normal (base) block rendering.
            return base.OnTesselation(mesher, tessThreadTesselator);
        }

        /// <summary>
        /// Returns true when this block type's JSON shape contains a static plate element
        /// (i.e. any stage that has a plate in it). Read from Block.Code which is immutable
        /// and safe to access from the tesselation thread without locks.
        /// </summary>
        private bool BlockTypeHasStaticPlate()
        {
            string path = Block?.Code?.Path ?? string.Empty;
            return path.Contains("-exposed", StringComparison.Ordinal)
                || path.Contains("-developed", StringComparison.Ordinal)
                || path.Contains("-finished", StringComparison.Ordinal);
        }

    }
}

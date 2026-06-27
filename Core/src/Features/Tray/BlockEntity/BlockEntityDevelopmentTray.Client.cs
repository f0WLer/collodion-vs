using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
namespace Photocore.Tray
{
    public sealed partial class BlockEntityDevelopmentTray
    {
        private sealed class PlatePhotoTextureSource : ITexPositionSource
        {
            private readonly ITexPositionSource _baseSource;
            private readonly TextureAtlasPosition _photoTex;

            public PlatePhotoTextureSource(ITexPositionSource baseSource, TextureAtlasPosition photoTex)
            {
                _baseSource = baseSource;
                _photoTex = photoTex;
            }

            public TextureAtlasPosition this[string textureCode] => string.Equals(textureCode, "plate", StringComparison.OrdinalIgnoreCase) ? _photoTex : _baseSource[textureCode]!;

            public Size2i AtlasSize => _baseSource.AtlasSize!;
        }

        private readonly object _clientMeshLock = new();
        private MeshData? _clientTrayBodyMesh;
        private MeshData? _clientFallbackTrayMesh; // body-only mesh built once at init; never nulled
        private MeshData? _clientPhotoMesh;
        private MeshData? _clientDeveloperOverlayMesh;
        private bool _clientNeedsRebuild;
        private string? _clientRenderSignature;
        private bool _clientDeveloperOverlayActive;
        private float _clientDeveloperOverlayAlpha = 1.0f;
        private long _clientDeveloperOverlayLastRebuildMs;
        private string? _clientOverlayAction;

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
                        bodyShape.IgnoreElements = ["plate"];
                        capiInit.Tesselator.TesselateShape(
                            "photocore-devtray-body-fallback",
                            Block?.Code ?? new AssetLocation("photocore", "developmenttray-red"),
                            bodyShape,
                            out MeshData? fallbackMesh,
                            bodySource
                        );
                        lock (_clientMeshLock)
                        {
                            _clientFallbackTrayMesh = fallbackMesh;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(capiInit.Logger, "ClientInitialize: body mesh tessellation failed: {0}", ex.Message);
                }
            }

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
                        bool actionChanged = !string.Equals(action, _clientOverlayAction, StringComparison.Ordinal);
                        bool alphaChanged = Math.Abs(alpha - _clientDeveloperOverlayAlpha) > 0.001f;
                        bool stale = nowMs - _clientDeveloperOverlayLastRebuildMs > 200;
                        if (_clientDeveloperOverlayActive && !alphaChanged && !stale && !actionChanged) return;

                        _clientDeveloperOverlayActive = true;
                        _clientDeveloperOverlayAlpha = alpha;
                        _clientDeveloperOverlayLastRebuildMs = nowMs;
                        _clientOverlayAction = action;
                        _clientNeedsRebuild = true;
                        RequestClientMeshRebuild();

                        try { capi.World.BlockAccessor.MarkBlockDirty(Pos); }
                        catch (Exception ex) { Log.Debug(capi.Logger, "overlay tick: MarkBlockDirty failed: {0}", ex.Message); }
                        return;
                    }

                    if (!_clientDeveloperOverlayActive) return;

                    _clientDeveloperOverlayActive = false;
                    _clientDeveloperOverlayLastRebuildMs = 0;
                    _clientOverlayAction = null;
                    _clientNeedsRebuild = true;
                    RequestClientMeshRebuild();

                    try { capi.World.BlockAccessor.MarkBlockDirty(Pos); }
                    catch (Exception ex) { Log.Debug(capi.Logger, "overlay tick: MarkBlockDirty failed: {0}", ex.Message); }
                }, 50);
            }
            catch (Exception ex)
            {
                Log.Warn(api.Logger, "ClientInitialize: failed to register overlay tick listener: {0}", ex.Message);
            }
        }

        partial void ClientPlateChanged(bool markBlockDirty)
        {
            if (Api?.Side != EnumAppSide.Client) return;
            if (Api is not ICoreClientAPI capi) return;

            lock (_clientMeshLock)
            {
                if (!_clientDeveloperOverlayActive)
                    _clientDeveloperOverlayMesh = null;
            }

            // Build ALL client meshes (body + plate/photo) fully and synchronously on the
            // main thread right now, before MarkDirty causes any tesselation. This ensures
            // the tesselation thread always sees both clientTrayBodyMesh and clientPhotoMesh
            // populated from the very first frame - no flash of the base block shape or
            // a missing plate while the async rebuild catches up.
            _clientNeedsRebuild = false;
            _clientRenderSignature = null;
            BuildClientMesh(capi);
            // BuildClientMesh calls MarkDirty(true) internally - no need to call it here.
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
                lock (_plateLock)
                {
                    sig = ComputePlateSignature(PlateStack);
                }
            }
            catch
            {
                sig = null;
            }

            if (!string.Equals(_clientRenderSignature, sig, StringComparison.Ordinal))
            {
                _clientNeedsRebuild = true;
            }

            if (_clientNeedsRebuild)
            {
                _clientNeedsRebuild = false;
                RequestClientMeshRebuild();
            }

            MeshData? trayBodyMesh;
            MeshData? photoMesh;
            MeshData? overlayMesh;
            lock (_clientMeshLock)
            {
                trayBodyMesh = _clientTrayBodyMesh;
                photoMesh = _clientPhotoMesh;
                overlayMesh = _clientDeveloperOverlayMesh;
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
                lock (_clientMeshLock) { fallback = _clientFallbackTrayMesh; }
                if (fallback != null) mesher.AddMeshData(fallback.Clone());
                return true;
            }

            return base.OnTesselation(mesher, tessThreadTesselator);
        }

        private bool BlockTypeHasStaticPlate()
        {
            string path = Block?.Code?.Path ?? string.Empty;
            return path.Contains("-exposed", StringComparison.Ordinal)
                || path.Contains("-developing", StringComparison.Ordinal)
                || path.Contains("-developed", StringComparison.Ordinal)
                || path.Contains("-finished", StringComparison.Ordinal);
        }

    }
}


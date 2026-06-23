using Photochemistry.Plates.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Photochemistry.Plates;
namespace Photochemistry.Tray
{
    public sealed partial class BlockEntityDevelopmentTray
    {
        private bool _clientMeshQueued;

        // Queues exactly one main-thread tray mesh rebuild even if multiple dirty signals arrive in quick succession.
        private void RequestClientMeshRebuild()
        {
            if (Api?.Side != EnumAppSide.Client) return;

            ICoreClientAPI capi = (ICoreClientAPI)Api;
            lock (_clientMeshLock)
            {
                if (_clientMeshQueued) return;
                _clientMeshQueued = true;
            }

            try
            {
                capi.Event.EnqueueMainThreadTask(() =>
                {
                    lock (_clientMeshLock) _clientMeshQueued = false;
                    BuildClientMesh(capi);
                }, "photochemistry-devtray-rebuild");
            }
            catch (Exception ex)
            {
                Log.Debug(capi.Logger, "RequestClientMeshRebuild enqueue failed: {0}", ex.Message);
                lock (_clientMeshLock) _clientMeshQueued = false;
            }
        }

        private void BuildClientMesh(ICoreClientAPI capi)
        {
            if (capi == null) return;

            ItemStack? plate;
            string? sig;
            lock (_plateLock)
            {
                plate = PlateStack?.Clone();
                sig = ComputePlateSignature(PlateStack);
            }

            if (plate?.Collectible?.Code == null)
            {
                lock (_clientMeshLock)
                {
                    _clientTrayBodyMesh = null;
                    _clientPhotoMesh = null;
                    _clientDeveloperOverlayMesh = null;
                }
                _clientRenderSignature = sig;
                MarkDirty(true);
                return;
            }

            bool builtTrayBody = TryBuildTrayBodyMesh(capi, out MeshData? trayBodyMesh);
            bool builtPhotoMesh = TryBuildPlateMesh(capi, plate, out MeshData? photoMesh);

            lock (_clientMeshLock)
            {
                if (builtTrayBody) _clientTrayBodyMesh = trayBodyMesh;
                _clientPhotoMesh = builtPhotoMesh ? photoMesh : null;
            }

            bool builtOverlayMesh = TryBuildPourOverlayMesh(capi, out MeshData? devOverlayMesh);

            lock (_clientMeshLock)
            {
                if (builtOverlayMesh)
                    _clientDeveloperOverlayMesh = devOverlayMesh;
                else if (!_clientDeveloperOverlayActive)
                    _clientDeveloperOverlayMesh = null;
            }

            _clientRenderSignature = sig;
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

                bodyShape.IgnoreElements = ["plate"];
                capi.Tesselator.TesselateShape(
                    "photochemistry-devtray-body",
                    Block?.Code ?? new AssetLocation("photochemistry", "developmenttray-red"),
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
            PlateStage plateStage = PlateAttributes.GetStage(plate);
            bool showPhoto = plate.Attributes != null
                && (plateStage == PlateStage.Developing || plateStage == PlateStage.Developed || plateStage == PlateStage.Finished);

            // Physical plate element — always rendered while a plate sits in the tray, in the
            // transparent pass so the plate texture's baked alpha shows as translucent glass.
            try
            {
                ITexPositionSource baseSource = capi.Tesselator.GetTextureSource(Block);

                // Paper plates carry their own opaque sheet texture for every stage (via the plate item's
                // own "plate" texture, e.g. kosphotography:item/paper-sensitized / paper-developed), so the
                // tray body reads as paper rather than the glass texture the tray block carries.
                // Glass plates keep using the tray block's texture, swapping to plate-developed during the
                // Developing pours (the tray stays "-exposed" because there is no "-developing" block).
                bool isPaper = PhotoPlateRenderUtil.IsPaperMedium(plate);
                ITexPositionSource plateSource = baseSource;
                if (isPaper && TryGetPlatePaperTexture(capi, plate, out TextureAtlasPosition paperTex))
                {
                    plateSource = new PlatePhotoTextureSource(baseSource, paperTex);
                }
                else if (plateStage == PlateStage.Developing
                    && TryGetItemPlateTexture(capi, "plate-developed", out TextureAtlasPosition devTex))
                {
                    plateSource = new PlatePhotoTextureSource(baseSource, devTex);
                }

                var shape = Block?.Shape?.Clone();
                if (shape == null) return false;

                shape.IgnoreElements = ["base", "wall-n", "wall-s", "wall-e", "wall-w"];
                capi.Tesselator.TesselateShape(
                    "photochemistry-devtray-plainplate",
                    Block?.Code ?? new AssetLocation("photochemistry", "developmenttray-red"),
                    shape,
                    out mesh,
                    plateSource
                );
                if (mesh == null) return false;

                int placementYawDeg = GetPlacementFacingYawDeg();
                if (placementYawDeg != 0)
                    mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, placementYawDeg * GameMath.DEG2RAD, 0f);

                mesh.Translate(0f, 0.0006f, 0f);

                // Glass plate: alpha-blend so the texture's baked translucency is visible (block
                // faces otherwise render in the opaque pass, which ignores partial alpha). Paper is a
                // solid opaque sheet and stays in the opaque pass.
                if (!isPaper)
                    PhotoMeshUtil.SetTransparentRenderPass(mesh);
            }
            catch
            {
                mesh = null;
                return false;
            }

            // During/after development, layer the derived silver image slightly above the plate
            // (not replacing it), matching the held item's plate+overlay look.
            if (showPhoto && PhotoPlateRenderUtil.TryGetPhotoBlockTexture(capi, plate, out TextureAtlasPosition photoTex, out float photoAspect, Pos))
            {
                try
                {
                    ITexPositionSource baseSource = capi.Tesselator.GetTextureSource(Block);
                    ITexPositionSource texSource = new PlatePhotoTextureSource(baseSource, photoTex);

                    var shape = Block?.Shape?.Clone();
                    if (shape == null) return true;

                    shape.IgnoreElements = ["base", "wall-n", "wall-s", "wall-e", "wall-w"];
                    capi.Tesselator.TesselateShape(
                        "photochemistry-devtray-platephoto",
                        Block?.Code ?? new AssetLocation("photochemistry", "developmenttray-red"),
                        shape,
                        out MeshData? photoMesh,
                        texSource
                    );
                    if (photoMesh == null) return true;

                    PhotoMeshUtil.StampUvByRotationCropped(photoMesh, photoTex, 0, photoAspect, PhotoMeshUtil.PhotoTargetAspect);

                    int placementYawDeg = GetPlacementFacingYawDeg();
                    if (placementYawDeg != 0)
                        photoMesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, placementYawDeg * GameMath.DEG2RAD, 0f);

                    // One offset step above the plate's up face (the pour liquid sits above both).
                    photoMesh.Translate(0f, 0.0012f, 0f);
                    // Glass silver image alpha-blends over the plate; a paper positive is opaque.
                    if (!PhotoPlateRenderUtil.IsPaperMedium(plate))
                        PhotoMeshUtil.SetTransparentRenderPass(photoMesh);

                    // Pad the plate's per-quad passes with -1 (block default) so the overlay's
                    // Transparent entries stay aligned to the overlay quads after the merge.
                    int plateQuads = mesh.VerticesCount / 4;
                    while (mesh.RenderPassCount < plateQuads) mesh.AddRenderPass(-1);
                    mesh.AddMeshData(photoMesh);
                }
                catch
                {
                    // Best-effort overlay: the plain plate still renders without it.
                }
            }

            return true;
        }

        // Glass-plate convenience overload: loads photochemistry:textures/item/<name>.png into the block
        // atlas (used for the Developing stage, where the tray block is still "-exposed").
        private static bool TryGetItemPlateTexture(ICoreClientAPI capi, string textureName, out TextureAtlasPosition texPos)
            => TryGetPlateTextureFromAsset(
                capi,
                new AssetLocation("photochemistry", "devtray-" + textureName),
                new AssetLocation("photochemistry", "textures/item/" + textureName + ".png"),
                out texPos);

        // Resolves the plate item's own "plate" texture (e.g. kosphotography:item/paper-sensitized or
        // paper-developed) so the tray body renders the correct opaque paper sheet for the current stage,
        // regardless of which asset domain the texture lives in.
        private static bool TryGetPlatePaperTexture(ICoreClientAPI capi, ItemStack plate, out TextureAtlasPosition texPos)
        {
            texPos = capi.BlockTextureAtlas.UnknownTexturePosition;
            if (plate.Collectible is not Item item || item.Textures == null) return false;
            if (!item.Textures.TryGetValue("plate", out CompositeTexture? ct) || ct?.Base == null) return false;

            AssetLocation baseLoc = ct.Base;
            var atlasKey = new AssetLocation(baseLoc.Domain, "devtray-plate-" + baseLoc.Path.Replace('/', '-'));
            var asset = new AssetLocation(baseLoc.Domain, "textures/" + baseLoc.Path + ".png");
            return TryGetPlateTextureFromAsset(capi, atlasKey, asset, out texPos);
        }

        // Inserts (once per atlas key) and returns a texture in the block atlas, loading its PNG from the
        // given asset path, so the tray can render a plate texture the current tray block type doesn't carry.
        private static bool TryGetPlateTextureFromAsset(ICoreClientAPI capi, AssetLocation atlasKey, AssetLocation asset, out TextureAtlasPosition texPos)
        {
            texPos = capi.BlockTextureAtlas.UnknownTexturePosition;
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
                            var a = capi.Assets.TryGet(asset);
                            return a != null ? capi.Render.BitmapCreateFromPng(a.Data) : null;
                        }
                        catch { return null; }
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

        private int GetPlacementFacingYawDeg()
        {
            return PlacementFacingCode switch
            {
                "south" => 90,
                "west" => 0,
                "north" => 270,
                _ => 180
            };
        }
    }
}

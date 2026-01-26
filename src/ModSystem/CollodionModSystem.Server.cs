using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Collodion
{
    public partial class CollodionModSystem
    {
        public override void StartServerSide(ICoreServerAPI api)
        {
            ServerChannel = api.Network.GetChannel("collodion");
            ServerChannel.SetMessageHandler<PhotoTakenPacket>(OnPhotoTakenReceived);
            ServerChannel.SetMessageHandler<CameraLoadPlatePacket>(OnCameraLoadPlateReceived);
            PhotoSync = new WetplatePhotoSync(this);

            serverPhotoLastSeenIndex = LoadOrCreateServerPhotoLastSeenIndex(api);
            serverPhotoLastSeenDirty = false;
            serverPhotoLastSeenFlushListenerId = api.Event.RegisterGameTickListener(_ => ServerMaybeFlushPhotoLastSeenIndex(api), 10_000);

            ServerChannel
                .SetMessageHandler<PhotoBlobRequestPacket>((player, p) => PhotoSync?.ServerHandleRequest(player, p))
                .SetMessageHandler<PhotoBlobChunkPacket>((player, p) => PhotoSync?.ServerHandleChunk(player, p))
                .SetMessageHandler<PhotoCaptionSetPacket>((player, p) => OnPhotoCaptionSet(player, p))
                .SetMessageHandler<PhotoSeenPacket>((player, p) => OnPhotoSeen(player, p));
        }

        private static PhotoLastSeenIndex LoadOrCreateServerPhotoLastSeenIndex(ICoreServerAPI sapi)
        {
            PhotoLastSeenIndex? idx = null;
            try
            {
                idx = sapi.LoadModConfig<PhotoLastSeenIndex>(ServerPhotoIndexFileName);
            }
            catch
            {
                idx = null;
            }

            if (idx == null)
            {
                idx = new PhotoLastSeenIndex();
                try
                {
                    idx.ClampInPlace();
                    sapi.StoreModConfig(idx, ServerPhotoIndexFileName);
                }
                catch
                {
                    // ignore
                }
            }

            idx.ClampInPlace();
            return idx;
        }

        internal void ServerTouchPhotoSeen(string photoId)
        {
            if (serverPhotoLastSeenIndex == null) return;

            serverPhotoLastSeenIndex.Touch(photoId);
            serverPhotoLastSeenDirty = true;
        }

        private void ServerMaybeFlushPhotoLastSeenIndex(ICoreServerAPI sapi)
        {
            if (!serverPhotoLastSeenDirty) return;
            if (serverPhotoLastSeenIndex == null) return;

            try
            {
                serverPhotoLastSeenIndex.ClampInPlace();
                sapi.StoreModConfig(serverPhotoLastSeenIndex, ServerPhotoIndexFileName);
                serverPhotoLastSeenDirty = false;
            }
            catch
            {
                // Keep dirty so we retry.
                serverPhotoLastSeenDirty = true;
            }
        }

        private void OnPhotoSeen(IServerPlayer player, PhotoSeenPacket packet)
        {
            if (packet == null) return;
            ServerTouchPhotoSeen(packet.PhotoId);
        }

        private static readonly AssetLocation SilveredPlateItemCode = new AssetLocation("collodion", "silveredplate");
        private static readonly AssetLocation ExposedPlateItemCode = new AssetLocation("collodion", "exposedplate");

        private void OnCameraLoadPlateReceived(IServerPlayer player, CameraLoadPlatePacket packet)
        {
            if (Api == null) return;

            ItemSlot? cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            ItemStack? cameraStack = cameraSlot?.Itemstack;
            if (cameraStack?.Item is not ItemWetplateCamera) return;
            if (cameraSlot == null) return;

            bool wantLoad = packet?.Load != false;

            ItemSlot? offhandSlot = player.InventoryManager.OffhandHotbarSlot;
            ItemStack? offhandStack = offhandSlot?.Itemstack;

            if (wantLoad)
            {
                // Only load if currently empty.
                string alreadyLoaded = cameraStack.Attributes.GetString(ItemWetplateCamera.AttrLoadedPlate, string.Empty);
                if (!string.IsNullOrEmpty(alreadyLoaded)) return;

                if (offhandStack?.Collectible?.Code == null) return;

                AssetLocation code = offhandStack.Collectible.Code;
                bool isSupportedPlate = code == SilveredPlateItemCode || code == ExposedPlateItemCode;
                if (!isSupportedPlate) return;

                // Store the plate stack (including any attributes) inside the camera so we can unload it later.
                // IMPORTANT: clone so we don't retain a reference that could be mutated elsewhere.
                try
                {
                    cameraStack.Attributes.SetItemstack(ItemWetplateCamera.AttrLoadedPlateStack, offhandStack.Clone());
                }
                catch
                {
                    // If we can't store state, don't consume the plate.
                    return;
                }

                if (offhandSlot == null) return;
                offhandSlot.TakeOut(1);
                offhandSlot.MarkDirty();

                cameraStack.Attributes.SetString(ItemWetplateCamera.AttrLoadedPlate, code.ToString());
                cameraSlot.MarkDirty();
                return;
            }

            // Unload
            string loaded = cameraStack.Attributes.GetString(ItemWetplateCamera.AttrLoadedPlate, string.Empty);
            if (string.IsNullOrEmpty(loaded)) return;

            // Only unload when offhand is empty.
            if (offhandSlot == null || !offhandSlot.Empty) return;

            // Restore the stored plate stack if present (preferred).
            ItemStack? stored = null;
            try
            {
                stored = cameraStack.Attributes.GetItemstack(ItemWetplateCamera.AttrLoadedPlateStack, null);
                stored?.ResolveBlockOrItem(Api.World);
            }
            catch
            {
                stored = null;
            }

            if (stored == null)
            {
                // Fallback: reconstruct from the stored code string.
                AssetLocation loadedLoc;
                try
                {
                    loadedLoc = new AssetLocation(loaded);
                }
                catch
                {
                    return;
                }

                if (!(loadedLoc == SilveredPlateItemCode || loadedLoc == ExposedPlateItemCode))
                {
                    return;
                }

                Item? plateItem = Api.World.GetItem(loadedLoc);
                if (plateItem == null) return;
                stored = new ItemStack(plateItem);
            }

            stored.StackSize = 1;

            // Place into offhand slot.
            offhandSlot.Itemstack = stored;
            offhandSlot.MarkDirty();

            // Clear the camera state.
            cameraStack.Attributes.RemoveAttribute(ItemWetplateCamera.AttrLoadedPlate);
            cameraStack.Attributes.RemoveAttribute(ItemWetplateCamera.AttrLoadedPlateStack);
            cameraSlot.MarkDirty();
        }

        private void OnPhotoCaptionSet(IServerPlayer player, PhotoCaptionSetPacket packet)
        {
            if (Api == null || packet == null) return;

            var pos = new Vintagestory.API.MathTools.BlockPos(packet.X, packet.Y, packet.Z);
            var be = Api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityPhotograph;
            if (be == null) return;

            // Basic sanity limit to avoid abuse.
            string caption = packet.Caption ?? string.Empty;
            if (caption.Length > 200) caption = caption.Substring(0, 200);

            be.SetCaption(caption);

            if (!string.IsNullOrEmpty(be.PhotoId))
            {
                ServerTouchPhotoSeen(be.PhotoId);
            }

            // Force a block entity update/sync.
            try
            {
                Api.World.BlockAccessor.MarkBlockEntityDirty(pos);
            }
            catch { }
        }

        private void OnPhotoTakenReceived(IServerPlayer player, PhotoTakenPacket packet)
        {
            if (Api == null || packet == null) return;

            // Verify player is holding camera to prevent cheating
            ItemSlot? cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            ItemStack? cameraStack = cameraSlot?.Itemstack;
            if (cameraStack?.Item is not ItemWetplateCamera) return;
            if (cameraSlot == null) return;

            // Only allow exposure when a silvered plate is loaded.
            string loaded = cameraStack.Attributes.GetString(ItemWetplateCamera.AttrLoadedPlate, string.Empty);
            if (!string.Equals(loaded, SilveredPlateItemCode.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Pull the stored plate stack (preferred) so we preserve wetness attributes.
            ItemStack? stored = null;
            try
            {
                stored = cameraStack.Attributes.GetItemstack(ItemWetplateCamera.AttrLoadedPlateStack, null);
                stored?.ResolveBlockOrItem(Api.World);
            }
            catch
            {
                stored = null;
            }

            if (stored == null || stored.Item is not ItemSilveredPlate)
            {
                // If we can't prove a silvered plate was loaded, don't create an exposure.
                return;
            }

            Item? exposedItem = Api.World.GetItem(ExposedPlateItemCode);
            if (exposedItem == null) return;

            var exposedStack = new ItemStack(exposedItem);
            try
            {
                // Copy all existing plate attrs (wetness timer etc.)
                exposedStack.Attributes.MergeTree(stored.Attributes.Clone());
            }
            catch
            {
                // Best-effort; continue with just photo metadata.
            }

            // Attach photo metadata to the plate.
            string photoId = packet.PhotoId ?? string.Empty;
            exposedStack.Attributes.SetString(WetPlateAttrs.PhotoId, photoId);
            exposedStack.Attributes.SetString("timestamp", DateTime.Now.ToString());
            exposedStack.Attributes.SetString("photographer", player.PlayerName);
            exposedStack.Attributes.SetString(WetPlateAttrs.PlateStage, "exposed");

            ServerTouchPhotoSeen(photoId);

            // Keep the exposed (now photo-bearing) plate loaded in the camera.
            cameraStack.Attributes.SetItemstack(ItemWetplateCamera.AttrLoadedPlateStack, exposedStack);
            cameraStack.Attributes.SetString(ItemWetplateCamera.AttrLoadedPlate, ExposedPlateItemCode.ToString());
            cameraSlot?.MarkDirty();
        }
    }
}

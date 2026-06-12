using Collodion.Plates;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Collodion.FieldCamera
{
    internal sealed partial class FieldCameraModSystemBridge
    {
        internal bool TryHandleMountedCameraBlockInteract(IWorldAccessor world, BlockPos pos, IPlayer? byPlayer, bool shiftDown, bool ctrlDown)
        {
            if (Api?.Side != EnumAppSide.Server || Api.World == null) return false;
            if (byPlayer is not IServerPlayer serverPlayer) return false;
            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityMountedCamera mountedBe) return false;
            ItemStack? cameraStack = mountedBe.GetStoredCameraStack(Api.World);
            if (cameraStack == null || !IsFieldcameraStack(cameraStack)) return false;

            // Allow interaction only if no active exposure is locked to another photographer.
            string? photographerUid = null;
            if (CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? checkPlate) && checkPlate != null)
            {
                PlateStage checkStage = PlateAttributes.GetStage(checkPlate);
                if (checkStage is PlateStage.Exposing or PlateStage.ExposurePaused)
                {
                    photographerUid = checkPlate.Attributes.GetString(PlateAttributes.PhotographerUid);
                }
            }

            if (!string.IsNullOrEmpty(photographerUid)
                && !string.Equals(photographerUid, serverPlayer.PlayerUID, StringComparison.Ordinal))
            {
                serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, "Collodion: someone else's exposure is in progress.", EnumChatType.Notification);
                return true;
            }

            // No active exposure — if a different player is taking over, transfer block ownership.
            if (!string.Equals(mountedBe.OwnerPlayerUid, serverPlayer.PlayerUID, StringComparison.Ordinal))
            {
                ForgetMountedCameraPos(mountedBe.OwnerPlayerUid);
                mountedBe.TransferOwnership(serverPlayer.PlayerUID);
            }

            // Shift+Ctrl+RMB: recover camera to player, pausing any active exposure first.
            if (shiftDown && ctrlDown)
            {
                _ = PauseMountedCameraStorage(cameraStack);
                mountedBe.MarkCameraDirty();
                SendMountedCameraControl(serverPlayer, false, false);
                string oldOwnerUid = mountedBe.OwnerPlayerUid;
                ItemStack? recovered = mountedBe.TakeStoredCameraStack(Api.World);
                if (recovered == null) { return false; }
                ClearMountedCameraPos(recovered);
                ForgetMountedCameraPos(oldOwnerUid);
                ForgetMountedCameraPos(serverPlayer.PlayerUID);
                TryGiveOrSpawnMountedCamera(world, serverPlayer, pos, recovered);
                world.BlockAccessor.SetBlock(0, pos);
                world.BlockAccessor.RemoveBlockEntity(pos);
                return true;
            }

            // Shift+RMB: unload plate, but refuse if exposure is currently active.
            if (shiftDown)
            {
                if (!CameraHasLoadedPlate(cameraStack))
                {
                    serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, "Collodion: no plate is loaded.", EnumChatType.Notification);
                    return true;
                }

                if (CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? stagePlate) && stagePlate != null
                    && PlateAttributes.GetStage(stagePlate) == PlateStage.Exposing)
                {
                    serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, "Collodion: pause the exposure first (RMB) before unloading.", EnumChatType.Notification);
                    return true;
                }

                if (TryHandleMountedBlockPlateUnload(serverPlayer, mountedBe, cameraStack))
                {
                    RememberMountedCameraPos(serverPlayer.PlayerUID, pos);
                    SendMountedCameraControl(serverPlayer, false, true, cameraStack);
                }
                return true;
            }

            RememberMountedCameraPos(serverPlayer.PlayerUID, pos);

            if (!CameraHasLoadedPlate(cameraStack))
            {
                if (TryHandleMountedBlockPlateLoad(serverPlayer, mountedBe, cameraStack))
                {
                    SendMountedCameraControl(serverPlayer, false, true, cameraStack);
                    return true;
                }
                serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, "Collodion: hold a sensitized plate and right-click to load.", EnumChatType.Notification);
                return true;
            }

            if (PauseMountedCameraStorage(cameraStack))
            {
                mountedBe.MarkCameraDirty();
                SendMountedCameraControl(serverPlayer, false, true, cameraStack);
                return true;
            }

            if (ResumeMountedCameraStorage(cameraStack, serverPlayer.PlayerUID))
            {
                mountedBe.MarkCameraDirty();
                SendMountedCameraControl(serverPlayer, true, true, cameraStack);
                return true;
            }

            return true;
        }

        private bool TryHandleMountedBlockPlateLoad(IServerPlayer player, BlockEntityMountedCamera mountedBe, ItemStack cameraStack)
        {
            if (Api?.World == null) return false;
            ItemSlot? activeSlot = player.InventoryManager.ActiveHotbarSlot;
            if (activeSlot == null || activeSlot.Empty) return false;
            if (CameraHasLoadedPlate(cameraStack)) return false;
            if (!CameraEligibility.CanLoadIntoCamera(activeSlot.Itemstack)) return false;

            ItemStack loadedPlate = activeSlot.Itemstack.Clone();
            loadedPlate.StackSize = 1;

            SetLoadedPlateAttributes(cameraStack, loadedPlate);
            mountedBe.MarkCameraDirty();

            _ = activeSlot.TakeOut(1);
            activeSlot.MarkDirty();

            AudioUtils.FireAndForgetEntitySound(Api?.World, _cameraPlateLoadSound, player.Entity, AudioUtils.NextRandomPitch(Api?.World));
            return true;
        }

        // Caller must ensure the plate is not actively exposing before calling this.
        private bool TryHandleMountedBlockPlateUnload(IServerPlayer player, BlockEntityMountedCamera mountedBe, ItemStack cameraStack)
        {
            if (Api?.World == null) return false;
            if (!CameraHasLoadedPlate(cameraStack)) return false;

            if (!CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? loadedPlate) || loadedPlate == null)
            {
                ClearLoadedPlateAttributes(cameraStack);
                mountedBe.MarkCameraDirty();
                return true;
            }

            loadedPlate.StackSize = 1;
            ClearLoadedPlateAttributes(cameraStack);
            mountedBe.MarkCameraDirty();

            if (!player.InventoryManager.TryGiveItemstack(loadedPlate))
            { _ = Api.World.SpawnItemEntity(loadedPlate, player.Entity.Pos.XYZ.Add(0, 0.5, 0)); }

            AudioUtils.FireAndForgetEntitySound(Api?.World, _cameraPlateUnloadSound, player.Entity, AudioUtils.NextRandomPitch(Api?.World));
            return true;
        }

        internal void HandleMountedCameraBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer? byPlayer)
        {
            if (Api?.World == null) return;
            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityMountedCamera mountedBe) return;

            string ownerPlayerUid = mountedBe.OwnerPlayerUid;
            ItemStack? cameraStack = mountedBe.GetStoredCameraStack(world);
            if (cameraStack == null || !IsFieldcameraStack(cameraStack))
            {
                ForgetMountedCameraPos(ownerPlayerUid);
                return;
            }

            PauseMountedCameraStorage(cameraStack);
            ItemStack? droppedCamera = mountedBe.TakeStoredCameraStack(world);
            if (droppedCamera == null)
            {
                ForgetMountedCameraPos(ownerPlayerUid);
                return;
            }

            ClearMountedCameraPos(droppedCamera);
            ForgetMountedCameraPos(ownerPlayerUid);

            if (Api.World.PlayerByUid(ownerPlayerUid) is IServerPlayer ownerPlayer)
                SendMountedCameraControl(ownerPlayer, false, false, droppedCamera);

            world.SpawnItemEntity(droppedCamera, pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
    }
}

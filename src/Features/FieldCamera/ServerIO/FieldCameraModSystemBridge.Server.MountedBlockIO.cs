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

            // Determine whether a foreign photographer's exposure is loaded, and whether it is
            // actively running. PhotographerUid is only stamped while exposing, so a non-empty,
            // non-matching UID means the loaded plate belongs to a different photographer.
            string? photographerUid = null;
            PlateStage lockedStage = default;
            if (CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? checkPlate) && checkPlate != null)
            {
                PlateStage checkStage = PlateAttributes.GetStage(checkPlate);
                if (checkStage is PlateStage.Exposing or PlateStage.ExposurePaused)
                {
                    photographerUid = checkPlate.Attributes.GetString(PlateAttributes.PhotographerUid);
                    lockedStage = checkStage;
                }
            }

            bool isOtherPhotographer = !string.IsNullOrEmpty(photographerUid)
                && !string.Equals(photographerUid, serverPlayer.PlayerUID, StringComparison.Ordinal);

            // Exposure actively running by another photographer: block everything.
            // Once the exposure is paused, non-owners can unload the plate (Shift+RMB) or
            // recover the camera (Shift+Ctrl+RMB) — see the gates below.
            if (isOtherPhotographer && lockedStage == PlateStage.Exposing)
            {
                serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, "Collodion: someone else's exposure is in progress.", EnumChatType.Notification);
                return true;
            }

            // No foreign lock — if a different player is taking over an idle/own camera, transfer ownership.
            // Skip while a foreign photographer's paused plate is loaded so the original owner keeps the block.
            if (!isOtherPhotographer
                && !string.Equals(mountedBe.OwnerPlayerUid, serverPlayer.PlayerUID, StringComparison.Ordinal))
            {
                ForgetMountedCameraPos(mountedBe.OwnerPlayerUid);
                mountedBe.TransferOwnership(serverPlayer.PlayerUID);
            }

            // Shift+Ctrl+RMB: recover camera to player, pausing any active exposure first.
            if (shiftDown && ctrlDown)
            {
                _ = PauseMountedCameraStorage(cameraStack);
                mountedBe.MarkCameraDirty();

                // Forget the mount entries BEFORE sending the control packet so it carries
                // HasMountBlock=false and clears the client's ActiveMountedCameraPos. If we sent first,
                // the still-present entry would re-arm the static to a block we're about to delete.
                string oldOwnerUid = mountedBe.OwnerPlayerUid;
                ForgetMountedCameraPos(oldOwnerUid);
                ForgetMountedCameraPos(serverPlayer.PlayerUID);
                SendMountedCameraControl(serverPlayer, false, false, cameraStack);

                ItemStack? recovered = mountedBe.TakeStoredCameraStack(Api.World);
                if (recovered == null) { return false; }
                ClearMountedCameraPos(recovered);
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
                    // Only remember this block as the player's camera when it's actually theirs;
                    // a non-owner unloading a foreign paused plate must not adopt the block.
                    if (!isOtherPhotographer)
                    {
                        RememberMountedCameraPos(serverPlayer.PlayerUID, pos);
                    }
                    SendMountedCameraControl(serverPlayer, false, true, cameraStack);
                }
                return true;
            }

            // Plain RMB drives load / pause / resume — require ownership of any loaded exposure.
            if (isOtherPhotographer)
            {
                serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, "Collodion: someone else's paused exposure — Shift+RMB to unload the plate, or Shift+Ctrl+RMB to recover the camera.", EnumChatType.Notification);
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

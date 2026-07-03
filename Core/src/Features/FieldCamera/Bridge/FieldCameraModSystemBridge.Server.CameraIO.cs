using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

using Photocore.CameraCapture;
using Photocore.Plates;

namespace Photocore.FieldCamera
{
    internal sealed partial class FieldCameraModSystemBridge
    {
        private readonly Dictionary<string, BlockPos> _mountedCameraPositionsByPlayerUid = new(StringComparer.Ordinal);

        private static bool IsFieldcameraStack(ItemStack? stack)
        {
            return stack?.Item is ItemFieldcamera;
        }

        internal static bool CameraHasLoadedPlate(ItemStack? cameraStack)
        {
            if (cameraStack == null) return false;
            string loaded = cameraStack.Attributes.GetString(ItemFieldcamera.AttrLoadedPlate, string.Empty);
            return !string.IsNullOrWhiteSpace(loaded);
        }

        // Persists a single-item copy of the loaded plate onto the camera so later server actions can resolve it reliably.
        private static void SetLoadedPlateAttributes(ItemStack cameraStack, ItemStack loadedPlate)
            => CameraItemHelper.SetLoadedPlateStack(cameraStack, loadedPlate);

        private static void ClearLoadedPlateAttributes(ItemStack cameraStack)
        {
            cameraStack.Attributes.RemoveAttribute(ItemFieldcamera.AttrLoadedPlate);
            cameraStack.Attributes.RemoveAttribute(ItemFieldcamera.AttrLoadedPlateStack);
        }

        private static bool TryReadMountedCameraPos(ItemStack cameraStack, out BlockPos pos)
        {
            pos = new BlockPos(0, 0, 0);
            string[] parts = cameraStack.Attributes.GetString(CameraItemHelper.MountedPosAttrKey, string.Empty).Split(',');
            if (parts.Length != 3 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y) || !int.TryParse(parts[2], out int z)) return false;
            pos = new BlockPos(x, y, z);
            return true;
        }

        private static void SetMountedCameraPos(ItemStack cameraStack, BlockPos pos)
        {
            cameraStack.Attributes.SetString(CameraItemHelper.MountedPosAttrKey, $"{pos.X},{pos.Y},{pos.Z}");
        }

        private static void ClearMountedCameraPos(ItemStack cameraStack)
        {
            cameraStack.Attributes.RemoveAttribute(CameraItemHelper.MountedPosAttrKey);
        }

        private void RememberMountedCameraPos(string playerUid, BlockPos pos)
        {
            if (string.IsNullOrWhiteSpace(playerUid)) return;
            _mountedCameraPositionsByPlayerUid[playerUid] = pos.Copy();
        }

        private void ForgetMountedCameraPos(string playerUid)
        {
            if (string.IsNullOrWhiteSpace(playerUid)) return;
            _mountedCameraPositionsByPlayerUid.Remove(playerUid);
        }

        // When the owner disconnects ungracefully their client exposure accumulator dies, so a plate
        // left mid-exposure would stay stuck. Pause it (if the camera is still loaded) and drop the
        // stale mount entry. The load-time guard in BlockEntityMountedCamera covers the case where the
        // camera's chunk was already unloaded at disconnect time.
        private void OnPlayerDisconnect(IServerPlayer player)
        {
            if (!TryGetMountedCameraEntity(player.PlayerUID, out BlockEntityMountedCamera? mountedBe) || mountedBe == null)
                return; // dict entry already cleaned up by TryGetMountedCameraEntity on any failure path

            ItemStack? cameraStack = mountedBe.GetStoredCameraStack(Api?.World);
            if (cameraStack != null && PauseMountedCameraStorage(cameraStack))
                mountedBe.MarkCameraDirty(); // MarkDirty(true) -> client FromTreeAttributes -> RefreshExposingState -> idle model

            ForgetMountedCameraPos(player.PlayerUID);
        }

        private bool TryGetMountedCameraEntity(string playerUid, out BlockEntityMountedCamera? mountedBe)
        {
            mountedBe = null;
            if (Api?.World == null || string.IsNullOrWhiteSpace(playerUid)) return false;
            if (!_mountedCameraPositionsByPlayerUid.TryGetValue(playerUid, out BlockPos? pos) || pos == null) return false;

            if (Api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityMountedCamera be)
            {
                ForgetMountedCameraPos(playerUid);
                return false;
            }

            if (!string.Equals(be.OwnerPlayerUid, playerUid, StringComparison.Ordinal) || !be.HasStoredCamera(Api.World))
            {
                ForgetMountedCameraPos(playerUid);
                return false;
            }

            mountedBe = be;
            return true;
        }

        private bool TryResolveCameraStorage(IServerPlayer player, out ItemSlot? cameraSlot, out ItemStack? cameraStack, out BlockEntityMountedCamera? mountedBe)
        {
            cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            cameraStack = cameraSlot?.Itemstack;
            if (IsFieldcameraStack(cameraStack))
            {
                mountedBe = null;
                return cameraSlot != null && cameraStack != null;
            }

            if (TryGetMountedCameraEntity(player.PlayerUID, out mountedBe))
            {
                cameraStack = mountedBe?.GetStoredCameraStack(Api?.World);
                return IsFieldcameraStack(cameraStack);
            }

            mountedBe = null;
            cameraStack = null;
            return false;
        }

        private bool PauseMountedCameraStorage(ItemStack cameraStack)
            => CameraItemHelper.TryPauseExposingPlate(Api?.World, cameraStack);

        private bool ResumeMountedCameraStorage(ItemStack cameraStack, string playerUid)
        {
            if (Api?.World == null) return false;
            if (!CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? loadedPlate) || loadedPlate == null) return false;

            PlateStage stage = PlateAttributes.GetStage(loadedPlate);
            if (stage != PlateStage.ExposurePaused && stage != PlateStage.Sensitized) return false;

            // On first exposure (Sensitized → Exposing): assign exposure ID, stamp photographer, and
            // stamp capture date. This mirrors OnExposureStateReceived so the mounted-block path is
            // equally guarded.
            if (stage == PlateStage.Sensitized)
            {
                if (string.IsNullOrEmpty(loadedPlate.Attributes.GetString(PlateAttributes.ExposureId, string.Empty)))
                    loadedPlate.Attributes.SetString(PlateAttributes.ExposureId, Guid.NewGuid().ToString("N"));
                loadedPlate.Attributes.SetString(PlateAttributes.PhotographerUid, playerUid);
                PlateAttributes.SetCaptureDate(loadedPlate, Api.World.Calendar);
            }

            PlateDryingTransition.TickNow(Api.World, loadedPlate);
            PlateAttributes.SetStage(loadedPlate, PlateStage.Exposing);
            SetLoadedPlateAttributes(cameraStack, loadedPlate);
            return true;
        }

        private void SendMountedCameraControl(IServerPlayer player, bool isExposing, bool prepareIdlePreview, ItemStack? cameraStackOverride = null)
        {
            var packet = new MountedCameraControlPacket
            {
                IsExposing = isExposing,
                PrepareIdlePreview = prepareIdlePreview,
            };
            ItemStack? cameraStack = cameraStackOverride;

            if (cameraStack == null)
                TryResolveCameraStorage(player, out _, out cameraStack, out _);

            if (Api?.World != null && cameraStack != null)
            {
                if (CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? loadedPlate) && loadedPlate != null)
                {
                    packet.ExposureId = loadedPlate.Attributes.GetString(PlateAttributes.ExposureId, string.Empty);
                    packet.Chemistry = PlateAttributes.GetChemistry(loadedPlate) ?? string.Empty;
                }

                if (CameraItemHelper.TryGetMountedCaptureState(cameraStack, out VirtualCameraState cameraState))
                {
                    packet.HasCameraState = true;
                    packet.CameraPosX = cameraState.Position.X;
                    packet.CameraPosY = cameraState.Position.Y;
                    packet.CameraPosZ = cameraState.Position.Z;
                    packet.CameraYaw = cameraState.Yaw;
                    packet.CameraPitch = cameraState.Pitch;
                    packet.CameraFov = cameraState.Fov;
                    packet.CameraDimension = cameraState.Dimension;
                }
            }

            // Tell the client which camera block it is shooting through so it hides exactly that one.
            if (_mountedCameraPositionsByPlayerUid.TryGetValue(player.PlayerUID, out BlockPos? mountPos) && mountPos != null)
            {
                packet.HasMountBlock = true;
                packet.MountBlockX = mountPos.X;
                packet.MountBlockY = mountPos.Y;
                packet.MountBlockZ = mountPos.Z;
            }

            BestEffortLogger?.Notification(
                $"photocore[diag]: server send MountedCameraControl: isExposing={packet.IsExposing} " +
                $"hasCamState={packet.HasCameraState} prepIdle={packet.PrepareIdlePreview} hasMount={packet.HasMountBlock} " +
                $"chan={(ServerChannel == null ? "null" : "ok")}");
            ServerChannel?.SendPacket(packet, player);
        }

        private static void TryGiveOrSpawnMountedCamera(IWorldAccessor world, IServerPlayer player, BlockPos pos, ItemStack cameraStack)
        {
            if (!(player.InventoryManager?.TryGiveItemstack(cameraStack) ?? false))
                world.SpawnItemEntity(cameraStack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
    }
}

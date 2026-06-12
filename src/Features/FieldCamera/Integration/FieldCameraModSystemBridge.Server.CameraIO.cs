using Collodion.CameraCapture;
using Collodion.CameraCapture.Contracts;
using Collodion.Plates;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Collodion.FieldCamera
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
        {
            ItemStack clone = loadedPlate.Clone();
            clone.StackSize = 1;

            cameraStack.Attributes.SetString(ItemFieldcamera.AttrLoadedPlate, clone.Collectible?.Code?.ToString() ?? string.Empty);
            cameraStack.Attributes.SetItemstack(ItemFieldcamera.AttrLoadedPlateStack, clone);
        }

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
        {
            if (Api?.World == null) return false;
            if (!CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? loadedPlate) || loadedPlate == null) return false;

            PlateStage stage = PlateAttributes.GetStage(loadedPlate);
            if (stage != PlateStage.Exposing) return false;

            PlateDryingTransition.TickNow(Api.World, loadedPlate);
            PlateAttributes.SetStage(loadedPlate, PlateStage.ExposurePaused);
            SetLoadedPlateAttributes(cameraStack, loadedPlate);
            return true;
        }

        private bool ResumeMountedCameraStorage(ItemStack cameraStack)
        {
            if (Api?.World == null) return false;
            if (!CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? loadedPlate) || loadedPlate == null) return false;

            PlateStage stage = PlateAttributes.GetStage(loadedPlate);
            if (stage != PlateStage.ExposurePaused && stage != PlateStage.Sensitized) return false;

            // Assign a unique exposure ID the first time a fresh Sensitized plate begins exposing.
            // This guarantees the client always receives a non-empty ExposureId in the control packet,
            // preventing stale IDs from a previous camera session from contaminating a new one.
            if (stage == PlateStage.Sensitized &&
                string.IsNullOrEmpty(loadedPlate.Attributes.GetString(PlateAttributes.ExposureId, string.Empty)))
            {
                loadedPlate.Attributes.SetString(PlateAttributes.ExposureId, Guid.NewGuid().ToString("N"));
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

            ServerChannel?.SendPacket(packet, player);
        }

        private static void TryGiveOrSpawnMountedCamera(IWorldAccessor world, IServerPlayer player, BlockPos pos, ItemStack cameraStack)
        {
            if (!(player.InventoryManager?.TryGiveItemstack(cameraStack) ?? false))
                world.SpawnItemEntity(cameraStack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
    }
}

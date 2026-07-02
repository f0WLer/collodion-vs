using Photocore.CameraCapture;
using Photocore.Plates;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Photocore.FieldCamera
{
    internal sealed partial class FieldCameraModSystemBridge
    {
        private bool TryHandleCameraPlateLoad(IServerPlayer player)
        {
            if (Api?.World == null) return false;

            ItemSlot? cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            ItemSlot? offhandSlot = player.InventoryManager.OffhandHotbarSlot;

            ItemStack? cameraStack = cameraSlot?.Itemstack;
            ItemStack? offhandStack = offhandSlot?.Itemstack;

            if (!IsFieldcameraStack(cameraStack) || offhandStack == null) return false;
            if (cameraSlot == null || offhandSlot == null || cameraStack == null) return false;
            if (CameraHasLoadedPlate(cameraStack)) return false;
            if (!CameraEligibility.CanLoadIntoCamera(offhandStack)) return false;

            ItemStack loadedPlate = offhandStack.Clone();
            loadedPlate.StackSize = 1;

            SetLoadedPlateAttributes(cameraStack, loadedPlate);
            SetCameraCode(cameraSlot, GetLoadedCameraCodeForPlate(cameraStack, loadedPlate));

            offhandSlot.TakeOut(1);
            offhandSlot.MarkDirty();
            cameraSlot.MarkDirty();

            // Loading a dried plate is allowed, but it can no longer be exposed — let the player know.
            if (PlateDryingTransition.IsDry(Api.World, loadedPlate))
                player.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.Get("photocore:msg-plate-dried-reclaim"), EnumChatType.Notification);

            AudioUtils.FireAndForgetEntitySound(Api?.World, _cameraPlateLoadSound, player.Entity, AudioUtils.NextRandomPitch(Api?.World));
            return true;
        }

        private bool TryHandleCameraPlateUnload(IServerPlayer player)
        {
            if (Api?.World == null) return false;

            ItemSlot? cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            ItemSlot? offhandSlot = player.InventoryManager.OffhandHotbarSlot;

            ItemStack? cameraStack = cameraSlot?.Itemstack;
            if (!IsFieldcameraStack(cameraStack)) return false;
            if (offhandSlot == null || !offhandSlot.Empty) return false;
            if (cameraSlot == null || cameraStack == null) return false;
            if (!CameraHasLoadedPlate(cameraStack)) return false;

            if (!CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? loadedPlate) || loadedPlate == null)
            {
                ClearLoadedPlateAttributes(cameraStack);
                SetCameraCode(cameraSlot, GetBaseCode(cameraStack));
                cameraSlot.MarkDirty();
                return true;
            }

            loadedPlate.StackSize = 1;
            ClearLoadedPlateAttributes(cameraStack);
            SetCameraCode(cameraSlot, GetBaseCode(cameraStack));
            cameraSlot.MarkDirty();

            offhandSlot.Itemstack = loadedPlate;
            offhandSlot.MarkDirty();

            AudioUtils.FireAndForgetEntitySound(Api?.World, _cameraPlateUnloadSound, player.Entity, AudioUtils.NextRandomPitch(Api?.World));
            return true;
        }

        private bool TryHandleCameraTripodMount(IServerPlayer player)
        {
            if (Api?.World == null) return false;

            ItemSlot? cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            ItemSlot? offhandSlot = player.InventoryManager.OffhandHotbarSlot;

            ItemStack? cameraStack = cameraSlot?.Itemstack;
            ItemStack? offhandStack = offhandSlot?.Itemstack;

            if (!IsFieldcameraStack(cameraStack) || cameraSlot == null || offhandSlot == null || offhandStack == null) return false;
            if (CameraItemHelper.HasMountedTripod(cameraStack)) return false;
            if (!CameraItemHelper.IsTripodItemStack(offhandStack)) return false;

            cameraStack!.Attributes.SetString(CameraItemHelper.MountedAttrKey, offhandStack.Collectible?.Code?.ToString() ?? CameraItemHelper.TripodItemCode.ToString());
            offhandSlot.TakeOut(1);
            offhandSlot.MarkDirty();
            cameraSlot.MarkDirty();
            return true;
        }

        private bool TryHandleCameraTripodUnmount(IServerPlayer player)
        {
            if (Api?.World == null) return false;

            ItemSlot? cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            ItemSlot? offhandSlot = player.InventoryManager.OffhandHotbarSlot;

            ItemStack? cameraStack = cameraSlot?.Itemstack;
            if (!IsFieldcameraStack(cameraStack) || cameraSlot == null || offhandSlot == null) return false;
            if (!offhandSlot.Empty) return false;
            if (CameraHasLoadedPlate(cameraStack)) return false;
            if (!CameraItemHelper.HasMountedTripod(cameraStack)) return false;

            string tripodCode = cameraStack!.Attributes.GetString(CameraItemHelper.MountedAttrKey, string.Empty);
            if (string.IsNullOrWhiteSpace(tripodCode)) tripodCode = CameraItemHelper.TripodItemCode.ToString();

            Item? tripodItem = Api.World.GetItem(new AssetLocation(tripodCode)) ?? Api.World.GetItem(CameraItemHelper.TripodItemCode);
            if (tripodItem == null) return false;

            offhandSlot.Itemstack = new ItemStack(tripodItem, 1);
            offhandSlot.MarkDirty();

            cameraStack.Attributes.RemoveAttribute(CameraItemHelper.MountedAttrKey);
            cameraSlot.MarkDirty();
            return true;
        }

        private void OnCameraTripodReceived(IServerPlayer player, CameraTripodPacket packet)
        {
            if (Api?.Side != EnumAppSide.Server || player == null || packet == null) return;

            if (packet.Mount)
            {
                TryHandleCameraTripodMount(player);
                return;
            }

            TryHandleCameraTripodUnmount(player);
        }

        private bool EnsureMountedCameraBlock(ItemSlot cameraSlot, ItemStack cameraStack, IServerPlayer player, double cameraPosX, double cameraPosZ)
        {
            if (Api?.World == null || player?.Entity == null) return false;

            if (TryReadMountedCameraPos(cameraStack, out BlockPos existingPos))
            {
                Block existing = Api.World.BlockAccessor.GetBlock(existingPos);
                if (existing is BlockMountedCamera) return true;
            }

            BlockPos pos = player.Entity.Pos.AsBlockPos;
            Block current = Api.World.BlockAccessor.GetBlock(pos);
            if (current.Replaceable < 6000)
            {
                pos = pos.UpCopy();
                current = Api.World.BlockAccessor.GetBlock(pos);
                if (current.Replaceable < 6000) return false;
            }

            Block? mountedBlock = Api.World.GetBlock(new AssetLocation("photocore", "fieldcamera-tripod"));
            if (mountedBlock == null) return false;

            Api.World.BlockAccessor.SetBlock(mountedBlock.BlockId, pos);
            SetMountedCameraPos(cameraStack, pos);

            if (Api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityMountedCamera mountedBe) return false;

            mountedBe.SetStoredCameraStack(cameraStack, player.PlayerUID, Api.World);
            mountedBe.SetFacingYaw(player.Entity.Pos.Yaw);
            mountedBe.SetSubBlockOffset((float)(cameraPosX - (pos.X + 0.5)), (float)(cameraPosZ - (pos.Z + 0.5)));
            RememberMountedCameraPos(player.PlayerUID, pos);
            cameraSlot.Itemstack = null;
            cameraSlot.MarkDirty();
            return true;
        }

        private void OnCameraMountRequestReceived(IServerPlayer player, CameraMountRequestPacket packet)
        {
            if (Api?.Side != EnumAppSide.Server || player == null || packet == null) return;

            ItemSlot? cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            ItemStack? cameraStack = cameraSlot?.Itemstack;
            if (cameraSlot == null || cameraStack == null || !IsFieldcameraStack(cameraStack)) return;
            if (!CameraItemHelper.HasMountedTripod(cameraStack)) return;

            var cameraState = new VirtualCameraState(
                new Vec3d(
                    double.IsFinite(packet.CameraPosX) ? packet.CameraPosX : player.Entity.Pos.X,
                    double.IsFinite(packet.CameraPosY) ? packet.CameraPosY : player.Entity.Pos.Y,
                    double.IsFinite(packet.CameraPosZ) ? packet.CameraPosZ : player.Entity.Pos.Z),
                ClampFiniteRange(packet.CameraYaw, -360f, 360f),
                ClampFiniteRange(packet.CameraPitch, -180f, 180f),
                ClampFiniteRange(packet.CameraFov, 5f * GameMath.PI / 180f, GameMath.PI), // radians, not degrees
                packet.CameraDimension,
                selfPortrait: true);
            CameraItemHelper.SetMountedCaptureState(cameraStack, cameraState);

            if (!EnsureMountedCameraBlock(cameraSlot, cameraStack, player, cameraState.Position.X, cameraState.Position.Z)) return;

            // Fresh mount should immediately prepare idle preview before the first exposure begins.
            SendMountedCameraControl(player, false, true, cameraStack);
        }

        private void OnCameraLoadPlateReceived(IServerPlayer player, CameraLoadPlatePacket packet)
        {
            if (Api?.Side != EnumAppSide.Server || player == null || packet == null) return;

            if (packet.Load)
            {
                TryHandleCameraPlateLoad(player);
                return;
            }

            TryHandleCameraPlateUnload(player);
        }

        private void OnCameraRestReceived(IServerPlayer player, CameraRestPacket packet)
        {
            if (Api?.Side != EnumAppSide.Server || Api.World == null || player == null) return;

            ItemSlot? cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            ItemStack? cameraStack = cameraSlot?.Itemstack;
            if (cameraSlot == null || cameraStack == null || !IsFieldcameraStack(cameraStack)) return;
            if (!CameraItemHelper.HasMountedTripod(cameraStack)) return;

            // Pause any active plate exposure before storing.
            _ = PauseMountedCameraStorage(cameraStack);

            // Prefer placing on the face of the block the player is looking at; fall back to feet.
            BlockSelection? blockSel = player.CurrentBlockSelection;
            BlockPos pos = blockSel != null
                ? blockSel.Position.AddCopy(blockSel.Face.Normali)
                : player.Entity.Pos.AsBlockPos;

            Block current = Api.World.BlockAccessor.GetBlock(pos);
            if (current.Replaceable < 6000)
            {
                if (blockSel != null) return; // looked-at face is obstructed — don't silently redirect
                pos = pos.UpCopy();
                current = Api.World.BlockAccessor.GetBlock(pos);
                if (current.Replaceable < 6000) return;
            }

            BlockFacing facing = BlockFacing.HorizontalFromYaw(player.Entity.Pos.Yaw);
            Block? restingBlock = Api.World.GetBlock(new AssetLocation("photocore", $"fieldcamera-resting-{facing.Code}"));
            if (restingBlock == null) return;

            Api.World.BlockAccessor.SetBlock(restingBlock.BlockId, pos);

            if (Api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityRestingCamera restingBe) return;

            restingBe.SetStoredCameraStack(cameraStack, player.Entity.Pos.Yaw, Api.World);
            cameraSlot.Itemstack = null;
            cameraSlot.MarkDirty();
        }

        private static float ClampFiniteRange(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return min;
            return Math.Clamp(value, min, max);
        }
    }
}

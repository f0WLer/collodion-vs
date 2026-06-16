using System.Globalization;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Collodion.CameraCapture;
using Collodion.FieldCamera;

namespace Collodion
{
    /// <summary>
    /// Shared camera-item access helpers used by client and server camera flows.
    /// </summary>
    public static class CameraItemHelper
    {
        public static readonly AssetLocation TripodItemCode = new AssetLocation("collodion", "tripod");

        public const string MountedAttrKey    = "collodionMounted";
        public const string MountedPosAttrKey = "collodionMountedPos";
        internal const string MountedCaptureAttrKey = "collodionMountedCapture";

        // Gets the currently active hotbar slot only when it holds the field camera.
        public static ItemSlot? GetActiveCameraSlot(ICoreClientAPI? capi)
        {
            ItemSlot? activeSlot = capi?.World?.Player?.InventoryManager?.ActiveHotbarSlot;
            return activeSlot?.Itemstack?.Item is ItemFieldcamera ? activeSlot : null;
        }

        // Gets the currently active camera stack, or null when the active slot is not the camera.
        public static ItemStack? GetActiveCameraStack(ICoreClientAPI? capi)
        {
            return GetActiveCameraSlot(capi)?.Itemstack;
        }

        // Returns true when the camera stack currently carries an attached tripod code.
        public static bool HasMountedTripod(ItemStack? cameraStack)
        {
            if (cameraStack?.Item is not ItemFieldcamera) return false;
            return !string.IsNullOrEmpty(cameraStack.Attributes.GetString(MountedAttrKey, string.Empty));
        }

        // Returns true when the stack is the dedicated tripod item.
        public static bool IsTripodItemStack(ItemStack? stack)
        {
            return stack?.Collectible?.Code == TripodItemCode;
        }

        // Rehydrates the loaded plate from full stored stack first, then legacy code-only attribute.
        public static bool TryGetLoadedPlateStack(ItemStack? cameraStack, IWorldAccessor? world, out ItemStack? loadedPlate, Action<Exception>? onStoredStackReadFailure = null)
        {
            loadedPlate = null;
            if (cameraStack?.Item is not ItemFieldcamera || world == null) return false;

            try
            {
                loadedPlate = cameraStack.Attributes.GetItemstack(ItemFieldcamera.AttrLoadedPlateStack, null);
                loadedPlate?.ResolveBlockOrItem(world);
                if (loadedPlate != null) return true;
            }
            catch (Exception ex)
            {
                loadedPlate = null;
                onStoredStackReadFailure?.Invoke(ex);
            }

            string loadedCode = cameraStack.Attributes.GetString(ItemFieldcamera.AttrLoadedPlate, string.Empty) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(loadedCode)) return false;

            Item? loadedItem = world.GetItem(new AssetLocation(loadedCode));
            if (loadedItem == null) return false;

            loadedPlate = new ItemStack(loadedItem, 1);
            return true;
        }

        // Persists a single-item copy of the loaded plate onto the camera stack so later server
        // actions (and the mounted-camera block entity) can resolve and rewrite it consistently.
        public static void SetLoadedPlateStack(ItemStack? cameraStack, ItemStack loadedPlate)
        {
            if (cameraStack?.Item is not ItemFieldcamera) return;
            ItemStack clone = loadedPlate.Clone();
            clone.StackSize = 1;
            cameraStack.Attributes.SetString(ItemFieldcamera.AttrLoadedPlate, clone.Collectible?.Code?.ToString() ?? string.Empty);
            cameraStack.Attributes.SetItemstack(ItemFieldcamera.AttrLoadedPlateStack, clone);
        }

        internal static void SetMountedCaptureState(ItemStack? cameraStack, in VirtualCameraState state)
        {
            if (cameraStack?.Item is not ItemFieldcamera) return;
            cameraStack.Attributes.SetString(MountedCaptureAttrKey, FormattableString.Invariant(
                $"{state.Position.X:R};{state.Position.Y:R};{state.Position.Z:R};{state.Yaw:R};{state.Pitch:R};{state.Fov:R};{state.Dimension}"));
        }

        internal static bool TryGetMountedCaptureState(ItemStack? cameraStack, out VirtualCameraState state)
        {
            state = default;
            if (cameraStack?.Item is not ItemFieldcamera) return false;
            string? packed = cameraStack.Attributes.GetString(MountedCaptureAttrKey, null);
            if (string.IsNullOrEmpty(packed)) return false;

            string[] p = packed.Split(';');
            if (p.Length < 7) return false;
            var inv = CultureInfo.InvariantCulture;
            if (!double.TryParse(p[0], NumberStyles.Float, inv, out double x) ||
                !double.TryParse(p[1], NumberStyles.Float, inv, out double y) ||
                !double.TryParse(p[2], NumberStyles.Float, inv, out double z) ||
                !float.TryParse(p[3], NumberStyles.Float, inv, out float yaw) ||
                !float.TryParse(p[4], NumberStyles.Float, inv, out float pitch) ||
                !float.TryParse(p[5], NumberStyles.Float, inv, out float fov) ||
                !int.TryParse(p[6], out int dim))
                return false;

            state = new VirtualCameraState(new Vec3d(x, y, z), yaw, pitch, fov, dim, selfPortrait: true);
            return true;
        }

        internal static void ClearMountedCaptureState(ItemStack? cameraStack)
        {
            if (cameraStack?.Item is not ItemFieldcamera) return;
            cameraStack.Attributes.RemoveAttribute(MountedCaptureAttrKey);
        }
    }
}

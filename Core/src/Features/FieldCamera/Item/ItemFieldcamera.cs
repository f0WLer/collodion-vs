using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Photochemistry.FieldCamera
{
    // Shared camera item surface used by both client and server paths.
    // Keeps loaded-plate attribute keys and tooltip behavior centralized.
    // Related: ItemFieldcamera.Client*.cs (client behavior), CameraCaptureModSystemBridge.Server.cs (authority).
    public partial class ItemFieldcamera : Item
    {
        public const string AttrLoadedPlate = "photochemLoadedPlate";
        public const string AttrLoadedPlateStack = "photochemLoadedPlateStack";

        // Item code family used when swapping between loaded/unloaded visual variants.
        // Subclasses override these to stay within their own code family.
        private static readonly AssetLocation _baseCode             = new("photochemistry", "fieldcamera");
        // Asset code remains "loaded-silvered" for save-file backward compatibility; gameplay semantics are sensitized.
        private static readonly AssetLocation _loadedSensitizedCode = new("photochemistry", "fieldcamera-loaded-silvered");
        private static readonly AssetLocation _loadedExposedCode    = new("photochemistry", "fieldcamera-loaded-exposed");

        internal virtual AssetLocation CameraBaseCode             => _baseCode;
        internal virtual AssetLocation CameraLoadedSensitizedCode => _loadedSensitizedCode;
        internal virtual AssetLocation CameraLoadedExposedCode    => _loadedExposedCode;

        // Prevents normal item use so the client tick and held-interact callbacks can own the camera's custom viewfinder flow.
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;

            // Viewfinder mode is driven by the client tick polling RMB state in PhotochemistryModSystem.
            // We still prevent default use/interact while holding the camera.
        }

        // Shows the camera's basic controls plus the current loaded-plate state and the relevant load/unload hint.
        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine(Lang.Get("photochemistry:camera-info-controls"));
            dsc.AppendLine(Lang.Get("photochemistry:camera-info-tripod"));

            string? loadedPlate = inSlot?.Itemstack?.Attributes?.GetString(AttrLoadedPlate, null);
            if (!string.IsNullOrEmpty(loadedPlate))
            {
                dsc.AppendLine(Lang.Get("photochemistry:camera-info-plate-loaded", loadedPlate));
            }
            else
            {
                dsc.AppendLine(Lang.Get("photochemistry:camera-info-plate-none"));
            }

            if (string.IsNullOrEmpty(loadedPlate))
            {
                dsc.AppendLine(Lang.Get("photochemistry:camera-info-load-hint"));
            }
            else
            {
                dsc.AppendLine(Lang.Get("photochemistry:camera-info-unload-hint"));
            }
        }
    }
}

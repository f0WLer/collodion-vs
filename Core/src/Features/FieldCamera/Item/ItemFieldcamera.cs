using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Photocore.FieldCamera
{
    // Shared camera item surface used by both client and server paths.
    // Keeps loaded-plate attribute keys and tooltip behavior centralized.
    // Related: ItemFieldcamera.Client*.cs (client behavior), CameraCaptureModSystemBridge.Server.cs (authority).
    public partial class ItemFieldcamera : Item
    {
        public const string AttrLoadedPlate = "photochemLoadedPlate";
        public const string AttrLoadedPlateStack = "photochemLoadedPlateStack";

        // Item code family used when swapping between loaded/unloaded visual variants. The stem comes
        // from the "cameraFamily" itemtype attribute (default "fieldcamera") resolved in the item's own
        // domain, so a new camera family is a new JSON itemtype — no item subclass in a head.
        // Asset code remains "loaded-silvered" for save-file backward compatibility; gameplay semantics are sensitized.
        private AssetLocation? _baseCode;
        private AssetLocation? _loadedSensitizedCode;
        private AssetLocation? _loadedExposedCode;

        private string FamilyStem => Attributes?["cameraFamily"].AsString("fieldcamera") ?? "fieldcamera";

        internal AssetLocation CameraBaseCode             => _baseCode             ??= new AssetLocation(Code.Domain, FamilyStem);
        internal AssetLocation CameraLoadedSensitizedCode => _loadedSensitizedCode ??= new AssetLocation(Code.Domain, FamilyStem + "-loaded-silvered");
        internal AssetLocation CameraLoadedExposedCode    => _loadedExposedCode    ??= new AssetLocation(Code.Domain, FamilyStem + "-loaded-exposed");

        // Prevents normal item use so the client tick and held-interact callbacks can own the camera's custom viewfinder flow.
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;

            // Viewfinder mode is driven by the client tick polling RMB state in PhotocoreModSystem.
            // We still prevent default use/interact while holding the camera.
        }

        // Shows the camera's basic controls plus the current loaded-plate state and the relevant load/unload hint.
        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine(Lang.Get("photocore:camera-info-controls"));
            dsc.AppendLine(Lang.Get("photocore:camera-info-load-attach-hint"));

            string? loadedPlate = inSlot?.Itemstack?.Attributes?.GetString(AttrLoadedPlate, null);
            if (!string.IsNullOrEmpty(loadedPlate))
            {
                dsc.AppendLine(Lang.Get("photocore:camera-info-plate-loaded", loadedPlate));
            }
            else
            {
                dsc.AppendLine(Lang.Get("photocore:camera-info-plate-none"));
            }

            dsc.AppendLine(Lang.Get("photocore:camera-info-unload-detach-hint"));
        }
    }
}

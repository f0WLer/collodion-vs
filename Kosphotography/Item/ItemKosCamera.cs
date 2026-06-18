using System;
using System.Text;
using Collodion.FieldCamera;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Kosphotography
{
    // Camera item backing the kosphotography timed/automatic cameras. Keeps the loaded/unloaded visual
    // swap within its own code family (via the "cameraFamily" itemtype attribute) so a timed/automatic
    // camera doesn't revert to the baseline manual camera when a plate is loaded, and adds a shutter-mode
    // line to the tooltip.
    public class ItemKosCamera : ItemFieldcamera
    {
        private string FamilyStem => Attributes?["cameraFamily"].AsString("fieldcamera") ?? "fieldcamera";

        internal override AssetLocation CameraBaseCode             => new AssetLocation(Code.Domain, FamilyStem);
        internal override AssetLocation CameraLoadedSensitizedCode => new AssetLocation(Code.Domain, FamilyStem + "-loaded-silvered");
        internal override AssetLocation CameraLoadedExposedCode    => new AssetLocation(Code.Domain, FamilyStem + "-loaded-exposed");

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            string mode = KosShutterPolicyProvider.ReadShutterMode(inSlot?.Itemstack);
            if (string.Equals(mode, KosCameraAttrs.ModeTimed, StringComparison.OrdinalIgnoreCase))
            {
                int duration = inSlot?.Itemstack?.Attributes?.GetInt(
                    KosCameraAttrs.ShutterDurationAttr, KosCameraAttrs.DefaultShutterDurationSeconds)
                    ?? KosCameraAttrs.DefaultShutterDurationSeconds;
                dsc.AppendLine(Lang.Get("kosphotography:camera-info-timed", duration));
            }
            else if (string.Equals(mode, KosCameraAttrs.ModeAutomatic, StringComparison.OrdinalIgnoreCase))
            {
                dsc.AppendLine(Lang.Get("kosphotography:camera-info-automatic"));
            }
        }
    }
}

using System.Text;
using Photocore.FieldCamera;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Kosphotography
{
    // Camera item backing the kosphotography timed/automatic cameras: adds the shutter-mode line to
    // the tooltip. The loaded/unloaded visual swap stays within the item's own code family via the
    // "cameraFamily" itemtype attribute, which the base item resolves.
    public class ItemKosCamera : ItemFieldcamera
    {
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

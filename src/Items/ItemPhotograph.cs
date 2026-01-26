using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Collodion
{
    public partial class ItemPhotograph : Item
    {

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            // Legacy behavior placed a "photographmounted-*" block on walls.
            // The mod no longer supports mounted photo items/blocks; framed photos handle placement themselves.
            return;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            ItemStack? stack = inSlot?.Itemstack;
            if (stack == null) return;

            string photoId = stack.Attributes.GetString(PhotographAttrs.PhotoId) ?? string.Empty;
            string photographer = stack.Attributes.GetString("photographer") ?? string.Empty;
            string timestamp = stack.Attributes.GetString("timestamp") ?? string.Empty;
            string caption = stack.Attributes.GetString(PhotographAttrs.Caption) ?? string.Empty;

            if (!string.IsNullOrEmpty(caption))
            {
                int maxLen = 180;
                try
                {
                    if (api is ICoreClientAPI capi)
                    {
                        var modSys = capi.ModLoader.GetModSystem<CollodionModSystem>();
                        maxLen = modSys?.ClientConfig?.CaptionTooltipMaxLength ?? 180;
                    }
                }
                catch
                {
                    // ignore
                }

                if (maxLen > 0 && caption.Length > maxLen)
                {
                    caption = caption.Substring(0, maxLen) + "…";
                }
            }

            if (!string.IsNullOrEmpty(photographer)) dsc.AppendLine($"Photographer: {photographer}");
            if (!string.IsNullOrEmpty(timestamp)) dsc.AppendLine($"Developed: {timestamp}");
            if (!string.IsNullOrEmpty(caption)) dsc.AppendLine($"Caption: {caption}");
        }
    }
}

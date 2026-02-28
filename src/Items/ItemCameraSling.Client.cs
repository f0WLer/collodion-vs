using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public partial class ItemCameraSling
    {
        private static string GetSlingPosePrefix(ItemStack itemstack)
        {
            return itemstack?.Collectible?.Code?.Path == "camerasling-full"
                ? "sling-full"
                : "sling-empty";
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
#pragma warning disable CS0618 // Preserve FP compatibility where present
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

            var modSys = capi.ModLoader.GetModSystem<CollodionModSystem>();
            string posePrefix = GetSlingPosePrefix(itemstack);
            string poseKey = target switch
            {
                EnumItemRenderTarget.HandTp => $"{posePrefix}-tp",
                EnumItemRenderTarget.Gui => $"{posePrefix}-gui",
                EnumItemRenderTarget.Ground => $"{posePrefix}-ground",
                _ => string.Empty
            };
#pragma warning restore CS0618

            if (string.IsNullOrEmpty(poseKey)) return;

            if (target == EnumItemRenderTarget.Gui)
            {
                var t = renderinfo.Transform == null
                    ? new ModelTransform()
                    : RenderPoseUtil.CloneTransform(renderinfo.Transform);

                if (renderinfo.Transform == null)
                {
                    t.Translation = new FastVec3f(0f, 0f, 0f);
                    t.Rotation = new FastVec3f(0f, 0f, 0f);
                    t.Origin.X = 0f;
                    t.Origin.Y = 0f;
                    t.Origin.Z = 0f;
                    t.ScaleXYZ = new FastVec3f(1f, 1f, 1f);
                }

                t.Rotate = true;
                renderinfo.Transform = t;
            }

            RenderPoseUtil.ApplyPoseDelta(modSys, poseKey, ref renderinfo);
        }
    }
}

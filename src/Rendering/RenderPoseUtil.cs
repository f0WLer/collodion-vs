using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Collodion
{
    internal static class RenderPoseUtil
    {
        internal static ModelTransform CloneTransform(ModelTransform src)
        {
            var dst = new ModelTransform();

            dst.Translation.X = src.Translation.X;
            dst.Translation.Y = src.Translation.Y;
            dst.Translation.Z = src.Translation.Z;

            dst.Rotation.X = src.Rotation.X;
            dst.Rotation.Y = src.Rotation.Y;
            dst.Rotation.Z = src.Rotation.Z;

            dst.Origin.X = src.Origin.X;
            dst.Origin.Y = src.Origin.Y;
            dst.Origin.Z = src.Origin.Z;

            dst.ScaleXYZ = src.ScaleXYZ;
            dst.Rotate = src.Rotate;
            return dst;
        }

        internal static void ApplyPoseDelta(CollodionModSystem modSys, string poseKey, ref ItemRenderInfo renderinfo)
        {
            var d = modSys.GetPoseDelta(poseKey);

            renderinfo.Transform = renderinfo.Transform == null
                ? new ModelTransform()
                : CloneTransform(renderinfo.Transform);

            renderinfo.Transform.Translation.X += d.Tx;
            renderinfo.Transform.Translation.Y += d.Ty;
            renderinfo.Transform.Translation.Z += d.Tz;

            renderinfo.Transform.Rotation.X += d.Rx;
            renderinfo.Transform.Rotation.Y += d.Ry;
            renderinfo.Transform.Rotation.Z += d.Rz;

            renderinfo.Transform.Origin.X += d.Ox;
            renderinfo.Transform.Origin.Y += d.Oy;
            renderinfo.Transform.Origin.Z += d.Oz;

            renderinfo.Transform.ScaleXYZ *= d.Scale;

            // Keep inventory/hover preview stationary for GUI pose targets.
            if (poseKey.EndsWith("-gui", System.StringComparison.OrdinalIgnoreCase)
                || poseKey.Equals("gui", System.StringComparison.OrdinalIgnoreCase))
            {
                renderinfo.Transform.Rotate = false;
            }
        }
    }
}

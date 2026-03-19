using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public partial class CollodionModSystem
    {
        // Live pose tuning (client-only): additive transform applied at render-time so you can
        // tweak the camera position/rotation/scale in-game and then copy values into JSON.
        public class PoseDelta
        {
            public float Tx;
            public float Ty;
            public float Tz;
            public float Rx;
            public float Ry;
            public float Rz;
            public float Ox;
            public float Oy;
            public float Oz;
            public float Scale = 1f;
        }

        private readonly Dictionary<string, PoseDelta> poseDeltas = new Dictionary<string, PoseDelta>(StringComparer.OrdinalIgnoreCase);
        private static readonly AssetLocation[] DevTrayRawCodes =
        {
            new AssetLocation("collodion", "developmenttray-raw-blue"),
            new AssetLocation("collodion", "developmenttray-raw-fire"),
            new AssetLocation("collodion", "developmenttray-raw-red")
        };
        private readonly Dictionary<string, ModelTransform> devTrayKilnBaseTransforms = new Dictionary<string, ModelTransform>(StringComparer.OrdinalIgnoreCase);
        private const float PlateBoxEwRightOffset = -0.007f;

        public PoseDelta GetPoseDelta(string target)
        {
            if (!poseDeltas.TryGetValue(target, out PoseDelta? delta) || delta == null)
            {
                delta = new PoseDelta();
                poseDeltas[target] = delta;
            }
            return delta;
        }

        public float GetPlateBoxEwRightOffset()
        {
            return PlateBoxEwRightOffset;
        }

        private bool EnsureDevTrayKilnBaseTransforms()
        {
            if (ClientApi?.World == null) return false;

            foreach (AssetLocation code in DevTrayRawCodes)
            {
                string key = code.ToShortString();
                if (devTrayKilnBaseTransforms.ContainsKey(key)) continue;

                Item? item = ClientApi.World.GetItem(code);
                if (item == null) continue;

                ModelTransform baseTransform = item.GroundTransform != null
                    ? RenderPoseUtil.CloneTransform(item.GroundTransform)
                    : ModelTransform.ItemDefaultGround();

                devTrayKilnBaseTransforms[key] = baseTransform;
            }

            return devTrayKilnBaseTransforms.Count > 0;
        }

        public bool TryGetDevTrayKilnBaseTransform(out ModelTransform baseTransform)
        {
            baseTransform = ModelTransform.ItemDefaultGround();
            if (!EnsureDevTrayKilnBaseTransforms()) return false;

            foreach (AssetLocation code in DevTrayRawCodes)
            {
                string key = code.ToShortString();
                if (!devTrayKilnBaseTransforms.TryGetValue(key, out ModelTransform? candidate) || candidate == null) continue;
                baseTransform = RenderPoseUtil.CloneTransform(candidate);
                return true;
            }

            return false;
        }

        public bool TryApplyDevTrayKilnPose(PoseDelta delta, out int updatedCount)
        {
            updatedCount = 0;
            if (ClientApi?.World == null) return false;
            if (!EnsureDevTrayKilnBaseTransforms()) return false;

            foreach (AssetLocation code in DevTrayRawCodes)
            {
                Item? item = ClientApi.World.GetItem(code);
                if (item == null) continue;

                string key = code.ToShortString();
                if (!devTrayKilnBaseTransforms.TryGetValue(key, out ModelTransform? baseTransform) || baseTransform == null) continue;

                var transform = RenderPoseUtil.CloneTransform(baseTransform);
                transform.Translation = new FastVec3f(
                    baseTransform.Translation.X + delta.Tx,
                    baseTransform.Translation.Y + delta.Ty,
                    baseTransform.Translation.Z + delta.Tz
                );
                transform.Rotation = new FastVec3f(
                    baseTransform.Rotation.X + delta.Rx,
                    baseTransform.Rotation.Y + delta.Ry,
                    baseTransform.Rotation.Z + delta.Rz
                );
                transform.Origin = new FastVec3f(
                    baseTransform.Origin.X + delta.Ox,
                    baseTransform.Origin.Y + delta.Oy,
                    baseTransform.Origin.Z + delta.Oz
                );
                transform.ScaleXYZ = new FastVec3f(
                    baseTransform.ScaleXYZ.X * delta.Scale,
                    baseTransform.ScaleXYZ.Y * delta.Scale,
                    baseTransform.ScaleXYZ.Z * delta.Scale
                );

                item.GroundTransform = transform;
                updatedCount++;
            }

            return updatedCount > 0;
        }

    }
}

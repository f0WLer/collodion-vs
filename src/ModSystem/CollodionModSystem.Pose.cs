using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

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

        public PoseDelta GetPoseDelta(string target)
        {
            if (!poseDeltas.TryGetValue(target, out PoseDelta? delta) || delta == null)
            {
                delta = new PoseDelta();
                poseDeltas[target] = delta;
            }
            return delta;
        }

    }
}

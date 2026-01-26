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

        private void LoadPoseDeltas()
        {
            if (ClientApi == null) return;

            try
            {
                var cfg = GetOrLoadClientConfig(ClientApi);
                var loaded = cfg.PoseDeltas;
                if (loaded == null || loaded.Count == 0) return;

                poseDeltas.Clear();
                foreach (var kvp in loaded)
                {
                    poseDeltas[kvp.Key] = kvp.Value;
                }
            }
            catch
            {
                // ignore
            }
        }

        private void SavePoseDeltas()
        {
            if (ClientApi == null) return;

            try
            {
                var cfg = GetOrLoadClientConfig(ClientApi);
                cfg.PoseDeltas = new Dictionary<string, PoseDelta>(poseDeltas, StringComparer.OrdinalIgnoreCase);
                SaveClientConfig(ClientApi);
            }
            catch
            {
                // ignore
            }
        }

    }
}

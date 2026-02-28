using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Collodion
{
    public partial class CollodionModSystem
    {
        private bool TryGetSlingGuiBaseTransform(string poseKey, out ModelTransform baseTransform)
        {
            baseTransform = new ModelTransform();
            if (ClientApi?.World == null) return false;

            if (!poseKey.EndsWith("-gui", StringComparison.OrdinalIgnoreCase)) return false;

            AssetLocation itemCode;
            if (poseKey.StartsWith("sling-empty-", StringComparison.OrdinalIgnoreCase))
            {
                itemCode = new AssetLocation("collodion", "camerasling-empty");
            }
            else if (poseKey.StartsWith("sling-full-", StringComparison.OrdinalIgnoreCase))
            {
                itemCode = new AssetLocation("collodion", "camerasling-full");
            }
            else if (poseKey.StartsWith("sling-", StringComparison.OrdinalIgnoreCase))
            {
                // Back-compat key: treat generic sling as empty sling.
                itemCode = new AssetLocation("collodion", "camerasling-empty");
            }
            else
            {
                return false;
            }

            Item? item = ClientApi.World.GetItem(itemCode);
            if (item?.GuiTransform == null) return false;

            baseTransform = RenderPoseUtil.CloneTransform(item.GuiTransform);
            return true;
        }

        private void ShowJsonReadyPose(string poseKey, PoseDelta d)
        {
            if (!TryGetSlingGuiBaseTransform(poseKey, out ModelTransform baseTransform)) return;

            float tx = baseTransform.Translation.X + d.Tx;
            float ty = baseTransform.Translation.Y + d.Ty;
            float tz = baseTransform.Translation.Z + d.Tz;

            float rx = baseTransform.Rotation.X + d.Rx;
            float ry = baseTransform.Rotation.Y + d.Ry;
            float rz = baseTransform.Rotation.Z + d.Rz;

            float ox = baseTransform.Origin.X + d.Ox;
            float oy = baseTransform.Origin.Y + d.Oy;
            float oz = baseTransform.Origin.Z + d.Oz;

            float baseScale = baseTransform.ScaleXYZ.X;
            float scale = baseScale * d.Scale;

            string f(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

            ClientApi?.ShowChatMessage($"Wetplate pose json[{poseKey}]: {{\"translation\":{{\"x\":{f(tx)},\"y\":{f(ty)},\"z\":{f(tz)}}},\"rotation\":{{\"x\":{f(rx)},\"y\":{f(ry)},\"z\":{f(rz)}}},\"origin\":{{\"x\":{f(ox)},\"y\":{f(oy)},\"z\":{f(oz)}}},\"scale\":{f(scale)}}}");
        }

        private void HandleWetplatePoseCommand(Vintagestory.API.Common.CmdArgs args)
        {
            if (ClientApi == null) return;

            // .collodion pose [photo|camera|plate|sling|slingempty|slingfull] <target> <op> <axis> <amount>
            // targets (camera): fp, tp, gui
            // targets (photo): fp, tp, gui, ground
            // targets (plate): fp, tp, gui, ground
            // targets (sling*, all sling selectors): tp, gui, ground
            // ops: t (translate), r (rotate), o (origin), s (scale), show, reset, export
            string first = args.PopWord() ?? "fp";

            bool isPhoto = first.Equals("photo", StringComparison.OrdinalIgnoreCase) || first.Equals("photograph", StringComparison.OrdinalIgnoreCase);
            bool isCamera = first.Equals("camera", StringComparison.OrdinalIgnoreCase);
            bool isPlate = first.Equals("plate", StringComparison.OrdinalIgnoreCase) || first.Equals("plates", StringComparison.OrdinalIgnoreCase);
            bool isSling = first.Equals("sling", StringComparison.OrdinalIgnoreCase) || first.Equals("camerasling", StringComparison.OrdinalIgnoreCase);
            bool isSlingEmpty = first.Equals("slingempty", StringComparison.OrdinalIgnoreCase) || first.Equals("emptysling", StringComparison.OrdinalIgnoreCase) || first.Equals("cameraslingempty", StringComparison.OrdinalIgnoreCase);
            bool isSlingFull = first.Equals("slingfull", StringComparison.OrdinalIgnoreCase) || first.Equals("fullsling", StringComparison.OrdinalIgnoreCase) || first.Equals("cameraslingfull", StringComparison.OrdinalIgnoreCase);

            string target;
            string op;
            if (isPhoto || isCamera || isPlate || isSling || isSlingEmpty || isSlingFull)
            {
                target = args.PopWord() ?? "fp";
                op = args.PopWord() ?? "show";
            }
            else
            {
                // Back-compat: original syntax `pose <fp|tp|gui> ...` applies to camera.
                target = first;
                op = args.PopWord() ?? "show";
            }

            string poseKey;
            if (isPhoto)
            {
                poseKey = $"photo-{target}";
            }
            else if (isPlate)
            {
                poseKey = $"plate-{target}";
            }
            else if (isSling)
            {
                poseKey = $"sling-{target}";
            }
            else if (isSlingEmpty)
            {
                poseKey = $"sling-empty-{target}";
            }
            else if (isSlingFull)
            {
                poseKey = $"sling-full-{target}";
            }
            else
            {
                // camera (default)
                poseKey = target;
            }

            PoseDelta d = GetPoseDelta(poseKey);

            if (op.Equals("show", StringComparison.OrdinalIgnoreCase))
            {
                ClientApi.ShowChatMessage($"Wetplate pose[{poseKey}]: t=({d.Tx:0.###},{d.Ty:0.###},{d.Tz:0.###}) r=({d.Rx:0.###},{d.Ry:0.###},{d.Rz:0.###}) s={d.Scale:0.###}");
                ClientApi.ShowChatMessage($"Wetplate pose[{poseKey}]: o=({d.Ox:0.###},{d.Oy:0.###},{d.Oz:0.###})");
                ShowJsonReadyPose(poseKey, d);
                ClientApi.ShowChatMessage("Usage: .collodion pose [photo|camera|plate|sling|slingempty|slingfull] <fp|tp|gui|ground> t|r|o <x|y|z> <value> (sets) OR ... add <delta> OR ... s <value> OR ... reset OR ... export");
                return;
            }

            if (op.Equals("export", StringComparison.OrdinalIgnoreCase))
            {
                // Print a ready-to-copy JSON snippet for the delta file (or for sharing values).
                string f(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

                ClientApi.ShowChatMessage($"Wetplate pose export [{poseKey}]: {{\"translation\":{{\"x\":{f(d.Tx)},\"y\":{f(d.Ty)},\"z\":{f(d.Tz)}}},\"rotation\":{{\"x\":{f(d.Rx)},\"y\":{f(d.Ry)},\"z\":{f(d.Rz)}}},\"origin\":{{\"x\":{f(d.Ox)},\"y\":{f(d.Oy)},\"z\":{f(d.Oz)}}},\"scale\":{f(d.Scale)}}}");
                ShowJsonReadyPose(poseKey, d);
                ClientApi.ShowChatMessage("Tip: You can also just send collodion-posedeltas.json from your VS config folder.");
                return;
            }

            if (op.Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                poseDeltas[poseKey] = new PoseDelta();
                SavePoseDeltas();
                ClientApi.ShowChatMessage($"Wetplate pose[{poseKey}] reset.");
                return;
            }

            if (op.Equals("s", StringComparison.OrdinalIgnoreCase) || op.Equals("scale", StringComparison.OrdinalIgnoreCase))
            {
                string amtWord = args.PopWord();
                if (!float.TryParse(amtWord, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s))
                {
                    ClientApi.ShowChatMessage("Wetplate pose: scale requires a float amount (e.g. 1.0, 1.2, 0.8)");
                    return;
                }
                d.Scale = Math.Max(0.01f, s);
                SavePoseDeltas();
                ClientApi.ShowChatMessage($"Wetplate pose[{poseKey}] scale={d.Scale:0.###}");
                return;
            }

            string axis = args.PopWord() ?? "x";

            // Default behavior: set absolute values.
            // Optional: use "add"/"delta" to increment existing values.
            bool addMode = false;
            string modeOrAmt = args.PopWord() ?? "0";
            string amtStr;

            if (modeOrAmt.Equals("add", StringComparison.OrdinalIgnoreCase) || modeOrAmt.Equals("delta", StringComparison.OrdinalIgnoreCase))
            {
                addMode = true;
                amtStr = args.PopWord() ?? "0";
            }
            else if (modeOrAmt.Equals("set", StringComparison.OrdinalIgnoreCase) || modeOrAmt.Equals("=", StringComparison.OrdinalIgnoreCase))
            {
                addMode = false;
                amtStr = args.PopWord() ?? "0";
            }
            else
            {
                amtStr = modeOrAmt;
            }

            if (!float.TryParse(amtStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float amt))
            {
                ClientApi.ShowChatMessage("Wetplate pose: value must be a float (use dot decimal, e.g. 0.1)");
                return;
            }

            bool isTranslate = op.Equals("t", StringComparison.OrdinalIgnoreCase) || op.Equals("translate", StringComparison.OrdinalIgnoreCase);
            bool isRotate = op.Equals("r", StringComparison.OrdinalIgnoreCase) || op.Equals("rotate", StringComparison.OrdinalIgnoreCase);
            bool isOrigin = op.Equals("o", StringComparison.OrdinalIgnoreCase) || op.Equals("origin", StringComparison.OrdinalIgnoreCase);

            if (!isTranslate && !isRotate && !isOrigin)
            {
                ClientApi.ShowChatMessage("Wetplate pose: op must be t, r, o, s, show, reset, or export");
                return;
            }

            switch (axis.ToLowerInvariant())
            {
                case "x":
                    if (isTranslate) d.Tx = addMode ? d.Tx + amt : amt;
                    else if (isRotate) d.Rx = addMode ? d.Rx + amt : amt;
                    else d.Ox = addMode ? d.Ox + amt : amt;
                    break;
                case "y":
                    if (isTranslate) d.Ty = addMode ? d.Ty + amt : amt;
                    else if (isRotate) d.Ry = addMode ? d.Ry + amt : amt;
                    else d.Oy = addMode ? d.Oy + amt : amt;
                    break;
                case "z":
                    if (isTranslate) d.Tz = addMode ? d.Tz + amt : amt;
                    else if (isRotate) d.Rz = addMode ? d.Rz + amt : amt;
                    else d.Oz = addMode ? d.Oz + amt : amt;
                    break;
                default:
                    ClientApi.ShowChatMessage("Wetplate pose: axis must be x, y, or z");
                    return;
            }

            SavePoseDeltas();
            ClientApi.ShowChatMessage($"Wetplate pose[{poseKey}]: t=({d.Tx:0.###},{d.Ty:0.###},{d.Tz:0.###}) r=({d.Rx:0.###},{d.Ry:0.###},{d.Rz:0.###}) o=({d.Ox:0.###},{d.Oy:0.###},{d.Oz:0.###}) s={d.Scale:0.###}");
        }
    }
}

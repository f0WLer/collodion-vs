using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Collodion
{
    public sealed partial class BlockEntityPlateBox
    {
        private const float SlotPlateWidth = 0.25f / 16f;
        private const float SlotPlateHeight = 7.7f / 16f;
        private const float SlotPlateDepth = 3.5f / 16f;
        private static readonly Vec3f SlotPlateOffset = new Vec3f(0.225f / 16f, 0f, 3.5f / 16f);

        private static readonly Vec3f[] SlotOrigins =
        {
            // Matches platehb1..platehb8 "from" coordinates in platebox-open shape.
            new Vec3f(1.5f / 16f, 0.5f / 16f, 4.5f / 16f),
            new Vec3f(3.0f / 16f, 0.5f / 16f, 4.5f / 16f),
            new Vec3f(4.5f / 16f, 0.5f / 16f, 4.5f / 16f),
            new Vec3f(6.0f / 16f, 0.5f / 16f, 4.5f / 16f),
            new Vec3f(9.5f / 16f, 0.5f / 16f, 4.5f / 16f),
            new Vec3f(11.0f / 16f, 0.5f / 16f, 4.5f / 16f),
            new Vec3f(12.5f / 16f, 0.5f / 16f, 4.5f / 16f),
            new Vec3f(14.0f / 16f, 0.5f / 16f, 4.5f / 16f)
        };

        private PlateBoxSlotRenderer? clientSlotRenderer;

        partial void ClientInitialize(ICoreAPI api)
        {
            if (api?.Side != EnumAppSide.Client) return;

            ICoreClientAPI capi = (ICoreClientAPI)api;
            if (clientSlotRenderer == null)
            {
                clientSlotRenderer = new PlateBoxSlotRenderer(capi, this);
                capi.Event.RegisterRenderer(clientSlotRenderer, EnumRenderStage.Opaque, "collodion-platebox-slotrender");
            }

            try
            {
                capi.World.BlockAccessor.MarkBlockDirty(Pos);
            }
            catch
            {
                // ignore
            }
        }

        partial void ClientSlotsChanged(bool markBlockDirty)
        {
            if (!markBlockDirty || Api?.Side != EnumAppSide.Client) return;

            try
            {
                ((ICoreClientAPI)Api).World.BlockAccessor.MarkBlockDirty(Pos);
            }
            catch
            {
                // ignore
            }
        }

        public override void OnBlockRemoved()
        {
            DisposeClientRenderer();
            base.OnBlockRemoved();
        }

        public override void OnBlockUnloaded()
        {
            DisposeClientRenderer();
            base.OnBlockUnloaded();
        }

        private void DisposeClientRenderer()
        {
            try
            {
                if (Api?.Side != EnumAppSide.Client || clientSlotRenderer == null) return;

                ICoreClientAPI capi = (ICoreClientAPI)Api;
                capi.Event.UnregisterRenderer(clientSlotRenderer, EnumRenderStage.Opaque);
                clientSlotRenderer.Dispose();
                clientSlotRenderer = null;
            }
            catch
            {
                // ignore
            }
        }

        private string?[] SnapshotSlotCodePaths()
        {
            string?[] snapshot = new string?[SlotCount];

            lock (slotLock)
            {
                for (int index = 0; index < SlotCount; index++)
                {
                    snapshot[index] = plateSlots[index]?.Collectible?.Code?.Path;
                }
            }

            return snapshot;
        }

        private sealed class PlateBoxSlotRenderer : IRenderer, IDisposable
        {
            private readonly ICoreClientAPI capi;
            private readonly BlockEntityPlateBox owner;
            private readonly CollodionModSystem modSys;
            private readonly Matrixf modelMat = new Matrixf();
            private MeshRef? slotMeshRef;   // south/north: thin in X, depth in Z  (PW × PH × PD)
            private MeshRef? slotMeshRefEW; // east/west:   thin in Z, depth in X  (PD × PH × PW)

            public PlateBoxSlotRenderer(ICoreClientAPI capi, BlockEntityPlateBox owner)
            {
                this.capi = capi;
                this.owner = owner;
                modSys = capi.ModLoader.GetModSystem<CollodionModSystem>();
            }

            public double RenderOrder => 0.5;
            public int RenderRange => 64;

            public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
            {
                if (stage != EnumRenderStage.Opaque) return;
                if (owner.Api?.Side != EnumAppSide.Client) return;

                string blockPath = owner.Block?.Code?.Path ?? string.Empty;
                bool isOpen = owner.IsOpen || blockPath.StartsWith("platebox-open", StringComparison.OrdinalIgnoreCase);
                if (!isOpen) return;

                string facing = owner.Block?.Variant?["facing"] ?? "south";
                bool isEW = facing == "east" || facing == "west";
                float ewTweak = modSys.GetPlateBoxEwRightOffset();

                if (isEW) { if (!EnsureSlotMeshEW()) return; }
                else      { if (!EnsureSlotMesh())   return; }

                MeshRef? activeMesh = isEW ? slotMeshRefEW : slotMeshRef;
                if (activeMesh == null || activeMesh.Disposed || !activeMesh.Initialized) return;

                EntityPlayer? player = capi.World?.Player?.Entity;
                if (player?.CameraPos == null) return;

                string?[] slotPaths = owner.SnapshotSlotCodePaths();

                IStandardShaderProgram prog = capi.Render.PreparedStandardShader(owner.Pos.X, owner.Pos.Y, owner.Pos.Z);
                bool cullDisabled = false;
                try
                {
                    if (capi.BlockTextureAtlas.AtlasTextures.Count <= 0) return;

                    int atlasTextureId = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;
                    if (atlasTextureId == 0) return;

                    prog.Tex2D = atlasTextureId;
                    prog.ViewMatrix = capi.Render.CameraMatrixOriginf;
                    prog.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;

                    capi.Render.GlDisableCullFace();
                    cullDisabled = true;

                    for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
                    {
                        string? path = slotPaths[slotIndex];
                        if (string.IsNullOrEmpty(path)) continue;

                        prog.RgbaTint = GetSlotTintByPath(path);

                        Vec3f origin = SlotOrigins[slotIndex];
                        float totalX = origin.X + SlotPlateOffset.X;
                        float totalZ = origin.Z + SlotPlateOffset.Z;
                        (float wx, float wz) = TransformForFacing(totalX, totalZ, facing);

                        // GetCube() returns a [-1,+1]³ cube centered at origin; Scale() doubles the value,
                        // so SlotPlateWidth etc. are half-extents. The translate point is the plate center.
                        //
                        // South/north: plate X center = totalX = origin.X + SlotPlateOffset.X (as tuned).
                        // East/west: plate Z center = TransformForFacing(totalX,...) = wz.
                        //
                        // The slot X-center (south) = (1.5+2.0)/2/16 = 1.75/16.
                        // totalX places the plate center at origin.X + 0.225/16 = 1.725/16, intentionally
                        // 0.025/16 = SlotPlateWidth*0.1 off-center toward the low wall — matching south.
                        // East needs the identical relative nudge in +Z: add SlotPlateWidth*0.1 so the
                        // plate sits in the same relative position within   the rotated slot as it does in south.
                        // West: previously needed -SlotPlateWidth*0.5 to counter an over-rotation artifact.
                        // Additional EW tweak comes from `.collodion pose plateboxew ...` so alignment can
                        // be tuned live in-game without recompiling.
                        float ewBaseShift = SlotPlateWidth * 0.6f;
                        float drawX = wx;
                        float drawZ = facing == "east" ? wz + ewBaseShift + ewTweak
                                    : facing == "west" ? wz - ewBaseShift - ewTweak
                                    : wz;

                        modelMat.Identity()
                            .Translate(
                                (float)(owner.Pos.X - player.CameraPos.X) + drawX,
                                (float)(owner.Pos.Y - player.CameraPos.Y) + origin.Y + SlotPlateOffset.Y,
                                (float)(owner.Pos.Z - player.CameraPos.Z) + drawZ
                            );

                        prog.ModelMatrix = modelMat.Values;
                        capi.Render.RenderMesh(activeMesh);
                    }
                }
                finally
                {
                    if (cullDisabled)
                    {
                        capi.Render.GlEnableCullFace();
                    }

                    prog.Stop();
                }
            }

            private bool EnsureSlotMesh()
            {
                if (slotMeshRef != null && !slotMeshRef.Disposed && slotMeshRef.Initialized)
                {
                    return true;
                }

                slotMeshRef?.Dispose();
                slotMeshRef = null;

                try
                {
                    ITexPositionSource source = capi.Tesselator.GetTextureSource(owner.Block);
                    TextureAtlasPosition texPos = source["plate"];
                    if (texPos == null || texPos == capi.BlockTextureAtlas.UnknownTexturePosition) return false;

                    MeshData mesh = CubeMeshUtil.GetCube();
                    mesh = mesh.WithTexPos(texPos);
                    mesh.Scale(new Vec3f(0f, 0f, 0f), SlotPlateWidth, SlotPlateHeight, SlotPlateDepth);
                    mesh.Rgba?.Fill((byte)255);

                    slotMeshRef = capi.Render.UploadMesh(mesh);
                    return slotMeshRef != null;
                }
                catch
                {
                    return false;
                }
            }

            private bool EnsureSlotMeshEW()
            {
                if (slotMeshRefEW != null && !slotMeshRefEW.Disposed && slotMeshRefEW.Initialized)
                {
                    return true;
                }

                slotMeshRefEW?.Dispose();
                slotMeshRefEW = null;

                try
                {
                    ITexPositionSource source = capi.Tesselator.GetTextureSource(owner.Block);
                    TextureAtlasPosition texPos = source["plate"];
                    if (texPos == null || texPos == capi.BlockTextureAtlas.UnknownTexturePosition) return false;

                    MeshData mesh = CubeMeshUtil.GetCube();
                    mesh = mesh.WithTexPos(texPos);
                    // Swap X and Z so the plate stands thin in Z and has depth in X — correct for E/W boxes.
                    mesh.Scale(new Vec3f(0f, 0f, 0f), SlotPlateDepth, SlotPlateHeight, SlotPlateWidth);
                    mesh.Rgba?.Fill((byte)255);

                    slotMeshRefEW = capi.Render.UploadMesh(mesh);
                    return slotMeshRefEW != null;
                }
                catch
                {
                    return false;
                }
            }

            private static Vec4f GetSlotTintByPath(string path)
            {
                if (path.Contains("silveredplate", StringComparison.OrdinalIgnoreCase)) return new Vec4f(0.96f, 0.96f, 0.98f, 1f);
                if (path.Contains("exposedplate", StringComparison.OrdinalIgnoreCase)) return new Vec4f(0.80f, 0.76f, 0.72f, 1f);
                if (path.Contains("developedplate", StringComparison.OrdinalIgnoreCase)) return new Vec4f(0.64f, 0.59f, 0.55f, 1f);
                if (path.Contains("finishedphotoplate", StringComparison.OrdinalIgnoreCase)) return new Vec4f(0.47f, 0.45f, 0.43f, 1f);
                if (path.Contains("collodioncoatedplate", StringComparison.OrdinalIgnoreCase)) return new Vec4f(0.88f, 0.86f, 0.84f, 1f);
                if (path.Contains("cleanglassplate", StringComparison.OrdinalIgnoreCase)) return new Vec4f(0.94f, 0.96f, 0.98f, 1f);
                if (path.Contains("roughglassplate", StringComparison.OrdinalIgnoreCase)) return new Vec4f(0.80f, 0.82f, 0.86f, 1f);
                return new Vec4f(0.92f, 0.92f, 0.92f, 1f);
            }

            public void Dispose()
            {
                slotMeshRef?.Dispose();
                slotMeshRef = null;
                slotMeshRefEW?.Dispose();
                slotMeshRefEW = null;
            }

            /// <summary>Forward-rotate an XZ slot position from south-model space to world space for the given facing.
            /// Matches VS Mat4f.RotateY convention (positive = CW viewed from above).
            /// 90°CW:  (x,z) → (1-z, x) = (cx-dz, cx+dx)
            /// 180°:   (x,z) → (1-x, 1-z) = (cx-dx, cx-dz)
            /// 270°CW: (x,z) → (z, 1-x)  = (cx+dz, cx-dx)
            /// </summary>
            private static (float, float) TransformForFacing(float x, float z, string facing)
            {
                float cx = 0.5f;
                float dx = x - cx, dz = z - cx;
                return facing switch
                {
                    "east"  => (cx - dz, cx + dx),  // 90°CW:  (1-z, x)
                    "north" => (cx + dx, cx - dz),  // 180°+X-flip: (x, 1-z)
                    "west"  => (cx + dz, cx - dx),  // 270°CW: (z, 1-x)
                    _       => (cx - dx, cx + dz)   // south X-flip: (1-x, z)
                };
            }
        }
    }
}

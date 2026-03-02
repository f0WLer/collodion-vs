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
            private readonly Matrixf modelMat = new Matrixf();
            private MeshRef? slotMeshRef;

            public PlateBoxSlotRenderer(ICoreClientAPI capi, BlockEntityPlateBox owner)
            {
                this.capi = capi;
                this.owner = owner;
            }

            public double RenderOrder => 0.5;
            public int RenderRange => 64;

            public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
            {
                if (stage != EnumRenderStage.Opaque) return;
                if (owner.Api?.Side != EnumAppSide.Client) return;

                string blockPath = owner.Block?.Code?.Path ?? string.Empty;
                bool isOpen = owner.IsOpen || blockPath.Equals("platebox-open", StringComparison.OrdinalIgnoreCase);
                if (!isOpen) return;

                if (!EnsureSlotMesh()) return;
                if (slotMeshRef == null || slotMeshRef.Disposed || !slotMeshRef.Initialized) return;

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
                        modelMat.Identity().Translate(
                            (float)(owner.Pos.X - player.CameraPos.X) + origin.X + SlotPlateOffset.X,
                            (float)(owner.Pos.Y - player.CameraPos.Y) + origin.Y + SlotPlateOffset.Y,
                            (float)(owner.Pos.Z - player.CameraPos.Z) + origin.Z + SlotPlateOffset.Z
                        );

                        prog.ModelMatrix = modelMat.Values;
                        capi.Render.RenderMesh(slotMeshRef);
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
            }
        }
    }
}

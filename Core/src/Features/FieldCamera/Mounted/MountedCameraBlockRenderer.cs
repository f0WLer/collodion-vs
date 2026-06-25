using Photochemistry.CameraCapture;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Photochemistry.FieldCamera
{
    // Renders the mounted camera block entity mesh during EnumRenderStage.Opaque using the
    // standard block shader.  Unlike chunk-tessellated geometry this renderer can check
    // ViewportExposureSuppressContext at frame time and skip, ensuring the camera the local
    // player is shooting through never appears in their own virtual capture output (other
    // mounted cameras still render so they show up in the exposure).
    internal sealed class MountedCameraBlockRenderer : IRenderer
    {
        private const float MaxBaseTranslate  = 5f/ 16f; // outer pole travels 4 model units
        private const float MaxInnerTranslate = 7f / 16f; // inner pole travels 5 more model units

        private static readonly string[] _baseMeshIgnore = [
            "CameraRoot", "BottomBoard", "SlingClip1", "SlingClip2",
            "FrontBoard", "BackBoard", "BackSide1", "BackSide2", "BackCover", "MetalNib1", "MetalNib2",
            "Bellows1", "Bellows2", "Bellows3", "Bellows4",
            "KnobPlate", "KnobStem", "Knob",
            "LensHousingPlate", "LensHousing", "LensPiece", "TripodMount",
            "Tripod-PoleBase", "Tripod-PoleInner"
        ];
        private static readonly string[] _poleBaseMeshIgnore = [
            "CameraRoot", "BottomBoard", "SlingClip1", "SlingClip2",
            "FrontBoard", "BackBoard", "BackSide1", "BackSide2", "BackCover", "MetalNib1", "MetalNib2",
            "Bellows1", "Bellows2", "Bellows3", "Bellows4",
            "KnobPlate", "KnobStem", "Knob",
            "LensHousingPlate", "LensHousing", "LensPiece", "TripodMount",
            "Arm Hub", "HingeBackRight", "MountBR", "LegBR", "FootBR",
            "HingeBackLeft", "MountBL", "LegBL", "FootBL",
            "HingeFront", "MountF", "LegF", "FootF",
            // exclude inner pole so PoleBase geometry tessellates alone
            "Tripod-PoleInner"
        ];
        private static readonly string[] _poleInnerSelect = ["Tripod-PoleInner"];
        private static readonly string[] _cameraMeshIgnore = [
            "Arm Hub", "HingeBackRight", "MountBR", "LegBR", "FootBR",
            "HingeBackLeft", "MountBL", "LegBL", "FootBL",
            "HingeFront", "MountF", "LegF", "FootF",
            "Tripod-PoleBase", "Tripod-PoleInner"
        ];

        private readonly ICoreClientAPI _capi;
        private readonly BlockPos _pos;
        private readonly Block _block;
        private float _facingYaw;
        private float _subOffsetX;
        private float _subOffsetZ;
        private float _heightOffset;
        private bool _isExposing;
        private Shape? _idleShape;
        private Shape? _exposingShape;
        private MultiTextureMeshRef? _meshRef;
        private bool _meshDirty = true;
        private bool _disposed;

        private readonly Matrixf _modelMat = new Matrixf();

        public double RenderOrder => 0.4;
        public int RenderRange => 0; // unlimited — chunk presence already constrains load distance

        internal MountedCameraBlockRenderer(ICoreClientAPI capi, BlockPos pos, Block block, float facingYaw, float subOffsetX, float subOffsetZ, bool isExposing)
        {
            _capi = capi;
            _pos = pos.Copy();
            _block = block;
            _facingYaw = facingYaw;
            _subOffsetX = subOffsetX;
            _subOffsetZ = subOffsetZ;
            _isExposing = isExposing;
        }

        internal void SetFacingYaw(float yaw)
        {
            _facingYaw = yaw;
            _meshDirty = true;
        }

        internal void SetSubBlockOffset(float x, float z)
        {
            _subOffsetX = x;
            _subOffsetZ = z;
        }

        internal void SetHeightOffset(float h)
        {
            if (Math.Abs(_heightOffset - h) < 0.001f) return;
            _heightOffset = h;
            _meshDirty = true;
        }

        internal void SetExposing(bool isExposing)
        {
            if (_isExposing == isExposing) return;
            _isExposing = isExposing;
            _meshDirty = true;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (_disposed) return;

            // Hide only the camera the local player is shooting through, not every mounted camera.
            // Compare coordinates rather than BlockPos.Equals so a dimension-0 reconstructed pos from
            // the network still matches this block entity's position.
            if (ViewportExposureSuppressContext.IsVirtualRender
                && ViewportExposureSuppressContext.ActiveMountedCameraPos is BlockPos activePos
                && activePos.X == _pos.X && activePos.Y == _pos.Y && activePos.Z == _pos.Z) return;

            if (_meshDirty) RebuildMesh();
            if (_meshRef == null) return;

            Vec3d camPos = _capi.World.Player.Entity.CameraPos;
            Vec4f light = _capi.World.BlockAccessor.GetLightRGBs(_pos.X, _pos.Y, _pos.Z);

            IStandardShaderProgram prog = _capi.Render.PreparedStandardShader(_pos.X, _pos.Y, _pos.Z);
            prog.ViewMatrix = _capi.Render.CameraMatrixOriginf;
            prog.ProjectionMatrix = _capi.Render.CurrentProjectionMatrix;
            prog.RgbaLightIn = light;
            prog.ModelMatrix = _modelMat.Identity()
                .Translate(_pos.X + _subOffsetX - camPos.X, _pos.Y - camPos.Y, _pos.Z + _subOffsetZ - camPos.Z)
                .Values;

            _capi.Render.RenderMultiTextureMesh(_meshRef, "tex");
            prog.Stop();
        }

        private void RebuildMesh()
        {
            _meshRef?.Dispose();
            _meshRef = null;
            _meshDirty = false;

            try
            {
                Shape? shape = GetShape();
                if (shape == null) return;

                MeshData mesh = BuildStretchedMesh(shape);
                mesh.Rotate(0f, _facingYaw, 0f);
                _meshRef = _capi.Render.UploadMultiTextureMesh(mesh);
            }
            catch (Exception ex)
            {
                _capi.Logger.Warning("photochemistry: MountedCameraBlockRenderer mesh build failed at {0}: {1}", _pos, ex.Message);
            }
        }

        private Shape? GetShape()
        {
            AssetLocation shapeBase = _block.Shape.Base;
            if (_isExposing)
            {
                _exposingShape ??= _capi.Assets.TryGet(new AssetLocation(shapeBase.Domain, $"shapes/{shapeBase.Path}.json"))?.ToObject<Shape>();
                return _exposingShape;
            }
            _idleShape ??= _capi.Assets.TryGet(new AssetLocation(shapeBase.Domain, $"shapes/{shapeBase.Path}-idle.json"))?.ToObject<Shape>();
            return _idleShape;
        }

        private MeshData BuildStretchedMesh(Shape shape)
        {
            ITexPositionSource texSource = _capi.Tesselator.GetTextureSource(_block);

            MeshData TessIgnore(string[] ignore)
            {
                var meta = new TesselationMetaData
                {
                    TypeForLogging = "photochemistry-mounted-camera",
                    TexSource = texSource,
                    IgnoreElements = ignore
                };
                _capi.Tesselator.TesselateShape(meta, shape, out MeshData m);
                return m;
            }

            MeshData TessSelect(string[] select)
            {
                var meta = new TesselationMetaData
                {
                    TypeForLogging = "photochemistry-mounted-camera",
                    TexSource = texSource,
                    SelectiveElements = select
                };
                _capi.Tesselator.TesselateShape(meta, shape, out MeshData m);
                return m;
            }

            MeshData baseMesh      = TessIgnore(_baseMeshIgnore);    // Arm Hub + legs (fixed)
            MeshData poleBaseMesh  = TessIgnore(_poleBaseMeshIgnore);// outer pole only
            MeshData poleInnerMesh = TessSelect(_poleInnerSelect);   // inner pole only (root element)
            MeshData cameraMesh    = TessIgnore(_cameraMeshIgnore);  // camera body + TripodMount

            // CameraRoot rests on PoleInner's top — measure natural maxY so heightDelta is 0
            // at rest and positive only when extension is needed.
            float poleInnerMaxY = float.MinValue;
            for (int v = 0; v < poleInnerMesh.VerticesCount; v++)
            {
                float y = poleInnerMesh.xyz[v * 3 + 1];
                if (y > poleInnerMaxY) poleInnerMaxY = y;
            }
            // Fallback: if PoleInner produced no geometry, measure from PoleBase top instead.
            if (poleInnerMaxY == float.MinValue)
            {
                for (int v = 0; v < poleBaseMesh.VerticesCount; v++)
                {
                    float y = poleBaseMesh.xyz[v * 3 + 1];
                    if (y > poleInnerMaxY) poleInnerMaxY = y;
                }
            }

            float heightDelta = poleInnerMaxY > float.MinValue ? _heightOffset - poleInnerMaxY : 0f;

            if (heightDelta > 0.001f)
            {
                float baseTranslate  = Math.Min(heightDelta, MaxBaseTranslate);
                float innerTranslate = Math.Clamp(heightDelta - MaxBaseTranslate, 0f, MaxInnerTranslate);
                float totalTranslate = baseTranslate + innerTranslate;

                for (int v = 0; v < poleBaseMesh.VerticesCount; v++)
                    poleBaseMesh.xyz[v * 3 + 1] += baseTranslate;

                // PoleInner rides with PoleBase and then extends further.
                for (int v = 0; v < poleInnerMesh.VerticesCount; v++)
                    poleInnerMesh.xyz[v * 3 + 1] += totalTranslate;

                // Camera sits on PoleInner's top — same total translation.
                for (int v = 0; v < cameraMesh.VerticesCount; v++)
                    cameraMesh.xyz[v * 3 + 1] += totalTranslate;
            }

            baseMesh.AddMeshData(poleBaseMesh);
            baseMesh.AddMeshData(poleInnerMesh);
            baseMesh.AddMeshData(cameraMesh);
            return baseMesh;
        }

        public void Dispose()
        {
            _disposed = true;
            _meshRef?.Dispose();
            _meshRef = null;
        }
    }
}

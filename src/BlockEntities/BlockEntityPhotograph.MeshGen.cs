using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public partial class BlockEntityPhotograph
    {
    // Plate/frame visible area is 5w x 5.5h => aspect = 10/11.
    private const float PhotoTargetAspect = 10f / 11f;
    private static readonly string[] PreferredPlankTextureKeys = { "all", "side", "north", "south", "east", "west", "up", "down" };
    private static readonly string[] IgnorePhotoElements = { "Photo", "Photo1", "Photo2" };
    private static readonly string[] IgnoreDoubleFramePrimaryWall = { "Frame2", "Photo", "Photo1", "Photo2" };
    private static readonly string[] IgnoreDoubleFrameSecondaryWall = { "Frame", "Photo", "Photo1", "Photo2" };
    private static readonly string[] IgnoreDoubleFramePrimaryGround = { "Frame2", "Photo", "Photo1", "Photo2" };
    private static readonly string[] IgnoreDoubleFrameSecondaryGround = { "Frame1", "Photo", "Photo1", "Photo2" };
    private static readonly AssetLocation FallbackFramedPhotographGround = new AssetLocation("collodion", "framedphotographground");

        private sealed class SingleTextureSource : ITexPositionSource
        {
            private readonly TextureAtlasPosition texPos;
            private readonly Size2i atlasSize;

            public SingleTextureSource(TextureAtlasPosition texPos, Size2i atlasSize)
            {
                this.texPos = texPos;
                this.atlasSize = atlasSize;
            }

            public TextureAtlasPosition this[string textureCode] => texPos;

            public Size2i AtlasSize => atlasSize;
        }

        private MeshData? GenerateFrameMeshForBlock(ICoreClientAPI capi)
        {
            string path = Block?.Code?.Path ?? string.Empty;
            if (!path.StartsWith("framedphotograph", StringComparison.OrdinalIgnoreCase)) return null;

            var shape = Block?.Shape;
            if (shape == null) return null;

            bool isDoubleWall = path.StartsWith("framedphotographwall2", StringComparison.OrdinalIgnoreCase);
            bool isDoubleGround = path.StartsWith("framedphotographground2", StringComparison.OrdinalIgnoreCase);
            bool isDouble = isDoubleWall || isDoubleGround;

            if (isDouble)
            {
                MeshData? combined = null;

                string[] primaryIgnore = isDoubleWall ? IgnoreDoubleFramePrimaryWall : IgnoreDoubleFramePrimaryGround;
                string[] secondaryIgnore = isDoubleWall ? IgnoreDoubleFrameSecondaryWall : IgnoreDoubleFrameSecondaryGround;

                MeshData? part1 = GenerateFrameMeshPart(capi, shape, FramePlankBlockCode, primaryIgnore);

                if (part1 != null)
                {
                    combined = part1;
                }

                MeshData? part2 = GenerateFrameMeshPart(capi, shape, FramePlankBlockCode2, secondaryIgnore);

                if (part2 != null)
                {
                    if (combined == null)
                    {
                        combined = part2;
                    }
                    else
                    {
                        combined.AddMeshData(part2);
                    }
                }

                return combined;
            }

            // Wall frames include a built-in "Photo" panel in the base shape.
            // When we re-tesselate the whole shape using a single plank texture, that panel becomes a solid plank and
            // can fully cover the actual photo overlay plane. Excluding it matches the ground frame behavior.
            if (path.StartsWith("framedphotographwall", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(FramePlankBlockCode))
            {
                try
                {
                    var cloned = shape.Clone();
                    cloned.IgnoreElements = IgnorePhotoElements;
                    shape = cloned;
                }
                catch
                {
                    // fall back to original shape
                }
            }

            ITexPositionSource texSource;
            try
            {
                // Default: render the oak frame as-authored.
                texSource = capi.Tesselator.GetTextureSource(Block);
            }
            catch
            {
                return null;
            }

            // Optional: if a plank has been applied, re-map all frame textures to that plank.
            string? plankCode = FramePlankBlockCode;
            if (TryGetPlankTextureSource(capi, plankCode, out ITexPositionSource? mappedTexSource))
            {
                texSource = mappedTexSource;
            }

            try
            {
                capi.Tesselator.TesselateShape(
                    "collodion-frame",
                    Block?.Code ?? FallbackFramedPhotographGround,
                    shape,
                    out MeshData frameMesh,
                    texSource
                );

                return frameMesh;
            }
            catch
            {
                return null;
            }
        }

        private MeshData? GenerateFrameMeshPart(ICoreClientAPI capi, CompositeShape shape, string? plankCode, string[] ignoreElements)
        {
            CompositeShape localShape;
            try
            {
                localShape = shape.Clone();
                localShape.IgnoreElements = ignoreElements;
            }
            catch
            {
                return null;
            }

            ITexPositionSource texSource;
            try
            {
                texSource = capi.Tesselator.GetTextureSource(Block);
            }
            catch
            {
                return null;
            }

            if (TryGetPlankTextureSource(capi, plankCode, out ITexPositionSource? mappedTexSource))
            {
                texSource = mappedTexSource;
            }

            try
            {
                capi.Tesselator.TesselateShape(
                    "collodion-frame-part",
                    Block?.Code ?? FallbackFramedPhotographGround,
                    localShape,
                    out MeshData frameMesh,
                    texSource
                );

                return frameMesh;
            }
            catch
            {
                return null;
            }
        }

        private bool TryGetPlankTextureSource(ICoreClientAPI capi, string? plankCode, out ITexPositionSource texSource)
        {
            texSource = null!;
            if (string.IsNullOrWhiteSpace(plankCode)) return false;

            try
            {
                Block? plankBlock = capi.World.GetBlock(new AssetLocation(plankCode));
                if (plankBlock == null || plankBlock.Id == 0) return false;

                ITexPositionSource plankTexSource = capi.Tesselator.GetTextureSource(plankBlock);
                TextureAtlasPosition chosenTexPos = capi.BlockTextureAtlas.UnknownTexturePosition;

                foreach (string key in PreferredPlankTextureKeys)
                {
                    try
                    {
                        TextureAtlasPosition pos = plankTexSource[key];
                        if (pos != null && pos != capi.BlockTextureAtlas.UnknownTexturePosition)
                        {
                            chosenTexPos = pos;
                            break;
                        }
                    }
                    catch { }
                }

                if (chosenTexPos == capi.BlockTextureAtlas.UnknownTexturePosition && plankBlock.Textures != null)
                {
                    foreach (string key in plankBlock.Textures.Keys)
                    {
                        try
                        {
                            TextureAtlasPosition pos = plankTexSource[key];
                            if (pos != null && pos != capi.BlockTextureAtlas.UnknownTexturePosition)
                            {
                                chosenTexPos = pos;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                if (chosenTexPos == capi.BlockTextureAtlas.UnknownTexturePosition) return false;

                texSource = new SingleTextureSource(chosenTexPos, capi.BlockTextureAtlas.Size);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private MeshData GeneratePhotoMeshForBlock(ICoreClientAPI capi, TextureAtlasPosition texPos, float photoAspect, int photoSlot)
        {
            string path = Block?.Code?.Path ?? string.Empty;

            if (path.StartsWith("framedphotographwall2", StringComparison.OrdinalIgnoreCase))
            {
                string attrName = photoSlot == 2 ? "photoshape2" : "photoshape";
                if (TryGetPhotoPlaneMeshFromBlockAttribute(capi, texPos, photoAspect, attrName, out MeshData planeMesh))
                {
                    return planeMesh;
                }

                // Fallback to single-photo placement if authored element lookup fails.
                return GenerateFramedWallPhotoMesh(capi, texPos, photoAspect);
            }

            if (path.StartsWith("framedphotographground2", StringComparison.OrdinalIgnoreCase))
            {
                string attrName = photoSlot == 2 ? "photoshape2" : "photoshape";
                if (TryGetPhotoPlaneMeshFromBlockAttribute(capi, texPos, photoAspect, attrName, out MeshData planeMesh))
                {
                    return planeMesh;
                }

                // Fallback to single-photo placement if authored element lookup fails.
                return GenerateFramedGroundPhotoMesh(capi, texPos, photoAspect);
            }

            if (path.StartsWith("framedphotographwall", StringComparison.OrdinalIgnoreCase))
            {
                return GenerateFramedWallPhotoMesh(capi, texPos, photoAspect);
            }

            if (path.StartsWith("framedphotographground", StringComparison.OrdinalIgnoreCase))
            {
                return GenerateFramedGroundPhotoMesh(capi, texPos, photoAspect);
            }

            return GenerateMountedPlateMesh(capi, texPos, photoAspect);
        }

        private MeshData GenerateFramedWallPhotoMesh(ICoreClientAPI capi, TextureAtlasPosition texPos, float photoAspect)
        {
            // KosPhotography-style: tesselate a dedicated "photo plane" block mesh defined by the frame block.
            // This avoids terrain-pipeline edge cases and lets model authors control exact placement.
            if (TryGetPhotoPlaneMeshFromBlockAttribute(capi, texPos, photoAspect, "photoshape", out MeshData planeMesh))
            {
                return planeMesh;
            }

            // Fallback: centered quad (kept only as a safety net).
            const float insetVox = 2f;
            float x = insetVox / 16f;
            float y = insetVox / 16f;
            float w = 1f - (insetVox / 16f) * 2f;
            float h = 1f - (insetVox / 16f) * 2f;
            float z = 2.01f / 16f;

            float[] xyz = new float[]
            {
                x,     y,     z,
                x + w, y,     z,
                x + w, y + h, z,
                x,     y + h, z
            };

            MeshData mesh = CreateQuadMeshFromXyz(xyz, "south");
            mesh = mesh.WithTexPos(texPos);

            // Ensure we sample the correct atlas region and crop to target aspect.
            int uvRotationDeg = 0;
            try
            {
                uvRotationDeg = Block?.Attributes?["photoUvRotation"]?.AsInt(0) ?? 0;
            }
            catch
            {
                uvRotationDeg = 0;
            }
            StampUvByRotationCropped(mesh, texPos, uvRotationDeg, photoAspect, PhotoTargetAspect);

            float yawDeg = GetYawDegFromBlockVariant();
            if (Math.Abs(yawDeg) > 0.001f)
            {
                mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, yawDeg * GameMath.DEG2RAD, 0f);
            }

            return mesh;
        }

        private MeshData GenerateFramedGroundPhotoMesh(ICoreClientAPI capi, TextureAtlasPosition texPos, float photoAspect)
        {
            if (TryGetPhotoPlaneMeshFromBlockAttribute(capi, texPos, photoAspect, "photoshape", out MeshData planeMesh))
            {
                return planeMesh;
            }

            // Fallback: centered horizontal quad.
            const float insetVox = 2f;
            const float yVox = 2.01f;
            float x = insetVox / 16f;
            float z = insetVox / 16f;
            float w = 1f - (insetVox / 16f) * 2f;
            float len = 1f - (insetVox / 16f) * 2f;
            float y = yVox / 16f;

            float[] xyz = new float[]
            {
                x,     y, z,
                x + w, y, z,
                x + w, y, z + len,
                x,     y, z + len
            };

            MeshData mesh = CreateQuadMeshFromXyz(xyz, "up");
            mesh = mesh.WithTexPos(texPos);

            // Ensure we sample the correct atlas region and crop to target aspect.
            int uvRotationDeg = 0;
            try
            {
                uvRotationDeg = Block?.Attributes?["photoUvRotation"]?.AsInt(0) ?? 0;
            }
            catch
            {
                uvRotationDeg = 0;
            }
            StampUvByRotationCropped(mesh, texPos, uvRotationDeg, photoAspect, PhotoTargetAspect);

            float yawDeg = GetYawDegFromBlockVariant();
            if (Math.Abs(yawDeg) > 0.001f)
            {
                mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, yawDeg * GameMath.DEG2RAD, 0f);
            }

            return mesh;
        }

        private bool TryGetPhotoPlaneMeshFromBlockAttribute(ICoreClientAPI capi, TextureAtlasPosition texPos, float photoAspect, string attrName, out MeshData mesh)
        {
            mesh = default!;

            try
            {
                string? photoshape = Block?.Attributes?[attrName]?.AsString(null);
                if (string.IsNullOrWhiteSpace(photoshape))
                {
                    lock (clientMeshLock) clientOverlayInfo = $"{attrName}=<missing>";
                    return false;
                }

                string side = Block?.LastCodePart() ?? "north";

                AssetLocation baseLoc;
                if (photoshape.Contains(":", StringComparison.Ordinal))
                {
                    baseLoc = new AssetLocation(photoshape);
                }
                else
                {
                    baseLoc = new AssetLocation(Block?.Code?.Domain ?? "collodion", photoshape);
                }

                AssetLocation variantLoc = new AssetLocation(baseLoc.Domain, $"{baseLoc.Path}-{side}");
                Block overlayBlock = capi.World.GetBlock(variantLoc);
                if (overlayBlock == null || overlayBlock.Id == 0)
                {
                    lock (clientMeshLock) clientOverlayInfo = $"{attrName}={photoshape} variant={variantLoc} overlayBlock=<missing>";
                    return false;
                }

                // GetDefaultBlockMesh can return a cached/shared MeshData instance.
                // Always clone before mutating (WithTexPos / UV stamping), otherwise data can bleed across blocks.
                MeshData planeMesh = capi.TesselatorManager.GetDefaultBlockMesh(overlayBlock).Clone();
                planeMesh = planeMesh.WithTexPos(texPos);

                int uvRotationDeg = 0;
                try
                {
                    uvRotationDeg = Block?.Attributes?["photoUvRotation"]?.AsInt(0) ?? 0;
                }
                catch
                {
                    uvRotationDeg = 0;
                }

                // Ensure we sample the correct atlas region (and optionally rotate the photo).
                StampUvByRotationCropped(planeMesh, texPos, uvRotationDeg, photoAspect, PhotoTargetAspect);

                lock (clientMeshLock)
                {
                    clientOverlayInfo = $"{attrName}={photoshape} variant={variantLoc} overlayBlock={overlayBlock.Code} verts={planeMesh.VerticesCount} indices={planeMesh.IndicesCount} uvRotation={((uvRotationDeg % 360) + 360) % 360}";
                }
                mesh = planeMesh;
                return true;
            }
            catch
            {
                lock (clientMeshLock) clientOverlayInfo = $"{attrName}=<exception>";
                return false;
            }
        }

        private MeshData GenerateMountedPlateMesh(ICoreClientAPI capi, TextureAtlasPosition texPos, float photoAspect)
        {
            MeshData mesh;
            try
            {
                // Use the game's default block mesh so the vertex attribute layout matches terrain rendering expectations.
                // (This mirrors the approach used by the working kosphotography reference mod.)
                Block? meshBlock = Block;
                if (meshBlock == null)
                {
                    return CubeMeshUtil.GetCube();
                }

                // Important: default block meshes for rotated variants may already be pre-rotated by the engine.
                // We want a consistent unrotated base mesh (north), then apply our own yaw based on the current variant.
                try
                {
                    string path = meshBlock.Code?.Path ?? string.Empty;
                    int dash = path.LastIndexOf('-');
                    if (dash > 0)
                    {
                        if (meshBlock.Code != null)
                        {
                            AssetLocation northLoc = new AssetLocation(meshBlock.Code.Domain, path.Substring(0, dash) + "-north");
                            Block? maybeNorth = capi.World?.GetBlock(northLoc);
                            if (maybeNorth != null) meshBlock = maybeNorth;
                        }
                    }
                }
                catch { }

                mesh = capi.TesselatorManager.GetDefaultBlockMesh(meshBlock);
                // IMPORTANT: GetDefaultBlockMesh can return a cached/shared MeshData instance.
                // We must clone before mutating (scale/rotate/uv), otherwise transforms accumulate over time.
                mesh = mesh.Clone();
            }
            catch
            {
                mesh = CubeMeshUtil.GetCube();
            }

            // Make a thin plate, inset from edges a bit.
            // Start with a "north"-facing plate near z=0, then rotate based on the block variant.
            const float inset = 0.02f;
            const float thickness = 1f / 16f;
            float scaleXY = 1f - inset * 2f;

            mesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), scaleXY, scaleXY, thickness);

            // Move plate to z ~= 0..thickness (slightly inwards to avoid z-fighting).
            const float zEpsilon = 0.001f;
            float translateZ = zEpsilon - 0.5f + (thickness * 0.5f);
            mesh.Translate(0f, 0f, translateZ);

            // Apply facing rotation using the same convention as the JSON selectionbox rotateYByType.
            float yawDeg = GetYawDegFromBlockVariant();
            if (Math.Abs(yawDeg) > 0.001f)
            {
                mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, yawDeg * GameMath.DEG2RAD, 0f);
            }

            // Bind to the block texture atlas position (kosphotography pattern).
            mesh = mesh.WithTexPos(texPos);

            // Explicitly stamp UVs so we actually sample the photo region.
            StampUvByRotationCropped(mesh, texPos, 90, photoAspect, PhotoTargetAspect);

            return mesh;
        }

        private float GetYawDegFromBlockVariant()
        {
            // Default matches *-north: 0
            try
            {
                string side = Block?.Variant?["side"] ?? string.Empty;
                if (string.IsNullOrEmpty(side)) return 0f;

                // Mirror the JSON rotateYByType values:
                // east=270, south=180, west=90, north=0
                switch (side.ToLowerInvariant())
                {
                    case "east":
                        return 270f;
                    case "south":
                        return 180f;
                    case "west":
                        return 90f;
                    case "north":
                    default:
                        return 0f;
                }
            }
            catch
            {
                return 0f;
            }
        }

        // (Old photoOverlay JSON-driven quad config removed; we now use the kosphotography-style `photoshape` plane.)
    }
}

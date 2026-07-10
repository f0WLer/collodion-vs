using Vintagestory.API.Client;

namespace Photocore.Plates.Rendering
{
    internal sealed class PhotoMeshRenderCache
    {
        private sealed class CachedRender
        {
            internal MultiTextureMeshRef MeshRef { get; }
            internal int TextureId { get; }

            internal CachedRender(MultiTextureMeshRef meshRef, int textureId)
            {
                MeshRef = meshRef;
                TextureId = textureId;
            }
        }

        private readonly object _syncRoot = new();
        private readonly Dictionary<string, CachedRender> _meshCache = new(StringComparer.OrdinalIgnoreCase);
        private int _atlasVersion;

        internal int GetAtlasVersionSnapshot()
        {
            lock (_syncRoot)
            {
                return _atlasVersion;
            }
        }

        internal bool TryGetCachedRender(string cacheKey, out MultiTextureMeshRef? meshRef, out int textureId)
        {
            lock (_syncRoot)
            {
                if (_meshCache.TryGetValue(cacheKey, out CachedRender? cached) && cached != null)
                {
                    meshRef = cached.MeshRef;
                    textureId = cached.TextureId;
                    return true;
                }
            }

            meshRef = null;
            textureId = 0;
            return false;
        }

        internal bool TryStore(string cacheKey, int versionSnapshot, MultiTextureMeshRef meshRef, int textureId)
        {
            lock (_syncRoot)
            {
                if (_atlasVersion != versionSnapshot)
                {
                    return false;
                }

                _meshCache[cacheKey] = new CachedRender(meshRef, textureId);
                return true;
            }
        }

        internal int ClearAndBumpVersion()
        {
            lock (_syncRoot)
            {
                int cleared = DisposeEntriesAndClear();
                _atlasVersion++;
                return cleared;
            }
        }

        // Drops just one photo's cached meshes, leaving every other photo's alone. Callers use this instead
        // of ClearAndBumpVersion when only one photo's pixels became stale: bumping the version rewrites the
        // atlas texture key for every photo, and the game's atlas has no way to re-upload into an existing
        // region, so each bump re-allocates every subsequently rendered photo and orphans its old region.
        internal int RemoveForPhoto(string photoFileName)
        {
            if (string.IsNullOrEmpty(photoFileName)) return 0;

            // Mesh keys are "{photoFileName}|{variant}|..." — the separator keeps a short id from matching
            // a longer one that starts with the same characters.
            string prefix = photoFileName + "|";

            lock (_syncRoot)
            {
                List<string> stale = new();
                foreach (string key in _meshCache.Keys)
                {
                    if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) stale.Add(key);
                }

                foreach (string key in stale)
                {
                    _meshCache[key].MeshRef.Dispose();
                    _meshCache.Remove(key);
                }

                return stale.Count;
            }
        }

        private int DisposeEntriesAndClear()
        {
            int cleared = 0;

            foreach (var kvp in _meshCache)
            {
                kvp.Value.MeshRef.Dispose();
                cleared++;
            }

            _meshCache.Clear();
            return cleared;
        }
    }
}

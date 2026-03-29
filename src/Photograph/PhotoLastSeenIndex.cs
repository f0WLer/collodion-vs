using System;
using System.Collections.Generic;

namespace Collodion
{
    public sealed class PhotoLastSeenIndex
    {
        public Dictionary<string, PhotoLastSeenEntry> Entries = new Dictionary<string, PhotoLastSeenEntry>(StringComparer.OrdinalIgnoreCase);

        public void Touch(string photoId)
        {
            photoId = WetplatePhotoSync.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(photoId)) return;

            string now = DateTime.UtcNow.ToString("o");

            if (!Entries.TryGetValue(photoId, out PhotoLastSeenEntry? entry) || entry == null)
            {
                entry = new PhotoLastSeenEntry
                {
                    FirstSeenUtc = now,
                    LastSeenUtc = now
                };

                Entries[photoId] = entry;
                return;
            }

            if (string.IsNullOrEmpty(entry.FirstSeenUtc)) entry.FirstSeenUtc = now;
            entry.LastSeenUtc = now;
        }

        public void ClampInPlace()
        {
            if (Entries == null)
            {
                Entries = new Dictionary<string, PhotoLastSeenEntry>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            // Remove obviously invalid keys.
            var toRemove = new List<string>();
            foreach (var kvp in Entries)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key)) toRemove.Add(kvp.Key);
            }

            foreach (string k in toRemove)
            {
                Entries.Remove(k);
            }
        }
    }

}

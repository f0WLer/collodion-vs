using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Collodion
{
    public partial class BlockEntityPhotograph : BlockEntity
    {
        public string? PhotoId { get; private set; }

        public string? Caption { get; private set; }

        public string? FramePlankBlockCode { get; private set; }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            ClientRequestMeshRebuild();
        }

        public void SetPhoto(string? photoId)
        {
            string? normalized = string.IsNullOrWhiteSpace(photoId) ? null : photoId;
            if (string.Equals(PhotoId, normalized, StringComparison.Ordinal)) return;

            PhotoId = normalized;
            ClientRequestMeshRebuild();

            MarkDirty(true);

            try
            {
                Api?.World?.BlockAccessor?.MarkBlockEntityDirty(Pos);
            }
            catch { }
        }

        public void SetCaption(string? caption)
        {
            string? normalized = string.IsNullOrWhiteSpace(caption) ? null : caption;
            if (normalized != null && normalized.Length > 200)
            {
                normalized = normalized.Substring(0, 200);
            }

            if (string.Equals(Caption, normalized, StringComparison.Ordinal)) return;

            Caption = normalized;
            MarkDirty(true);

            try
            {
                Api?.World?.BlockAccessor?.MarkBlockEntityDirty(Pos);
            }
            catch { }
        }

        public void SetFramePlankBlockCode(string? blockCode)
        {
            string? normalized = string.IsNullOrWhiteSpace(blockCode) ? null : blockCode;
            if (string.Equals(FramePlankBlockCode, normalized, StringComparison.Ordinal)) return;

            FramePlankBlockCode = normalized;
            ClientRequestMeshRebuild();

            MarkDirty(true);

            try
            {
                Api?.World?.BlockAccessor?.MarkBlockEntityDirty(Pos);
            }
            catch { }
        }


        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            if (!string.IsNullOrEmpty(PhotoId))
            {
                tree.SetString(PhotographAttrs.PhotoId, PhotoId);
            }

            if (!string.IsNullOrEmpty(Caption))
            {
                tree.SetString(PhotographAttrs.Caption, Caption);
            }

            if (!string.IsNullOrEmpty(FramePlankBlockCode))
            {
                tree.SetString(PhotographAttrs.FramePlank, FramePlankBlockCode);
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            PhotoId = tree.GetString(PhotographAttrs.PhotoId);
            Caption = tree.GetString(PhotographAttrs.Caption);
            FramePlankBlockCode = tree.GetString(PhotographAttrs.FramePlank);
            ClientRequestMeshRebuild();
        }

        partial void ClientRequestMeshRebuild();
    }
}

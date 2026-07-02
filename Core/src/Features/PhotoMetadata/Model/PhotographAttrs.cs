using Vintagestory.API.Common;

namespace Photocore.PhotoMetadata.Model
{
    public static class PhotographAttrs
    {
        public const string PhotoId = "photoId";
        public const string Caption = "caption";

        public static string ResolvePhotoId(this ItemStack? stack) => stack?.Attributes?.GetString(PhotoId) ?? string.Empty;
        public static string ResolveCaption(this ItemStack? stack) => stack?.Attributes?.GetString(Caption) ?? string.Empty;
    }
}

using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Collodion
{
    internal sealed class ViewfinderDebugPreviewRenderer : IRenderer
    {
        private readonly ICoreClientAPI capi;
        private readonly PhotoCaptureRenderer captureRenderer;
        private readonly Func<bool> isViewfinderActive;

        private LoadedTexture? previewTexture;
        private long lastPreviewRequestMs;

        public ViewfinderDebugPreviewRenderer(ICoreClientAPI capi, PhotoCaptureRenderer captureRenderer, Func<bool> isViewfinderActive)
        {
            this.capi = capi;
            this.captureRenderer = captureRenderer;
            this.isViewfinderActive = isViewfinderActive;
        }

        private ViewfinderConfig? ViewfinderCfg => CollodionConfigAccess.ResolveClientConfig(capi)?.Viewfinder;

        public double RenderOrder => 0.97;
        public int RenderRange => 0;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.Ortho) return;

            ViewfinderConfig? cfg = ViewfinderCfg;
            if (cfg == null) return;
            if (!cfg.DebugPreviewEnabled) return;
            if (!cfg.DebugPreviewPeak && !isViewfinderActive()) return;

            int refreshMs = cfg.DebugPreviewRefreshMs;
            long nowMs = capi.ElapsedMilliseconds;
            if (nowMs - lastPreviewRequestMs >= refreshMs)
            {
                lastPreviewRequestMs = nowMs;
                captureRenderer.RequestDebugPreviewFrame(cfg.DebugPreviewMaxDimension);
            }

            if (captureRenderer.TryConsumeDebugPreviewFrame(out int[] bgraPixels, out int width, out int height))
            {
                if (previewTexture == null || previewTexture.Width != width || previewTexture.Height != height)
                {
                    previewTexture?.Dispose();
                    previewTexture = new LoadedTexture(capi)
                    {
                        Width = width,
                        Height = height
                    };
                }

                capi.Render.LoadOrUpdateTextureFromBgra(
                    bgraPixels,
                    linearMag: true,
                    clampMode: (int)EnumTextureWrap.ClampToEdge,
                    intoTexture: ref previewTexture);

                // LoadOrUpdateTextureFromBgra updates level 0 via TexSubImage on existing textures.
                // Rebuild mipmaps so lower levels do not keep stale first-frame content.
                capi.Render.BindTexture2d(previewTexture.TextureId);
                capi.Render.GlGenerateTex2DMipmaps();
            }

            if (previewTexture == null || previewTexture.TextureId == 0) return;

            int frameWidth = capi.Render.FrameWidth;
            int frameHeight = capi.Render.FrameHeight;

            int margin = cfg.DebugPreviewMargin;
            int previewWidth = Math.Max(64, Math.Min(cfg.DebugPreviewWidth, Math.Max(64, frameWidth - margin * 2)));
            int previewHeight = Math.Max(64, Math.Min(cfg.DebugPreviewHeight, Math.Max(64, frameHeight - margin * 2)));

            float x = margin;
            float y = margin;

            switch ((cfg.DebugPreviewAnchor ?? "topright").Trim().ToLowerInvariant())
            {
                case "topleft":
                    x = margin;
                    y = margin;
                    break;

                case "bottomleft":
                    x = margin;
                    y = frameHeight - margin - previewHeight;
                    break;

                case "bottomright":
                    x = frameWidth - margin - previewWidth;
                    y = frameHeight - margin - previewHeight;
                    break;

                case "topright":
                default:
                    x = frameWidth - margin - previewWidth;
                    y = margin;
                    break;
            }

            capi.Render.GLDisableDepthTest();
            try
            {
                float boxW = previewWidth;
                float boxH = previewHeight;
                float texAspect = previewTexture.Width > 0 && previewTexture.Height > 0
                    ? previewTexture.Width / (float)previewTexture.Height
                    : (10f / 11f);
                float boxAspect = boxW / Math.Max(1f, boxH);

                float drawW = boxW;
                float drawH = boxH;
                if (texAspect > boxAspect)
                {
                    drawH = boxW / Math.Max(0.0001f, texAspect);
                }
                else
                {
                    drawW = boxH * texAspect;
                }

                float drawX = x + (boxW - drawW) * 0.5f;
                float drawY = y + (boxH - drawH) * 0.5f;

                // Matte fill so letterbox/pillarbox areas are explicit and not stale framebuffer content.
                capi.Render.RenderRectangle(x, y, 49f, boxW, boxH, unchecked((int)0xFF000000));
                capi.Render.Render2DTexture(previewTexture.TextureId, drawX, drawY, drawW, drawH, 50f, ColorUtil.WhiteArgbVec);
                capi.Render.RenderRectangle(x - 1, y - 1, 49f, boxW + 2, boxH + 2, ColorUtil.WhiteArgb);
            }
            finally
            {
                capi.Render.GLEnableDepthTest();
            }
        }

        public void Dispose()
        {
            previewTexture?.Dispose();
            previewTexture = null;
        }
    }
}

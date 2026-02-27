using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Collodion
{
    public class PhotoCaptureRenderer : IRenderer
    {
        private readonly ICoreClientAPI capi;

        private WetplateEffectsConfig effectsConfig = new WetplateEffectsConfig();

        private PendingCapture? pending;
        private readonly object pendingLock = new object();

        private class PendingCapture
        {
            public readonly string FileName;
            public readonly string FullPath;
            public readonly Action<string> OnSuccess;
            public readonly Action<Exception> OnError;

            public PendingCapture(string fileName, string fullPath, Action<string> onSuccess, Action<Exception> onError)
            {
                FileName = fileName;
                FullPath = fullPath;
                OnSuccess = onSuccess;
                OnError = onError;
            }
        }

        public PhotoCaptureRenderer(ICoreClientAPI capi)
        {
            this.capi = capi;
            effectsConfig = WetplateEffects.LoadOrCreate(capi);
        }

        public void ReloadEffectsConfig()
        {
            effectsConfig = WetplateEffects.LoadOrCreate(capi);
        }

        public double RenderOrder => 0;
        public int RenderRange => 0;

        private static bool LooksBlank(byte[] pixels)
        {
            // Heuristic: sample a few points; if all channels are 0, we likely read the wrong buffer.
            if (pixels == null || pixels.Length < 16) return true;

            int step = Math.Max(4, pixels.Length / 32);
            for (int i = 0; i < pixels.Length; i += step)
            {
                // Check BGRA
                if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0)
                {
                    return false;
                }
            }
            return true;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            PendingCapture? toProcess;
            lock (pendingLock)
            {
                toProcess = pending;
                if (toProcess != null) pending = null;
            }

            if (toProcess == null) return;

            try
            {
                int width = capi.Render.FrameWidth;
                int height = capi.Render.FrameHeight;
                int pixelByteCount = width * height * 4;

                byte[] pixels = ArrayPool<byte>.Shared.Rent(pixelByteCount);
                GL.PixelStore(PixelStoreParameter.PackAlignment, 1);

                try
                {
                    // Ensure we read from the default framebuffer.
                    GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
                    GL.ReadBuffer(ReadBufferMode.Back);
                    GL.Finish();
                    GL.ReadPixels(0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

                    // If we got a blank image, try reading the front buffer as a fallback.
                    if (LooksBlank(pixels))
                    {
                        GL.ReadBuffer(ReadBufferMode.Front);
                        GL.Finish();
                        GL.ReadPixels(0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);
                    }

                    // Some framebuffers provide an undefined/zero alpha channel.
                    // Ensure we treat captures as fully opaque, otherwise post-processing may turn into a black image.
                    for (int i = 3; i < pixelByteCount; i += 4)
                    {
                        pixels[i] = 255;
                    }

                    var srcInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                    using var srcBitmap = new SKBitmap(srcInfo);
                    Marshal.Copy(pixels, 0, srcBitmap.GetPixels(), pixelByteCount);

                    const int maxDim = 512;
                    float scale = Math.Min(1f, maxDim / (float)Math.Max(width, height));
                    int outW = Math.Max(1, (int)(width * scale));
                    int outH = Math.Max(1, (int)(height * scale));

                    var dstInfo = new SKImageInfo(outW, outH, SKColorType.Bgra8888, SKAlphaType.Opaque);
                    using var dstBitmap = new SKBitmap(dstInfo);
                    using (var canvas = new SKCanvas(dstBitmap))
                    {
                        canvas.Clear(SKColors.Black);
                        // Flip only vertically to correct GL framebuffer orientation
                        canvas.Scale(1, -1);
                        canvas.Translate(0, -outH);
                        using var srcImage = SKImage.FromBitmap(srcBitmap);
                        canvas.DrawImage(srcImage, new SKRect(0, 0, outW, outH));
                    }

                    // Apply wetplate-style post-processing (optional, configurable).
                    try
                    {
                        WetplateEffects.ApplyInPlace(dstBitmap, toProcess.FileName, effectsConfig);
                    }
                    catch (Exception effectEx)
                    {
                        capi.Logger.Error($"PhotoCapture: Effects failed: {effectEx.Message}");
                    }

                    using var finalImage = SKImage.FromBitmap(dstBitmap);
                    using var pngData = finalImage.Encode(SKEncodedImageFormat.Png, 90);

                    Directory.CreateDirectory(Path.GetDirectoryName(toProcess.FullPath)!);
                    using (var output = File.Open(toProcess.FullPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        pngData.SaveTo(output);
                    }

                    toProcess.OnSuccess(toProcess.FileName);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(pixels);
                }
            }
            catch (Exception ex)
            {
                toProcess.OnError(ex);
            }
        }

        public bool TryScheduleCapture(out string fileName, Action<string> onSuccess, Action<Exception> onError)
        {
            lock (pendingLock)
            {
                if (pending != null)
                {
                    fileName = string.Empty;
                    return false;
                }

                string now = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                fileName = $"wetplate_{now}.png";

                string modDataPath = Path.Combine(GamePaths.DataPath, "ModData", "collodion", "photos");
                string fullPath = Path.Combine(modDataPath, fileName);

                pending = new PendingCapture(fileName, fullPath, onSuccess, onError);
                return true;
            }
        }

        public void Dispose()
        {
            lock (pendingLock)
            {
                pending = null;
            }
        }
    }
}

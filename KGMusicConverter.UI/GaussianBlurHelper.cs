using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KGMusicConverter;

/// <summary>
/// 软件高斯模糊：对 <see cref="WriteableBitmap"/> 的像素做可分离卷积。
///
/// 性能上有三条硬约束（否则 4K 壁纸 + 大半径会把 UI 线程冻住几分钟）：
/// 1. 先把最长边降采样到 <see cref="MaxBlurEdge"/>——模糊之后放大回去根本看不出来；
/// 2. 核半径按缩放比例折算并封顶（可分离卷积是 O(宽 × 高 × 半径)）；
/// 3. 每一行 / 每一列并行算。
/// 结果按 (源图, 半径) 缓存，只改不透明度时不会重算。
/// </summary>
internal static class GaussianBlurHelper
{
    /// <summary>模糊前把最长边降采样到的像素数。</summary>
    private const int MaxBlurEdge = 512;

    /// <summary>实际使用的核半径上限。</summary>
    private const int MaxKernelRadius = 24;

    private static WriteableBitmap? _cacheSource;
    private static int _cacheRadius = -1;
    private static WriteableBitmap? _cacheResult;

    /// <summary>半径为 0 时原样返回；否则走带缓存的模糊。</summary>
    internal static WriteableBitmap BlurIfNeeded(WriteableBitmap source, int radius)
    {
        if (radius <= 0) return source;

        if (ReferenceEquals(_cacheSource, source) && _cacheRadius == radius && _cacheResult is not null)
        {
            return _cacheResult;
        }

        var blurred = Blur(source, radius);
        _cacheSource = source;
        _cacheRadius = radius;
        _cacheResult = blurred;
        return blurred;
    }

    /// <summary>对源位图做降采样 + 高斯模糊，返回模糊后的（更小的）位图。</summary>
    internal static WriteableBitmap Blur(WriteableBitmap source, int radius)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        if (width <= 0 || height <= 0 || radius <= 0) return source;

        byte[] pixels = ReadPixels(source);

        // 1) 降采样
        double scale = Math.Min(1.0, (double)MaxBlurEdge / Math.Max(width, height));
        int dw = Math.Max(1, (int)Math.Round(width * scale));
        int dh = Math.Max(1, (int)Math.Round(height * scale));
        byte[] working = scale < 1.0 ? Downscale(pixels, width, height, dw, dh) : pixels;

        // 2) 折算并封顶半径
        int effective = Math.Clamp((int)Math.Round(radius * scale), 1, MaxKernelRadius);

        // 3) 可分离卷积：先横向，再纵向
        double[] kernel = CreateGaussianKernel(effective);
        byte[] temp = new byte[working.Length];
        byte[] result = new byte[working.Length];

        Parallel.For(0, dh, y => BlurRow(working, temp, dw, y, kernel, effective));
        Parallel.For(0, dw, x => BlurColumn(temp, result, dw, dh, x, kernel, effective));

        return CreateBitmap(result, dw, dh, source);
    }

    private static byte[] ReadPixels(WriteableBitmap source)
    {
        var buffer = new byte[source.PixelWidth * source.PixelHeight * 4];
        using var stream = System.Runtime.InteropServices.WindowsRuntime
            .WindowsRuntimeBufferExtensions.AsStream(source.PixelBuffer);
        stream.ReadExactly(buffer, 0, buffer.Length);
        return buffer;
    }

    private static WriteableBitmap CreateBitmap(byte[] pixels, int width, int height, WriteableBitmap reference)
    {
        var bitmap = new WriteableBitmap(width, height);
        using var stream = System.Runtime.InteropServices.WindowsRuntime
            .WindowsRuntimeBufferExtensions.AsStream(bitmap.PixelBuffer);
        stream.Write(pixels, 0, pixels.Length);
        stream.Flush();
        return bitmap;
    }

    /// <summary>盒式采样降采样：把 source 的若干像素平均到目标像素。</summary>
    private static byte[] Downscale(byte[] source, int sw, int sh, int dw, int dh)
    {
        var target = new byte[dw * dh * 4];

        Parallel.For(0, dh, y =>
        {
            int sy0 = y * sh / dh;
            int sy1 = Math.Max(sy0 + 1, (y + 1) * sh / dh);

            for (int x = 0; x < dw; x++)
            {
                int sx0 = x * sw / dw;
                int sx1 = Math.Max(sx0 + 1, (x + 1) * sw / dw);

                long b = 0, g = 0, r = 0, a = 0;
                int count = 0;

                for (int sy = sy0; sy < sy1; sy++)
                {
                    int rowOffset = sy * sw * 4;
                    for (int sx = sx0; sx < sx1; sx++)
                    {
                        int idx = rowOffset + sx * 4;
                        b += source[idx];
                        g += source[idx + 1];
                        r += source[idx + 2];
                        a += source[idx + 3];
                        count++;
                    }
                }

                if (count == 0) count = 1;
                int outIdx = (y * dw + x) * 4;
                target[outIdx] = (byte)(b / count);
                target[outIdx + 1] = (byte)(g / count);
                target[outIdx + 2] = (byte)(r / count);
                target[outIdx + 3] = (byte)(a / count);
            }
        });

        return target;
    }

    private static void BlurRow(byte[] source, byte[] target, int width, int y, double[] kernel, int radius)
    {
        int rowOffset = y * width * 4;

        for (int x = 0; x < width; x++)
        {
            double b = 0, g = 0, r = 0, a = 0;

            for (int k = -radius; k <= radius; k++)
            {
                int sx = Math.Clamp(x + k, 0, width - 1);
                int idx = rowOffset + sx * 4;
                double weight = kernel[k + radius];

                b += source[idx] * weight;
                g += source[idx + 1] * weight;
                r += source[idx + 2] * weight;
                a += source[idx + 3] * weight;
            }

            int outIdx = rowOffset + x * 4;
            target[outIdx] = (byte)Math.Clamp(b, 0, 255);
            target[outIdx + 1] = (byte)Math.Clamp(g, 0, 255);
            target[outIdx + 2] = (byte)Math.Clamp(r, 0, 255);
            target[outIdx + 3] = (byte)Math.Clamp(a, 0, 255);
        }
    }

    private static void BlurColumn(byte[] source, byte[] target, int width, int height, int x, double[] kernel, int radius)
    {
        int columnOffset = x * 4;
        int stride = width * 4;

        for (int y = 0; y < height; y++)
        {
            double b = 0, g = 0, r = 0, a = 0;

            for (int k = -radius; k <= radius; k++)
            {
                int sy = Math.Clamp(y + k, 0, height - 1);
                int idx = columnOffset + sy * stride;
                double weight = kernel[k + radius];

                b += source[idx] * weight;
                g += source[idx + 1] * weight;
                r += source[idx + 2] * weight;
                a += source[idx + 3] * weight;
            }

            int outIdx = columnOffset + y * stride;
            target[outIdx] = (byte)Math.Clamp(b, 0, 255);
            target[outIdx + 1] = (byte)Math.Clamp(g, 0, 255);
            target[outIdx + 2] = (byte)Math.Clamp(r, 0, 255);
            target[outIdx + 3] = (byte)Math.Clamp(a, 0, 255);
        }
    }

    private static double[] CreateGaussianKernel(int radius)
    {
        int size = radius * 2 + 1;
        var kernel = new double[size];
        double sigma = Math.Max(radius / 2.0, 0.5);
        double twoSigmaSq = 2 * sigma * sigma;

        double sum = 0;
        for (int i = 0; i < size; i++)
        {
            int x = i - radius;
            kernel[i] = Math.Exp(-(x * x) / twoSigmaSq);
            sum += kernel[i];
        }

        for (int i = 0; i < size; i++) kernel[i] /= sum;
        return kernel;
    }
}

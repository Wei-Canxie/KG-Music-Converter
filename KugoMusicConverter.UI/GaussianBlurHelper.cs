using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KugoMusicConverter;

/// <summary>
/// 软件高斯模糊工具 — 对 WriteableBitmap 像素进行模糊处理
/// </summary>
internal static class GaussianBlurHelper
{
    /// <summary>
    /// 从文件加载图像并应用高斯模糊
    /// </summary>
    public static WriteableBitmap LoadAndBlur(string filePath, int radius)
    {
        // 第一步：加载文件到 WriteableBitmap
        WriteableBitmap? wb = null;
        using (var fileStream = File.OpenRead(filePath))
        {
            var bitmap = new BitmapImage();
            bitmap.SetSource(fileStream.AsRandomAccessStream());
            // 必须让 BitmapImage 完成加载才能读取 PixelWidth/Height
            // 使用 LoadImage 之前需要等待，这里直接用 WriteableBitmap 构造函数
            wb = new WriteableBitmap((int)bitmap.PixelWidth, (int)bitmap.PixelHeight);
        }

        // 第二步：把像素从文件读入 WriteableBitmap
        using (var fileStream = File.OpenRead(filePath))
        {
            wb.SetSource(fileStream.AsRandomAccessStream());
        }

        // 第三步：应用高斯模糊
        if (radius > 0)
        {
            return Blur(wb, radius);
        }

        return wb;
    }

    /// <summary>
    /// 对 WriteableBitmap 进行高斯模糊
    /// </summary>
    public static WriteableBitmap Blur(WriteableBitmap source, int radius)
    {
        if (radius <= 0) return source;

        int width = source.PixelWidth;
        int height = source.PixelHeight;

        // 获取源像素 (BGRA 格式)
        byte[] pixels = new byte[width * height * 4];
        using (var stream = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsStream(source.PixelBuffer))
        {
            stream.Read(pixels, 0, pixels.Length);
        }

        // 应用高斯模糊
        byte[] result = ApplyGaussianBlur(pixels, width, height, radius);

        // 创建目标 WriteableBitmap
        var dest = new WriteableBitmap(width, height);
        using (var stream = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsStream(dest.PixelBuffer))
        {
            stream.Write(result, 0, result.Length);
        }

        return dest;
    }

    private static byte[] ApplyGaussianBlur(byte[] pixels, int width, int height, int radius)
    {
        radius = Math.Min(radius, 50);
        if (radius <= 0) return pixels;

        double[] kernel = CreateGaussianKernel(radius);

        byte[] temp = new byte[pixels.Length];
        byte[] result = new byte[pixels.Length];

        // 水平方向模糊
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double b = 0, g = 0, r = 0, a = 0;
                double weightSum = 0;

                for (int k = -radius; k <= radius; k++)
                {
                    int px = Math.Clamp(x + k, 0, width - 1);
                    int idx = (y * width + px) * 4;
                    double weight = kernel[k + radius];

                    b += pixels[idx] * weight;
                    g += pixels[idx + 1] * weight;
                    r += pixels[idx + 2] * weight;
                    a += pixels[idx + 3] * weight;
                    weightSum += weight;
                }

                int outIdx = (y * width + x) * 4;
                temp[outIdx] = (byte)Math.Clamp(b / weightSum, 0, 255);
                temp[outIdx + 1] = (byte)Math.Clamp(g / weightSum, 0, 255);
                temp[outIdx + 2] = (byte)Math.Clamp(r / weightSum, 0, 255);
                temp[outIdx + 3] = (byte)Math.Clamp(a / weightSum, 0, 255);
            }
        }

        // 垂直方向模糊
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double b = 0, g = 0, r = 0, a = 0;
                double weightSum = 0;

                for (int k = -radius; k <= radius; k++)
                {
                    int py = Math.Clamp(y + k, 0, height - 1);
                    int idx = (py * width + x) * 4;
                    double weight = kernel[k + radius];

                    b += temp[idx] * weight;
                    g += temp[idx + 1] * weight;
                    r += temp[idx + 2] * weight;
                    a += temp[idx + 3] * weight;
                    weightSum += weight;
                }

                int outIdx = (y * width + x) * 4;
                result[outIdx] = (byte)Math.Clamp(b / weightSum, 0, 255);
                result[outIdx + 1] = (byte)Math.Clamp(g / weightSum, 0, 255);
                result[outIdx + 2] = (byte)Math.Clamp(r / weightSum, 0, 255);
                result[outIdx + 3] = (byte)Math.Clamp(a / weightSum, 0, 255);
            }
        }

        return result;
    }

    private static double[] CreateGaussianKernel(int radius)
    {
        int size = radius * 2 + 1;
        double[] kernel = new double[size];
        double sigma = Math.Max(radius / 2.0, 0.5);
        double twoSigmaSq = 2 * sigma * sigma;

        double sum = 0;
        for (int i = 0; i < size; i++)
        {
            int x = i - radius;
            kernel[i] = Math.Exp(-(x * x) / twoSigmaSq);
            sum += kernel[i];
        }

        for (int i = 0; i < size; i++)
        {
            kernel[i] /= sum;
        }

        return kernel;
    }
}

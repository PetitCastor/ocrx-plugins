using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SignatureReaderSpike;

/// <summary>A BGRA image held as a flat byte array (4 bytes per pixel, no padding).</summary>
internal sealed record Bgra(int Width, int Height, byte[] Pixels)
{
    public static Bgra Load(string path)
    {
        using var source = new Bitmap(path);
        using var bitmap = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[4 * bitmap.Width * bitmap.Height];
            for (var y = 0; y < bitmap.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * 4 * bitmap.Width, 4 * bitmap.Width);
            return new Bgra(bitmap.Width, bitmap.Height, pixels);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public Bgra Crop(int x, int y, int width, int height)
    {
        x = Math.Clamp(x, 0, Width);
        y = Math.Clamp(y, 0, Height);
        width = Math.Clamp(width, 0, Width - x);
        height = Math.Clamp(height, 0, Height - y);
        var pixels = new byte[4 * width * height];
        for (var row = 0; row < height; row++)
            Array.Copy(Pixels, 4 * ((y + row) * Width + x), pixels, 4 * row * width, 4 * width);
        return new Bgra(width, height, pixels);
    }

    public void Save(string path)
    {
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (var y = 0; y < Height; y++)
                Marshal.Copy(Pixels, y * 4 * Width, data.Scan0 + y * data.Stride, 4 * Width);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        bitmap.Save(path, ImageFormat.Png);
    }
}

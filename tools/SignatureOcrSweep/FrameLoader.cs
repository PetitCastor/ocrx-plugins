using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace SignatureOcrSweep;

/// <summary>Decodes a PNG frame exactly as the engine's <c>ReplayFrameSource.DecodeFrameAsync</c> does:
/// Bgra8, alpha ignored, default colour management. A different pixel path here would make offline mode
/// measure something the engine never sees.</summary>
public static class FrameLoader
{
    public static async Task<SoftwareBitmap> LoadAsync(string path)
    {
        using var fileStream = File.OpenRead(path);
        var decoder = await BitmapDecoder.CreateAsync(fileStream.AsRandomAccessStream());
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
    }

    /// <summary>Writes a Bgra8 bitmap as PNG.</summary>
    public static async Task SavePngAsync(SoftwareBitmap bitmap, string path)
    {
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        stream.Seek(0);
        var bytes = new byte[stream.Size];
        await stream.ReadAsync(bytes.AsBuffer(), (uint)stream.Size, Windows.Storage.Streams.InputStreamOptions.None);
        await File.WriteAllBytesAsync(path, bytes);
    }

    /// <summary>Resamples <paramref name="source"/> to <paramref name="width"/> x <paramref name="height"/>
    /// with Fant (an area-averaging filter), for building synthetic downscales of a native frame.</summary>
    public static async Task<SoftwareBitmap> DownscaleAsync(SoftwareBitmap source, int width, int height)
    {
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, stream);
        encoder.SetSoftwareBitmap(source);
        await encoder.FlushAsync();

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)width,
            ScaledHeight = (uint)height,
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        return await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SignatureReaderSpike;

/// <summary>Draws every located badge, enlarged, on one image: the quick way to label new crops by eye.</summary>
internal static class ContactSheet
{
    private const int Zoom = 5;
    private const int CellWidth = 520;
    private const int CellHeight = 150;
    private const int Columns = 3;

    public static void Save(string path, IReadOnlyList<(string Caption, Bgra? Crop)> rows)
    {
        var lines = (rows.Count + Columns - 1) / Columns;
        using var sheet = new Bitmap(Columns * CellWidth, Math.Max(1, lines) * CellHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.Black);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var font = new Font("Consolas", 16, FontStyle.Bold);
        for (var i = 0; i < rows.Count; i++)
        {
            var x = i % Columns * CellWidth;
            var y = i / Columns * CellHeight;
            g.DrawString(rows[i].Caption, font, Brushes.Yellow, x + 4, y + 4);
            if (rows[i].Crop is not { Width: > 0, Height: > 0 } crop)
            {
                g.DrawString("not found", font, Brushes.Red, x + 60, y + 60);
                continue;
            }

            using var cell = ToBitmap(crop);
            var zoom = Math.Max(1, Math.Min(Zoom, Math.Min((CellWidth - 60) / cell.Width, (CellHeight - 40) / cell.Height)));
            g.DrawImage(cell, new Rectangle(x + 50, y + 34, cell.Width * zoom, cell.Height * zoom));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        sheet.Save(path, ImageFormat.Png);
    }

    private static Bitmap ToBitmap(Bgra image)
    {
        var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppRgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (var y = 0; y < image.Height; y++)
                Marshal.Copy(image.Pixels, y * 4 * image.Width, data.Scan0 + y * data.Stride, 4 * image.Width);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }
}

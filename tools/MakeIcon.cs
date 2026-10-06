using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Génère l'icône de l'application (fichier .ico multi-tailles) au moment de la compilation.
static class MakeIcon
{
    static void Main(string[] args)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++) images[i] = Png(sizes[i]);
        using (var f = new BinaryWriter(File.Create(args[0])))
        {
            f.Write((short)0);
            f.Write((short)1);
            f.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                f.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                f.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                f.Write((byte)0);
                f.Write((byte)0);
                f.Write((short)1);
                f.Write((short)32);
                f.Write(images[i].Length);
                f.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) f.Write(img);
        }
    }

    static byte[] Png(int size)
    {
        using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = size;
                Round(g, Color.FromArgb(76, 141, 255), new RectangleF(0, 0, s - 1, s - 1), s * 0.22f);
                float[] levels = { 0.62f, 0.32f, 0.5f };
                for (int i = 0; i < 3; i++)
                {
                    float x = s * (0.28f + i * 0.22f);
                    using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(1f, s * 0.07f)))
                        g.DrawLine(pen, x, s * 0.2f, x, s * 0.8f);
                    float y = s * 0.2f + s * 0.6f * levels[i];
                    float w = s * 0.17f, h = Math.Max(2f, s * 0.12f);
                    Round(g, Color.White, new RectangleF(x - w / 2, y - h / 2, w, h), h * 0.3f);
                }
            }
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }

    static void Round(Graphics g, Color c, RectangleF r, float rad)
    {
        using (var p = new GraphicsPath())
        {
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }
    }
}

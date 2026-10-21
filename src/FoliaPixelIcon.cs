using System;
using System.Drawing;

namespace NixLanConsole
{
    // Original 32px pixel icon, drawn in code so GitHub code-only updates include it.
    internal static class FoliaPixelIcon
    {
        internal static Image Create()
        {
            var bitmap = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                Color outline = Color.FromArgb(53, 32, 110);
                Color deep = Color.FromArgb(82, 46, 145);
                Color purple = Color.FromArgb(143, 100, 202);
                Color light = Color.FromArgb(220, 178, 250);
                Color pink = Color.FromArgb(255, 119, 200);
                Color palePink = Color.FromArgb(255, 207, 240);
                Color cyan = Color.FromArgb(92, 236, 244);
                Color paleCyan = Color.FromArgb(192, 255, 255);

                Pixel(graphics, outline, 6, 2, 20, 1);
                Pixel(graphics, outline, 4, 3, 24, 2);
                Pixel(graphics, outline, 3, 5, 26, 22);
                Pixel(graphics, outline, 4, 27, 24, 2);
                Pixel(graphics, outline, 6, 29, 20, 1);
                Pixel(graphics, deep, 5, 5, 22, 22);
                Pixel(graphics, purple, 6, 7, 20, 18);
                Pixel(graphics, light, 6, 4, 20, 2);
                Pixel(graphics, pink, 5, 6, 1, 18);
                Pixel(graphics, palePink, 7, 6, 1, 17);
                Pixel(graphics, deep, 25, 7, 1, 18);
                Pixel(graphics, light, 7, 26, 18, 1);
                Pixel(graphics, pink, 8, 27, 16, 1);

                // Leaves and tiny stars give the music note a Folia identity.
                Pixel(graphics, outline, 7, 12, 4, 5);
                Pixel(graphics, cyan, 8, 13, 4, 3);
                Pixel(graphics, paleCyan, 9, 13, 2, 1);
                Pixel(graphics, outline, 22, 8, 4, 5);
                Pixel(graphics, cyan, 23, 9, 3, 3);
                Pixel(graphics, paleCyan, 24, 9, 1, 2);
                Pixel(graphics, palePink, 8, 8, 1, 3);
                Pixel(graphics, palePink, 7, 9, 3, 1);
                Pixel(graphics, paleCyan, 24, 21, 1, 3);
                Pixel(graphics, paleCyan, 23, 22, 3, 1);

                // Two note heads, stems and a connected beam with a crisp dark outline.
                Pixel(graphics, outline, 13, 10, 11, 4);
                Pixel(graphics, outline, 13, 12, 4, 11);
                Pixel(graphics, outline, 21, 12, 4, 9);
                Pixel(graphics, outline, 9, 20, 8, 6);
                Pixel(graphics, outline, 17, 18, 8, 6);
                Pixel(graphics, pink, 14, 11, 9, 2);
                Pixel(graphics, palePink, 15, 11, 7, 1);
                Pixel(graphics, pink, 14, 13, 2, 9);
                Pixel(graphics, pink, 22, 13, 2, 7);
                Pixel(graphics, palePink, 14, 13, 1, 8);
                Pixel(graphics, palePink, 22, 13, 1, 6);
                Pixel(graphics, pink, 11, 21, 5, 4);
                Pixel(graphics, palePink, 11, 21, 4, 2);
                Pixel(graphics, pink, 19, 19, 5, 4);
                Pixel(graphics, palePink, 19, 19, 4, 2);
            }
            return bitmap;
        }

        private static void Pixel(Graphics graphics, Color color, int x, int y, int width, int height)
        {
            using (var brush = new SolidBrush(color)) graphics.FillRectangle(brush, x * 2, y * 2, width * 2, height * 2);
        }
    }
}

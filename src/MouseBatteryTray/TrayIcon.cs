using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MouseBatteryTray;

internal static class TrayIcon
{
    public static Bitmap Draw(BatteryReading reading, bool light)
    {
        var image = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var outline = new GraphicsPath();
        outline.AddArc(1, 1, 12, 12, 180, 90);
        outline.AddArc(19, 1, 12, 12, 270, 90);
        outline.AddArc(19, 19, 12, 12, 0, 90);
        outline.AddArc(1, 19, 12, 12, 90, 90);
        outline.CloseFigure();
        using var background = new SolidBrush(light ? Color.FromArgb(245, 247, 250) : Color.FromArgb(30, 38, 52));
        using var border = new Pen(light ? Color.FromArgb(156, 166, 182) : Color.FromArgb(89, 104, 128), 1);
        graphics.FillPath(background, outline);
        graphics.DrawPath(border, outline);
        var accent = reading.Charging ? (light ? Color.FromArgb(5, 125, 85) : Color.FromArgb(110, 231, 183)) :
            reading.Percentage <= 10 ? (light ? Color.FromArgb(190, 30, 62) : Color.FromArgb(251, 113, 133)) :
            reading.Percentage <= 20 ? (light ? Color.FromArgb(161, 91, 0) : Color.FromArgb(251, 191, 36)) :
            light ? Color.FromArgb(5, 125, 85) : Color.FromArgb(110, 231, 183);
        var text = reading.Percentage?.ToString() ?? "?";
        var textColor = reading.Charging || reading.Percentage <= 20 ? accent :
            light ? Color.FromArgb(25, 35, 50) : Color.FromArgb(241, 245, 249);
        using var textBrush = new SolidBrush(textColor);
        using var font = new Font("Segoe UI", text.Length == 3 ? 16 : 21, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(text, font, textBrush, new RectangleF(-1, 0, 34, 25), format);
        using var track = new SolidBrush(light ? Color.FromArgb(210, 216, 225) : Color.FromArgb(66, 79, 99));
        using var fill = new SolidBrush(accent);
        graphics.FillRectangle(track, 6, 26, 20, 3);
        if (reading.Percentage is int pct && pct > 0) graphics.FillRectangle(fill, 6, 26, Math.Max(1, 20 * pct / 100f), 3);
        if (reading.Charging)
            graphics.FillPolygon(fill, new PointF[] { new(26, 3), new(23, 8), new(26, 8), new(24, 12), new(29, 6), new(26, 6) });
        return image;
    }
}

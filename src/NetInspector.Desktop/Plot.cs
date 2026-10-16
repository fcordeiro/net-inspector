using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Text.Json;
using NetInspector.Core;

namespace NetInspector.Desktop;

public sealed class Plot : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public (DateTimeOffset At, double? Value)[] Points { get; set; } = [];
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public string Caption { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Color LineColor { get; set; } = Theme.Good;
    public Plot() { DoubleBuffered = true; BackColor = Theme.Surface; Dock = DockStyle.Fill; Margin = new Padding(8); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var textBrush = new SolidBrush(Theme.Text); using var mutedBrush = new SolidBrush(Theme.Muted);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var titleFont = new Font("Segoe UI", 11, FontStyle.Bold);
        g.DrawString(Caption, titleFont, textBrush, 12, 10);
        var rect = new Rectangle(56, 40, Math.Max(10, Width - 80), Math.Max(10, Height - 76));
        var valid = Points.Where(p => p.Value.HasValue).ToArray();
        if (valid.Length == 0) { g.DrawString("Aguardando medições concluídas", Font, mutedBrush, 15, 55); return; }
        var max = Math.Max(1, valid.Max(p => p.Value!.Value) * 1.15);
        using var grid = new Pen(Theme.Border);
        for (var i = 0; i <= 3; i++)
        {
            var y = rect.Bottom - rect.Height * i / 3;
            g.DrawLine(grid, rect.Left, y, rect.Right, y);
            g.DrawString((max * i / 3).ToString("0.#"), Font, mutedBrush, 4, y - 7);
        }
        var minTime = Points[0].At; var range = Math.Max(1, (Points[^1].At - minTime).TotalSeconds);
        PointF? previous = null;
        using var pen = new Pen(LineColor, 2);
        using var brush = new SolidBrush(LineColor);
        foreach (var p in Points)
        {
            if (!p.Value.HasValue) { previous = null; continue; }
            var point = new PointF(rect.Left + (float)((p.At - minTime).TotalSeconds / range * rect.Width), rect.Bottom - (float)(p.Value.Value / max * rect.Height));
            if (previous.HasValue) g.DrawLine(pen, previous.Value, point);
            g.FillEllipse(brush, point.X - 2, point.Y - 2, 4, 4);
            previous = point;
        }
        g.DrawString(minTime.ToLocalTime().ToString("HH:mm:ss"), Font, mutedBrush, rect.Left, rect.Bottom + 7);
        g.DrawString(Points[^1].At.ToLocalTime().ToString("HH:mm:ss"), Font, mutedBrush, rect.Right - 65, rect.Bottom + 7);
    }
}

using System.Drawing.Drawing2D;
using NetInspector.Core;

namespace NetInspector.Desktop;

public sealed class ResourceMeter : Control
{
    private readonly bool cpu;
    private ResourceSample? sample;
    public ResourceMeter(bool cpu)
    {
        this.cpu = cpu; Dock = DockStyle.Fill; Margin = new Padding(6); DoubleBuffered = true;
        BackColor = Theme.Surface; ForeColor = Theme.Text;
        AccessibleName = cpu ? "Uso total de CPU" : "Uso de memória física";
    }
    public void ShowSample(ResourceSample? value)
    {
        sample = value;
        var percent = cpu ? sample?.CpuPercent : sample?.MemoryPercent;
        AccessibleDescription = percent.HasValue ? $"{percent:F1}%" : "Não medido";
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var value = cpu ? sample?.CpuPercent : sample?.MemoryPercent;
        using var text = new SolidBrush(Theme.Text); using var muted = new SolidBrush(Theme.Muted);
        using var title = new Font("Segoe UI", 10, FontStyle.Bold);
        using var number = new Font("Segoe UI", 18, FontStyle.Bold);
        using var small = new Font("Segoe UI", 9);
        g.DrawString(cpu ? "CPU • computador" : "MEMÓRIA FÍSICA • computador", title, text, 12, 6);
        var color = value >= 90 ? Theme.Orange : Theme.Good;
        if (cpu)
        {
            using var track = new Pen(Theme.Border, 10); using var fill = new Pen(color, 10);
            var arc = new Rectangle(24, 38, 112, 100);
            g.DrawArc(track, arc, 180, 180);
            if (value > 0) g.DrawArc(fill, arc, 180, (float)(value.Value * 1.8));
            g.DrawString(value.HasValue ? $"{value:F1}%" : "—", number, text, 151, 39);
            g.DrawString(sample == null ? "Aguardando coleta" : value == null ? "Aguardando amostra de CPU" : "Uso total • intervalo de 2s", small, muted, 151, 75);
        }
        else
        {
            using var track = new SolidBrush(Theme.Border); using var fill = new SolidBrush(color);
            // Ten vertical segments form a column filled from bottom to top.
            var bottom = Math.Min(Height - 10, 102);
            var height = Math.Max(10, bottom - 38);
            var segmentHeight = height / 10f;
            var fraction = (float)Math.Clamp(value.GetValueOrDefault() / 100, 0, 1);
            for (var i = 0; i < 10; i++)
            {
                var y = bottom - (i + 1) * segmentHeight;
                var segment = new RectangleF(24, y, 40, Math.Max(1, segmentHeight - 2));
                g.FillRectangle(track, segment);
                var filled = Math.Clamp(fraction * 10 - i, 0, 1);
                if (value.HasValue && filled > 0)
                    g.FillRectangle(fill, segment.X, segment.Bottom - segment.Height * filled, segment.Width, segment.Height * filled);
            }
            g.DrawString(value.HasValue ? $"{value:F1}%" : "—", number, text, 86, 39);
            var detail = sample?.UsedMemoryBytes is { } used && sample.TotalMemoryBytes is { } total
                ? $"{used / 1073741824.0:F1} / {total / 1073741824.0:F1} GiB em uso" : "Aguardando coleta / não medido";
            g.DrawString(detail, small, muted, 86, 75);
        }
    }
}

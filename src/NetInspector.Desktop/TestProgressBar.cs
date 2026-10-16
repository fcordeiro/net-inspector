namespace NetInspector.Desktop;

public sealed class TestProgressBar : ProgressBar
{
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 80 };
    private int offset;
    public TestProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        animation.Tick += (_, _) => { if (Style == ProgressBarStyle.Marquee) { offset = (offset + 12) % Math.Max(1, Width); Invalidate(); } };
        animation.Start();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Border);
        using var fill = new SolidBrush(Theme.Blue);
        if (Style == ProgressBarStyle.Marquee)
        {
            var width = Math.Max(24, Width / 5);
            e.Graphics.FillRectangle(fill, offset, 0, width, Height);
            e.Graphics.FillRectangle(fill, offset - Width, 0, width, Height);
        }
        else e.Graphics.FillRectangle(fill, 0, 0, Width * (Value - Minimum) / Math.Max(1, Maximum - Minimum), Height);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) animation.Dispose();
        base.Dispose(disposing);
    }
}

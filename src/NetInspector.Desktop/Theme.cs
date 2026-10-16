using System.Drawing.Drawing2D;

namespace NetInspector.Desktop;

public static class Theme
{
    public static readonly Color Background = Color.FromArgb(9, 18, 33);
    public static readonly Color Surface = Color.FromArgb(17, 32, 52);
    public static readonly Color Raised = Color.FromArgb(23, 43, 66);
    public static readonly Color Border = Color.FromArgb(39, 62, 86);
    public static readonly Color Text = Color.FromArgb(229, 239, 250);
    public static readonly Color Muted = Color.FromArgb(155, 178, 203);
    public static readonly Color Blue = Color.FromArgb(0, 111, 204);
    public static readonly Color Orange = Color.FromArgb(255, 139, 36);
    public static readonly Color Good = Color.FromArgb(97, 215, 178);
    public static readonly Color Bad = Color.FromArgb(255, 147, 151);
    public static readonly Color Navy = Text;
    public static void Initialize()
    {
#pragma warning disable WFO5001
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
    }
    public static void Apply(Form form, string title)
    {
        form.Text = title; form.Font = new Font("Segoe UI", 10); form.BackColor = Background; form.ForeColor = Text;
        form.Size = new Size(1440, 980); form.MinimumSize = new Size(1120, 820); form.StartPosition = FormStartPosition.CenterScreen;
    }
    public static Bitmap Asset(string name)
    {
        using var stream = typeof(Theme).Assembly.GetManifestResourceStream("NetInspector.Desktop.Assets." + name) ?? throw new InvalidOperationException("Imagem não encontrada: " + name);
        using var source = new Bitmap(stream); return new Bitmap(source);
    }
    public static Control Header(string title)
    {
        var header = new TechHeader { Dock = DockStyle.Top, Height = 120, Padding = new Padding(20, 10, 20, 10) };
        var brand = new PictureBox { Dock = DockStyle.Right, Width = 235, Image = Asset("logo-connect10.png"), SizeMode = PictureBoxSizeMode.Zoom, Padding = new Padding(12), AccessibleName = "CONNECT10 INTERNET" };
        var titles = new HeaderTitles { Dock = DockStyle.Fill, BackColor = Color.Transparent, AccessibleName = "Net Inspector. Diagnóstico de conexão" };
        header.Controls.Add(titles); header.Controls.Add(brand); return header;
    }
    public static Label CopyrightFooter() => new() { Text = NetInspector.Core.Branding.Copyright, Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted, BackColor = Background, Font = new Font("Segoe UI", 9) };
    public static Button Action(string text, Color color)
    {
        var button = new TechButton { Text = text, AutoSize = false, Size = new Size(196, 44), FlatStyle = FlatStyle.Flat, BackColor = color, ForeColor = Text, Font = new Font("Segoe UI", 10, FontStyle.Bold), Margin = new Padding(0, 0, 8, 8), UseVisualStyleBackColor = false, Cursor = Cursors.Hand };
        button.FlatAppearance.BorderColor = Border; button.FlatAppearance.MouseOverBackColor = Raised; button.FlatAppearance.MouseDownBackColor = Blue;
        button.EnabledChanged += (_, _) => { button.BackColor = button.Enabled ? color : Background; button.ForeColor = button.Enabled ? Text : Muted; };
        return button;
    }
    public static void StyleInputs(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            control.ForeColor = Text; control.BackColor = Surface;
            if (control is Label or CheckBox) control.BackColor = Background;
        }
    }
    public static void ToolWindow(Form form, string title, Label description)
    {
        form.Padding = new Padding(18);
        form.StartPosition = FormStartPosition.CenterParent;
        description.BackColor = Surface; description.ForeColor = Muted;
        description.Padding = new Padding(18, 12, 18, 12);
        var heading = new ToolHeading { Text = title.ToUpperInvariant(), Dock = DockStyle.Top, Height = 90 };
        form.Controls.Add(heading);
        foreach (var control in Descendants(form))
        {
            if (control is TextBox box) { box.BorderStyle = BorderStyle.None; box.BackColor = Surface; box.Font = new Font("Consolas", 9.5f); }
            if (control is CheckedListBox list) { list.BorderStyle = BorderStyle.None; list.Font = new Font("Segoe UI", 11); list.IntegralHeight = false; }
        }
    }
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var c in Descendants(child)) yield return c; }
    }
}

public sealed class ToolHeading : Control
{
    public ToolHeading() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new LinearGradientBrush(ClientRectangle, Theme.Raised, Theme.Background, 0f);
        e.Graphics.FillRectangle(background, ClientRectangle);
        using var grid = new Pen(Theme.Border);
        for (var x = Width - 220; x < Width; x += 32) e.Graphics.DrawLine(grid, x, 0, x - 45, Height);
        using var small = new Font("Segoe UI", 8, FontStyle.Bold); using var title = new Font("Segoe UI", 18, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, "CONNECT10  /  SYSTEM TOOLS", small, new Rectangle(14, 10, Width - 28, 18), Theme.Muted, TextFormatFlags.Left);
        TextRenderer.DrawText(e.Graphics, Text, title, new Rectangle(12, 31, Width - 24, 43), Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        using var accent = new Pen(Theme.Orange, 3); e.Graphics.DrawLine(accent, 14, Height - 8, 100, Height - 8);
    }
}

// Preserve wrapping and center each control in its actual flow row, including at scaled DPI.
public sealed class ControlRow : FlowLayoutPanel
{
    protected override void OnControlAdded(ControlEventArgs e)
    {
        // FlowLayoutPanel centers controls vertically within each row when no edge is anchored.
        if (e.Control != null) { e.Control.Anchor = AnchorStyles.None; e.Control.Margin = new Padding(0, 4, 8, 4); }
        base.OnControlAdded(e);
    }
}

public sealed class TechHeader : Panel
{
    public TechHeader() { DoubleBuffered = true; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var fill = new LinearGradientBrush(ClientRectangle, Theme.Surface, Theme.Background, 0f);
        e.Graphics.FillRectangle(fill, ClientRectangle);
        using var grid = new Pen(Color.FromArgb(20, 115, 175));
        for (var x = Width / 2; x < Width; x += 42) e.Graphics.DrawLine(grid, x, 0, x - 60, Height);
        using var accent = new Pen(Theme.Orange, 2); e.Graphics.DrawLine(accent, 20, Height - 2, 110, Height - 2);
    }
}

public sealed class TechButton : Button
{
    public TechButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Rounded corners belong to the parent surface, not to the native button background.
        using var background = new SolidBrush(Parent?.BackColor ?? Theme.Background);
        e.Graphics.FillRectangle(background, ClientRectangle);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        OnPaintBackground(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
        using var path = new GraphicsPath(); const int radius = 12;
        path.AddArc(bounds.X, bounds.Y, radius, radius, 180, 90); path.AddArc(bounds.Right - radius, bounds.Y, radius, radius, 270, 90);
        path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90); path.AddArc(bounds.X, bounds.Bottom - radius, radius, radius, 90, 90); path.CloseFigure();
        var hover = Enabled && ClientRectangle.Contains(PointToClient(Cursor.Position));
        using var fill = new SolidBrush(hover ? Theme.Raised : BackColor); e.Graphics.FillPath(fill, path);
        using var border = new Pen(Focused ? Theme.Orange : hover ? Theme.Blue : Theme.Border); e.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, Enabled ? ForeColor : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

public sealed class HeaderTitles : Control
{
    public HeaderTitles() { SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        using var title = new Font("Segoe UI", 25, FontStyle.Bold);
        using var subtitle = new Font("Segoe UI", 11);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var titleHeight = TextRenderer.MeasureText(e.Graphics, "Net Inspector", title, Size.Empty, flags).Height;
        var subtitleHeight = TextRenderer.MeasureText(e.Graphics, "Diagnóstico de conexão", subtitle, Size.Empty, flags).Height;
        const int top = 2;
        TextRenderer.DrawText(e.Graphics, "Net Inspector", title, new Rectangle(0, top, Width, titleHeight), Theme.Text, flags);
        TextRenderer.DrawText(e.Graphics, "Diagnóstico de conexão", subtitle, new Rectangle(0, top + titleHeight + 12, Width, subtitleHeight), Theme.Muted, flags);
    }
}

public sealed class ThemeComboBox : ComboBox
{
    public ThemeComboBox()
    {
        FlatStyle = FlatStyle.Flat; DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 36;
        BackColor = Theme.Surface; ForeColor = Theme.Text; DropDownStyle = ComboBoxStyle.DropDownList;
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        using var background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Raised : Theme.Surface);
        e.Graphics.FillRectangle(background, e.Bounds);
        var text = e.Index >= 0 ? GetItemText(Items[e.Index]) : Text;
        TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height), Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
}

public sealed class DurationPicker : UserControl
{
    private readonly TextBox editor = new() { BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center, BackColor = Theme.Surface, ForeColor = Theme.Text, Dock = DockStyle.Fill };
    private decimal value = NetInspector.Core.TestPolicy.DefaultMinutes;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public decimal Minimum { get; set; } = 1;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public decimal Maximum { get; set; } = NetInspector.Core.TestPolicy.MaximumMinutes;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public decimal Value { get => value; set { this.value = Math.Clamp(value, Minimum, Maximum); editor.Text = this.value.ToString("0"); } }
    public DurationPicker()
    {
        Height = 44; Width = 116; BackColor = Theme.Surface; Padding = new Padding(2, 0, 2, 0);
        editor.Dock = DockStyle.None;
        var minus = Theme.Action("−", Theme.Surface); minus.Dock = DockStyle.Left; minus.Width = 28; minus.TabStop = false;
        var plus = Theme.Action("+", Theme.Surface); plus.Dock = DockStyle.Right; plus.Width = 28; plus.TabStop = false;
        minus.Click += (_, _) => Value--; plus.Click += (_, _) => Value++;
        editor.Text = value.ToString("0"); editor.AccessibleName = "Duração do teste em minutos";
        editor.TextChanged += (_, _) => { if (decimal.TryParse(editor.Text, out var parsed)) value = Math.Clamp(parsed, Minimum, Maximum); };
        EnabledChanged += (_, _) => { editor.Visible = Enabled; Invalidate(); };
        editor.Leave += (_, _) => Value = value;
        editor.KeyDown += (_, e) => { if (e.KeyCode == Keys.Up) { Value++; e.SuppressKeyPress = true; } if (e.KeyCode == Keys.Down) { Value--; e.SuppressKeyPress = true; } };
        Controls.Add(editor); Controls.Add(minus); Controls.Add(plus);
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        editor.SetBounds(31, (ClientSize.Height - editor.PreferredHeight) / 2, Math.Max(10, ClientSize.Width - 62), editor.PreferredHeight);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); using var pen = new Pen(ContainsFocus ? Theme.Orange : Theme.Border); e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        if (!Enabled) TextRenderer.DrawText(e.Graphics, Value.ToString("0"), Font, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

public sealed class FlatTabs : TabControl
{
    public override Rectangle DisplayRectangle => ClientRectangle;
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x1328 && m.WParam == IntPtr.Zero) { m.Result = new IntPtr(1); return; }
        base.WndProc(ref m);
    }
}

public sealed class ThemeCheckBox : CheckBox
{
    public ThemeCheckBox()
    {
        // FlatStyle prevents the native checkbox renderer from repainting over our drawing on focus/hover.
        FlatStyle = FlatStyle.Flat;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine);
        return new Size(text.Width + LogicalToDeviceUnits(24) + Padding.Horizontal,
            Math.Max(text.Height, LogicalToDeviceUnits(16)) + Padding.Vertical + LogicalToDeviceUnits(8));
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var background = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(background, ClientRectangle);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        OnPaintBackground(e);
        var side = LogicalToDeviceUnits(16);
        var box = new Rectangle(Padding.Left, (Height - side) / 2, side, side);
        using var fill = new SolidBrush(Checked && Enabled ? Theme.Blue : Theme.Raised); e.Graphics.FillRectangle(fill, box);
        using var border = new Pen(Focused ? Theme.Orange : Theme.Muted); e.Graphics.DrawRectangle(border, box);
        if (Checked) TextRenderer.DrawText(e.Graphics, "✓", Font, box, Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        var textBounds = new Rectangle(box.Right + LogicalToDeviceUnits(8), Padding.Top,
            Math.Max(0, Width - box.Right - LogicalToDeviceUnits(8) - Padding.Right), Height - Padding.Vertical);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, textBounds, Theme.Muted, BackColor);
    }
}

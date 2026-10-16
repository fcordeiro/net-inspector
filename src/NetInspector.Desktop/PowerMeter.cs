using NetInspector.Core;

namespace NetInspector.Desktop;

public sealed class PowerMeter : Control
{
    private PowerSample? sample;
    public PowerMeter()
    {
        Dock = DockStyle.Fill; Margin = new Padding(6); DoubleBuffered = true;
        BackColor = Theme.Surface; ForeColor = Theme.Text; AccessibleName = "Bateria e alimentação elétrica";
    }
    public void ShowSample(PowerSample? value) { sample = value; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var title = new Font("Segoe UI", 10, FontStyle.Bold);
        using var number = new Font("Segoe UI", 17, FontStyle.Bold);
        using var small = new Font("Segoe UI", 9);
        using var text = new SolidBrush(Theme.Text);
        using var muted = new SolidBrush(Theme.Muted);
        using var border = new Pen(Theme.Muted, 2);
        using var fill = new SolidBrush(sample?.Percent <= 20 ? Theme.Orange : Theme.Good);
        e.Graphics.DrawString(sample?.HasBattery == true ? "BATERIA • computador" : "ALIMENTAÇÃO • computador", title, text, 12, 6);
        var source = sample?.OnAcPower switch { true => "Cabo conectado", false => "Sem energia da tomada", _ => "Fonte não informada" };
        if (sample?.HasBattery == true)
        {
            e.Graphics.DrawRectangle(border, 16, 44, 65, 35);
            e.Graphics.DrawRectangle(border, 81, 54, 5, 15);
            if (sample.Percent is { } percent) e.Graphics.FillRectangle(fill, 20, 48, 57 * percent / 100f, 27);
            e.Graphics.DrawString(sample.Percent is { } p ? $"{p}%" : "—", number, text, 98, 36);
            var state = sample.Charging == true ? "Carregando" : sample.OnAcPower == false ? "Em uso" : "Conectada";
            e.Graphics.DrawString(state, small, muted, 98, 67);
        }
        else
        {
            // Plug and cable: a desktop has no meaningful battery percentage.
            e.Graphics.DrawLine(border, 30, 39, 30, 51); e.Graphics.DrawLine(border, 47, 39, 47, 51);
            e.Graphics.DrawRectangle(border, 22, 51, 33, 23);
            e.Graphics.DrawLine(border, 38, 74, 38, 82); e.Graphics.DrawLine(border, 38, 82, 74, 82);
            e.Graphics.DrawString(sample == null ? "Não medido" : sample.HasBattery == false ? "Sem bateria" : "Bateria desconhecida", small, text, 98, 51);
        }
        e.Graphics.DrawString(sample == null ? "Aguardando coleta / relatório sem energia" : source, small, muted, 12, 88);
        AccessibleDescription = source;
    }
}

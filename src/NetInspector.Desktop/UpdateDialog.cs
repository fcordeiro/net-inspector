using System.Diagnostics;
using NetInspector.Core;

namespace NetInspector.Desktop;

public sealed class UpdateDialog : Form
{
    private readonly Label result = new() { Dock = DockStyle.Fill, Padding = new Padding(18), Text = "Consultando a última versão estável no GitHub…", ForeColor = Theme.Text, BackColor = Theme.Surface };
    private readonly Button download = Theme.Action("Abrir download", Theme.Blue);
    private readonly CancellationTokenSource cancellation = new();
    public UpdateDialog()
    {
        Theme.Apply(this, "Atualizações do Net Inspector"); MinimumSize = new(650, 380); Size = new(760, 420);
        var description = new Label { Dock = DockStyle.Top, Height = 80, Text = "Versão instalada: " + ReleaseUpdates.Current.ToString(3) + ". A consulta busca releases estáveis oficiais. O download abre no navegador; feche o aplicativo antes de substituir o EXE portátil." };
        var close = Theme.Action("Fechar", Theme.Surface); var buttons = new ControlRow { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(0, 12, 0, 0) };
        download.Enabled = false; buttons.Controls.AddRange([download, close]);
        Controls.Add(result); Controls.Add(description); Controls.Add(buttons); Theme.ToolWindow(this, "Atualizações", description);
        close.Click += (_, _) => Close(); FormClosed += (_, _) => cancellation.Cancel();
        Shown += async (_, _) =>
        {
            try
            {
                using var http = new HttpClient(); var release = await ReleaseUpdates.Check(http, cancellation.Token);
                if (IsDisposed) return;
                if (release == null) { result.Text = "Nenhuma versão estável disponível."; return; }
                var newer = ReleaseUpdates.IsNewer(release.Version, ReleaseUpdates.Current);
                result.Text = newer ? $"Nova versão disponível: {release.Version.ToString(3)}\r\n\r\nVeja as notas e baixe o executável em:\r\n{release.Page}\r\n\r\nArquivo: {release.AssetName}" : "Você está usando a versão mais recente disponível: " + ReleaseUpdates.Current.ToString(3) + ".";
                download.Enabled = newer;
                download.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(release.Page) { UseShellExecute = true }); } catch (Exception ex) { result.Text = "Não foi possível abrir o navegador: " + ex.Message + "\r\n" + release.Page; } };
            }
            catch (Exception ex) { if (!IsDisposed) result.Text = "Não foi possível verificar atualizações.\r\n" + (ex is OperationCanceledException ? "Consulta cancelada ou tempo limite atingido." : ex.Message) + "\r\n\r\n" + ReleaseUpdates.ReleasesPage; }
        };
    }
}

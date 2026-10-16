using System.Diagnostics;
using NetInspector.Core;

namespace NetInspector.Desktop;

public sealed class UpdateDialog : Form
{
    private readonly Label result = new() { Dock = DockStyle.Fill, Padding = new Padding(18), Text = "Verificando atualizações…", ForeColor = Theme.Text, BackColor = Theme.Surface };
    private readonly Button download = Theme.Action("Abrir download", Theme.Blue);
    private readonly CancellationTokenSource cancellation = new();
    public UpdateDialog(Func<CancellationToken, Task<ReleaseUpdate?>>? check = null)
    {
        Theme.Apply(this, "Atualizações do Net Inspector"); MinimumSize = new(650, 380); Size = new(760, 420);
        var description = new Label { Dock = DockStyle.Top, Height = 48, Text = "Versão instalada: " + ReleaseUpdates.Current.ToString(3) };
        var close = Theme.Action("Fechar", Theme.Surface); var buttons = new ControlRow { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(0, 12, 0, 0) };
        download.Visible = false; download.Enabled = false; buttons.Controls.AddRange([download, close]);
        Controls.Add(result); Controls.Add(description); Controls.Add(buttons); Theme.ToolWindow(this, "Atualizações", description);
        close.Click += (_, _) => Close(); FormClosed += (_, _) => cancellation.Cancel();
        Shown += async (_, _) =>
        {
            try
            {
                using var http = new HttpClient(); var release = check == null ? await ReleaseUpdates.Check(http, cancellation.Token) : await check(cancellation.Token);
                if (IsDisposed) return;
                if (release == null) { result.Text = "Não foi possível verificar atualizações. Tente novamente mais tarde."; return; }
                var newer = ReleaseUpdates.IsNewer(release.Version, ReleaseUpdates.Current);
                result.Text = newer ? $"Nova versão disponível: {release.Version.ToString(3)}." : "Nenhuma atualização disponível.";
                download.Enabled = newer;
                download.Visible = newer;
                download.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(release.Page) { UseShellExecute = true }); } catch (Exception) { result.Text = "Não foi possível abrir o download. Tente novamente."; } };
            }
            catch (Exception) { if (!IsDisposed) result.Text = "Não foi possível verificar atualizações. Tente novamente mais tarde."; }
        };
    }
}

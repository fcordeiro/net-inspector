using System.Text.Json;
using System.Diagnostics;

namespace NetInspector.Desktop;

public sealed class DriverDialog : Form
{
    private sealed record Driver(string Id, int Revision, string Title)
    { public override string ToString() => Title; }
    private readonly CheckedListBox list = new() { Dock = DockStyle.Fill, CheckOnClick = true, BackColor = Theme.Surface, ForeColor = Theme.Text };
    private readonly TextBox log = new() { Dock = DockStyle.Bottom, Height = 170, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Background, ForeColor = Theme.Text };
    private readonly Button search = Theme.Action("Buscar drivers", Theme.Surface);
    private readonly Button install = Theme.Action("Instalar selecionados", Theme.Blue);
    private readonly Button close = Theme.Action("Fechar", Theme.Surface);
    private CancellationTokenSource? cancellation;
    private readonly ComboBox sources = new ThemeComboBox() { Width = 500 };
    private static readonly (string Name, string Url)[] OfficialSources = [
        ("Intel • Driver & Support Assistant", "https://www.intel.com/content/www/us/en/support/detect.html"),
        ("Dell • Command Update", "https://www.dell.com/support/kbdoc/en-us/000177325/dell-command-update"),
        ("Lenovo • System Update", "https://support.lenovo.com/us/en/solutions/ht003029-lenovo-system-update-update-drivers-bios-and-applications"),
        ("HP • Image Assistant (modelos compatíveis)", "https://ftp.hp.com/pub/caps-softpaq/cmit/HPIA.html"),
        ("AMD • Drivers e suporte", "https://www.amd.com/en/support/download/drivers.html"),
        ("NVIDIA • Drivers oficiais", "https://www.nvidia.com/en-us/drivers/") ];
    public DriverDialog(bool scanOnShow = true)
    {
        Theme.Apply(this, "Ferramentas • Atualizar drivers"); MinimumSize = new(850, 650); Size = new(980, 740);
        var help = new Label { Dock = DockStyle.Top, Height = 130, Padding = new(14), Text = "A lista abaixo consulta e instala drivers do Windows Update ou da política da organização. Para drivers externos, escolha um serviço oficial: o site abrirá no navegador e a detecção/instalação será feita pela ferramenta do fabricante. Prefira o fabricante do computador para preservar personalizações. Salve seu trabalho: a conexão ou a tela podem ser interrompidas. Nenhum reinício será feito automaticamente." };
        sources.Items.AddRange(OfficialSources.Select(s => s.Name).ToArray()); sources.SelectedIndex = 0;
        var openSource = Theme.Action("Abrir fonte oficial", Theme.Surface);
        var external = new ControlRow { Dock = DockStyle.Top, AutoSize = true, Padding = new(0, 12, 0, 12) };
        external.Controls.AddRange([sources, openSource]);
        openSource.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(OfficialSources[sources.SelectedIndex].Url) { UseShellExecute = true }); } catch (Exception ex) { Append("Não foi possível abrir o site: " + ex.Message); } };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 65, Padding = new(14, 8, 0, 0) };
        buttons.Controls.AddRange([search, install, close]); Controls.Add(list); Controls.Add(log); Controls.Add(external); Controls.Add(help); Controls.Add(buttons);
        Theme.ToolWindow(this, "Central de drivers", help);
        install.Enabled = false;
        search.Click += async (_, _) => await Run(false);
        install.Click += async (_, _) => await Run(true);
        close.Click += (_, _) => { if (cancellation == null) Close(); else cancellation.Cancel(); };
        FormClosing += (_, e) => { if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); } };
        if (scanOnShow) Shown += async (_, _) => await Run(false);
    }
    private void Append(string text)
    { if (log.TextLength > 100_000) log.Text = log.Text[^60_000..]; log.AppendText(text + Environment.NewLine); }
    private async Task Run(bool apply)
    {
        var selected = list.CheckedItems.Cast<Driver>().ToArray();
        if (apply && selected.Length == 0) { Append("Selecione ao menos um driver."); return; }
        if (apply && MessageBox.Show(this, "Instalar estes drivers e aceitar seus termos?\r\n\r\n" + string.Join("\r\n", selected.Select(d => d.Title)), "Confirmar instalação", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        cancellation = new(); search.Enabled = install.Enabled = list.Enabled = false; close.Text = "Cancelar";
        if (!apply) list.Items.Clear();
        var script = """
            $pc=Get-CimInstance Win32_ComputerSystem
            Write-Output ('Computador: ' + $pc.Manufacturer + ' / ' + $pc.Model)
            $session=New-Object -ComObject Microsoft.Update.Session
            $session.ClientApplicationID='Net Inspector'
            Write-Output 'Consultando drivers. Esta etapa pode levar vários minutos...'
            $found=$session.CreateUpdateSearcher().Search("IsInstalled=0 and IsHidden=0 and Type='Driver'")
            """;
        if (!apply)
            script += """

                for ($i=0; $i -lt $found.Updates.Count; $i++) {
                    $u=$found.Updates.Item($i)
                    Write-Output ('DRIVER:' + (@{Id=$u.Identity.UpdateID;Revision=$u.Identity.RevisionNumber;Title=$u.Title} | ConvertTo-Json -Compress))
                }
                Write-Output ('Drivers disponíveis: ' + $found.Updates.Count)
                """;
        else
        {
            var json = JsonSerializer.Serialize(selected);
            script += "\n$selected=" + MaintenanceRunner.Quote(json) + " | ConvertFrom-Json\n" + """
                $failed=$false
                foreach ($item in $selected) {
                    if (Test-Path -LiteralPath $cancelFile) { throw 'Cancelado antes do próximo driver.' }
                    $u=$null
                    for ($i=0; $i -lt $found.Updates.Count; $i++) {
                        $candidate=$found.Updates.Item($i)
                        if ($candidate.Identity.UpdateID -eq $item.Id -and $candidate.Identity.RevisionNumber -eq $item.Revision) { $u=$candidate; break }
                    }
                    if ($null -eq $u) { Write-Output ('Indisponível ou alterado; busque novamente: ' + $item.Title); $failed=$true; continue }
                    Write-Output ('Baixando: ' + $u.Title)
                    if ($u.InstallationBehavior.CanRequestUserInput) { Write-Output 'Ignorado: exige interação do fabricante.'; $failed=$true; continue }
                    if (-not $u.EulaAccepted) { $u.AcceptEula() }
                    $collection=New-Object -ComObject Microsoft.Update.UpdateColl; $null=$collection.Add($u)
                    $downloader=$session.CreateUpdateDownloader(); $downloader.Updates=$collection; $null=$downloader.Download()
                    if (-not $u.IsDownloaded) { Write-Output ('Download falhou: ' + $u.Title); $failed=$true; continue }
                    if (Test-Path -LiteralPath $cancelFile) { throw 'Cancelado antes da instalação.' }
                    $installer=$session.CreateUpdateInstaller(); $installer.Updates=$collection
                    if ($installer.RebootRequiredBeforeInstallation) { throw 'Há um reinício pendente. Reinicie manualmente antes de continuar.' }
                    $installer.AllowSourcePrompts=$false
                    $result=$installer.Install(); $detail=$result.GetUpdateResult(0)
                    Write-Output ($u.Title + ': resultado=' + $detail.ResultCode + '; HRESULT=' + $detail.HResult + '; reinício necessário=' + $result.RebootRequired)
                    if ($detail.ResultCode -ne 2) { $failed=$true }
                }
                if ($failed) { throw 'Alguns drivers não foram instalados. Consulte os resultados individuais.' }
                """;
        }
        try
        {
            var code = await MaintenanceRunner.Run(script, apply, text =>
            {
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    if (line.TrimStart().StartsWith("DRIVER:"))
                    { var driver = JsonSerializer.Deserialize<Driver>(line.Trim()[7..]); if (driver != null) list.Items.Add(driver, false); }
                    else Append(line);
            }, cancellation.Token);
            Append(code == 0 ? "Consulta/operação encerrada. Resultado 2 significa instalação concluída; outros resultados exigem revisão." : "Operação não concluída integralmente; consulte as mensagens acima.");
            if (apply) list.Items.Clear();
        }
        catch (Exception ex) { Append("Falha: " + ex.Message); }
        finally { cancellation.Dispose(); cancellation = null; search.Enabled = list.Enabled = true; install.Enabled = list.Items.Count > 0; close.Text = "Fechar"; }
    }
}

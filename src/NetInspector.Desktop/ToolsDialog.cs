using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using NetInspector.Core;

namespace NetInspector.Desktop;

public sealed class ToolsDialog : Form
{
    private readonly bool upgrade;
    private readonly TextBox output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Background, ForeColor = Theme.Text };
    private readonly Button execute = Theme.Action("Executar seleção", Theme.Blue);
    private readonly Button cancel = Theme.Action("Fechar", Theme.Surface);
    private readonly List<CheckBox> choices = [];
    private CancellationTokenSource? cancellation;
    public ToolsDialog(bool upgrade, bool scanOnShow = true)
    {
        this.upgrade = upgrade;
        Theme.Apply(this, upgrade ? "Ferramentas • Atualizar programas" : "Ferramentas • Liberar memória");
        MinimumSize = new Size(800, 660); Size = new Size(900, 750); StartPosition = FormStartPosition.CenterParent;
        var description = new Label { Dock = DockStyle.Top, Height = upgrade ? 120 : 140, Padding = new Padding(14), Text = upgrade
            ? "Busca atualizações disponíveis ao abrir esta janela. Revise a lista antes de atualizar. As instalações usam modo silencioso quando suportado e aceitam os termos dos programas e das fontes. Requer internet; o Windows ainda pode solicitar permissão de administrador. Salve seu trabalho."
            : "Selecione o que deseja limpar. A exclusão é permanente. Cookies encerram sessões em sites. Feche os navegadores antes de limpar cache/cookies; navegadores abertos serão ignorados. Reduzir a RAM é temporário e pode tornar a próxima abertura mais lenta. Prefetch também pode tornar as próximas inicializações mais lentas." };
        var options = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 0, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14, 0, 0, upgrade ? 0 : 12) };
        if (!upgrade)
            foreach (var (label, selected) in new[] {
                ("RAM: reduzir conjuntos de trabalho dos processos acessíveis", true),
                ("Arquivos temporários do usuário com mais de 24 horas", true),
                ("Cache de Chrome, Edge, Brave e Firefox", true),
                ("Cookies dos navegadores (encerra sessões em sites)", false),
                ("Esvaziar a lixeira do usuário", false),
                ("Prefetch do Windows (solicita administrador se necessário)", false),
                ("Downloads: arquivos com mais de 30 dias (inclui subpastas)", false),
                ("Caches e logs antigos: ChatGPT, Claude, Visual Studio, Code e Antigravity", false),
                ("Relatórios de falhas do usuário com mais de 7 dias", false),
                ("Windows.old e atualizações antigas: escolher na Limpeza de Disco do Windows", false) })
            { var choice = new ThemeCheckBox { Text = label, Checked = selected, AutoSize = true, Margin = new Padding(0, 4, 0, 4) }; choices.Add(choice); options.Controls.Add(choice); }
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(14, 6, 0, 0) };
        execute.Text = upgrade ? "Atualizar programas" : "Limpar selecionados";
        buttons.Controls.AddRange([execute, cancel]);
        if (upgrade) { Controls.Add(output); Controls.Add(options); }
        else
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(900, 540), SplitterDistance = 410, SplitterWidth = 6, Panel1MinSize = 120, Panel2MinSize = 80, FixedPanel = FixedPanel.Panel2, BackColor = Theme.Border };
            options.Dock = DockStyle.Fill; options.AutoSize = false; options.AutoScroll = true;
            split.Panel1.BackColor = split.Panel2.BackColor = Theme.Background;
            split.Panel1.Controls.Add(options); split.Panel2.Controls.Add(output); Controls.Add(split);
        }
        Controls.Add(description); Controls.Add(buttons);
        Theme.ToolWindow(this, upgrade ? "Central de atualizações" : "Memória e limpeza", description);
        if (!upgrade) { Size = new Size(980, 930); MinimumSize = new Size(950, 700); }
        execute.Click += async (_, _) => await Execute();
        cancel.Click += (_, _) => { if (cancellation != null) { cancellation.Cancel(); cancel.Enabled = false; } else Close(); };
        FormClosing += (_, e) => { if (cancellation == null) return; e.Cancel = true; cancellation.Cancel(); Append("Cancelamento solicitado; aguarde o encerramento da etapa atual."); };
        if (upgrade && scanOnShow) Shown += async (_, _) => await ScanUpdates();
    }
    private bool scanSucceeded;
    private async Task ScanUpdates()
    {
        cancellation = new(); execute.Enabled = false; cancel.Text = "Cancelar";
        try
        {
            Append("Buscando atualizações disponíveis…");
            var code = await MaintenanceRunner.Run("""
                if (-not (Get-Command winget.exe -ErrorAction SilentlyContinue)) { throw 'Atualizador Microsoft ausente. Instale o Instalador de Aplicativos pela Microsoft Store e reabra esta janela.' }
                & winget.exe source update --disable-interactivity
                if ($LASTEXITCODE -ne 0) { throw ('Falha ao atualizar fontes: ' + $LASTEXITCODE) }
                & winget.exe upgrade --accept-source-agreements --disable-interactivity
                if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189) { throw ('Falha na consulta: ' + $LASTEXITCODE) }
                """, false, Append, cancellation.Token);
            scanSucceeded = code == 0 && !cancellation.IsCancellationRequested;
        }
        catch (Exception ex) { Append("Falha na consulta: " + ex.Message); }
        finally { cancellation.Dispose(); cancellation = null; execute.Enabled = cancel.Enabled = true; cancel.Text = "Fechar"; execute.Text = scanSucceeded ? "Atualizar programas" : "Repetir consulta"; }
    }
    private void Append(string line)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => Append(line)); return; }
        if (output.TextLength > 120_000) output.Text = output.Text[^80_000..];
        output.AppendText(line + Environment.NewLine);
    }
    private async Task Execute()
    {
        if (upgrade && !scanSucceeded) { await ScanUpdates(); return; }
        var selection = upgrade ? null : new CleanupOptions(choices[0].Checked, choices[1].Checked, choices[2].Checked, choices[3].Checked, choices[4].Checked, choices[5].Checked, choices[6].Checked, choices[7].Checked, choices[8].Checked, choices[9].Checked);
        if (!upgrade && !choices.Any(c => c.Checked)) { Append("Selecione ao menos um item."); return; }
        var confirmation = upgrade ? "Atualizar todos os programas disponíveis? Os termos dos programas e fontes serão aceitos automaticamente."
            : "Executar somente estes itens?\r\n\r\n" + string.Join("\r\n", choices.Where(c => c.Checked).Select(c => "• " + c.Text)) + "\r\n\r\nArquivos, cookies e itens da lixeira excluídos não poderão ser recuperados pelo aplicativo.";
        if (MessageBox.Show(this, confirmation, "Confirmar ferramenta", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        cancellation = new(); var ct = cancellation.Token;
        execute.Enabled = false; foreach (var choice in choices) choice.Enabled = false; cancel.Text = "Cancelar";
        try
        {
            Append("Iniciando…");
            if (upgrade) await Upgrade(ct);
            else
            {
                var progress = new Progress<string>(Append);
                await Task.Run(() => Maintenance.Cleanup(selection!, progress, ct), ct);
                if (selection!.Prefetch)
                {
                    ct.ThrowIfCancellationRequested();
                    using var identity = WindowsIdentity.GetCurrent();
                    if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        var result = await Task.Run(() => Maintenance.CleanPrefetch(ct), ct);
                        Append($"Prefetch: {result.Deleted} arquivos removidos; {result.Skipped} ignorados.");
                    }
                    else
                    {
                        Append("Prefetch precisa de administrador. Autorize a janela do Windows apenas para esta etapa.");
                        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                        start.ArgumentList.Add("--maintenance-prefetch");
                        using var helper = Process.Start(start) ?? throw new IOException("Não foi possível iniciar a limpeza de Prefetch.");
                        // UAC helper performs only a fixed, bounded target. Wait for its result even after cancellation.
                        await helper.WaitForExitAsync();
                        Append(helper.ExitCode switch { 0 => "Prefetch: limpeza concluída.", 2 => "Prefetch: limpeza parcial; arquivos protegidos/em uso foram ignorados.", _ => "Prefetch: falha na limpeza. Código " + helper.ExitCode });
                    }
                }
                if (selection!.WindowsOld && !ct.IsCancellationRequested)
                    await MaintenanceRunner.Run(MaintenanceRunner.WindowsCleanup, true, Append, ct);
                Append(ct.IsCancellationRequested ? "Cancelado após a etapa atual. Operações já concluídas foram mantidas." : "Limpeza encerrada. Consulte os resultados e os itens ignorados acima.");
            }
        }
        catch (OperationCanceledException) { Append("Cancelado. Alterações já executadas foram mantidas; alguns instaladores podem continuar abertos."); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { Append("Permissão de administrador cancelada. Prefetch não foi limpo; demais resultados estão acima."); }
        catch (Exception ex) { Append("Falha: " + ex.Message); }
        finally { cancellation.Dispose(); cancellation = null; execute.Enabled = cancel.Enabled = true; cancel.Text = "Fechar"; foreach (var choice in choices) choice.Enabled = true; }
    }
    private async Task Upgrade(CancellationToken ct)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); " + Maintenance.UpgradeScript)) }) start.ArgumentList.Add(arg);
        start.StandardOutputEncoding = start.StandardErrorEncoding = Encoding.UTF8;
        using var process = Process.Start(start) ?? throw new IOException("Não foi possível iniciar o WinGet.");
        async Task Pump(StreamReader reader) { while (await reader.ReadLineAsync() is { } line) Append(line); }
        var stdout = Pump(process.StandardOutput); var stderr = Pump(process.StandardError);
        try { await process.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } await process.WaitForExitAsync(); throw; }
        finally { await Task.WhenAll(stdout, stderr); }
        Append(process.ExitCode == 0 ? "Atualização encerrada. Consulte os resultados por programa acima." : $"WinGet encerrou com código {process.ExitCode}. Pode haver atualizações parciais; consulte as mensagens acima e tente novamente.");
    }
}

using System.Text.Json;
using NetInspector.Core;
using NetMonitor = NetInspector.Core.Monitor;

namespace NetInspector.Desktop;

public sealed class InspectorForm : Form
{
    private readonly Dashboard dashboard = new();
    private readonly DurationPicker minutes = new() { Minimum = TestPolicy.MinimumMinutes, Maximum = TestPolicy.MaximumMinutes, Value = TestPolicy.DefaultMinutes, Width = 116 };
    private readonly CheckBox speed = new ThemeCheckBox() { Text = "Medir velocidade (Mbps)", Checked = true, AutoSize = true };
    private readonly ThemeComboBox budget = new() { Width = 92, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button start = Theme.Action("▶  Iniciar", Theme.Blue);
    private readonly Button stop = Theme.Action("■  Parar e salvar", Theme.Surface);
    private readonly Button open = Theme.Action("Abrir relatório", Theme.Surface);
    private readonly Button export = Theme.Action("Exportar diagnóstico", Theme.Surface);
    private readonly Button games = Theme.Action("Servidores de jogos", Theme.Surface);
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(12, 6, 0, 0), AutoEllipsis = true, Text = "Pronto para monitorar ou abrir um relatório existente." };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private CancellationTokenSource? cancellation;
    private NetMonitor? monitor;
    private bool running, loading, closeAfterSave;
    private Report? report;
    private List<GameTarget> targets = GameCatalog.Defaults;
    private readonly ToolStripMenuItem tools = new("Ferramentas");
    private readonly ToolStripMenuItem savePdf = new("Salvar relatório completo em PDF…") { Enabled = false, ShortcutKeys = Keys.Control | Keys.Shift | Keys.S };
    private readonly ToolStripMenuItem autoUpdate = new("Buscar atualizações ao iniciar") { CheckOnClick = true, Checked = true };
    private static readonly string UpdatePreference = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetInspector", "updates.txt");
    public InspectorForm(string? initialPath = null)
    {
        Theme.Apply(this, "Net Inspector • CONNECT10 INTERNET"); AllowDrop = true; stop.Enabled = false; export.Enabled = false;
        budget.Items.AddRange(["2 GB", "5 GB", "20 GB", "50 GB"]); budget.SelectedIndex = 2;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20, 8, 10, 8), WrapContents = true };
        actions.Controls.AddRange([start, stop, open, export, games]);
        var settings = new ControlRow { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20, 8, 10, 12), WrapContents = true };
        settings.Controls.AddRange([new Label { Text = "Duração (min):", AutoSize = true }, minutes, speed, new Label { Text = "Limite de consumo dos testes:", AutoSize = true }, budget]);
        var help = new LinkLabel { Text = "Como usar", AutoSize = true, Padding = new Padding(12, 0, 0, 0), LinkColor = Theme.Muted, ActiveLinkColor = Theme.Text, VisitedLinkColor = Theme.Muted };
        help.LinkClicked += (_, _) => MessageBox.Show(this, "Escolha a duração e clique em Iniciar teste. Use a internet normalmente e mantenha o computador ligado.\r\n\r\nAo finalizar, consulte Resumo e diagnóstico para ver os resultados e as ações recomendadas.\r\n\r\nMbps indica velocidade; GB é o limite de consumo dos testes de velocidade.\r\n\r\nPara consultar uma coleta anterior, use Abrir relatório ou arraste o arquivo para a janela. Compartilhe o relatório apenas com suporte de confiança.", "Como usar o Net Inspector", MessageBoxButtons.OK, MessageBoxIcon.Information);
        settings.Controls.Add(help);
        status.BackColor = Theme.Surface; status.ForeColor = Theme.Muted;
        Theme.StyleInputs(settings);
        Controls.Add(dashboard); Controls.Add(settings); Controls.Add(actions); Controls.Add(Theme.Header("Net Inspector")); Controls.Add(status); Controls.Add(Theme.CopyrightFooter());
        var menu = new MenuStrip { Dock = DockStyle.Top, BackColor = Theme.Surface, ForeColor = Theme.Text };
        var file = new ToolStripMenuItem("Arquivo");
        file.DropDownItems.Add(savePdf); savePdf.Click += async (_, _) => await ExportPdf();
        file.DropDownItems.Add(new ToolStripMenuItem("Sair", null, (_, _) => Close()) { ShortcutKeys = Keys.Alt | Keys.F4 });
        tools.DropDownItems.Add("Atualizar programas", null, (_, _) => { using var dialog = new ToolsDialog(true); dialog.ShowDialog(this); });
        tools.DropDownItems.Add("Liberar memória", null, (_, _) => { using var dialog = new ToolsDialog(false); dialog.ShowDialog(this); });
        tools.DropDownItems.Add("Limpar worktrees", null, (_, _) => { using var dialog = new WorktreeDialog(); dialog.ShowDialog(this); });
        tools.DropDownItems.Add("Limpar espaço livre (TRIM / HDD)", null, (_, _) => { using var dialog = new DiskDialog(true); dialog.ShowDialog(this); });
        tools.DropDownItems.Add("Analisar e desfragmentar disco", null, (_, _) => { using var dialog = new DiskDialog(); dialog.ShowDialog(this); });
        tools.DropDownItems.Add("Atualizar drivers", null, (_, _) => { using var dialog = new DriverDialog(); dialog.ShowDialog(this); });
        var helpMenu = new ToolStripMenuItem("Ajuda");
        helpMenu.DropDownItems.Add("Verificar atualizações…", null, (_, _) => { using var dialog = new UpdateDialog(); dialog.ShowDialog(this); });
        try { autoUpdate.Checked = !File.Exists(UpdatePreference) || File.ReadAllText(UpdatePreference).Trim() != "off"; } catch (IOException) { }
        autoUpdate.Click += (_, _) => { try { Directory.CreateDirectory(Path.GetDirectoryName(UpdatePreference)!); File.WriteAllText(UpdatePreference, autoUpdate.Checked ? "on" : "off"); } catch (Exception ex) { MessageBox.Show(this, "Não foi possível salvar a preferência: " + ex.Message); } };
        helpMenu.DropDownItems.Add(autoUpdate);
        helpMenu.DropDownItems.Add("Licenças de terceiros", null, (_, _) =>
        {
            using var dialog = new Form { Text = "Licenças de terceiros", Icon = Theme.ApplicationIcon, Size = new Size(800, 650), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Background, ForeColor = Theme.Text };
            using var stream = typeof(Theme).Assembly.GetManifestResourceStream("NetInspector.ThirdPartyNotices")!;
            using var reader = new StreamReader(stream);
            dialog.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = reader.ReadToEnd(), Font = new Font("Consolas", 9), BackColor = Theme.Surface, ForeColor = Theme.Text });
            dialog.ShowDialog(this);
        });
        helpMenu.DropDownItems.Add("Sobre", null, (_, _) => MessageBox.Show(this,
            "Net Inspector\r\nVersão " + typeof(InspectorForm).Assembly.GetName().Version?.ToString(3) + "\r\n\r\nDiagnóstico de conexão e manutenção do Windows.\r\n\r\n" + Branding.Copyright,
            "Sobre o Net Inspector", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.AddRange([file, tools, helpMenu]); MainMenuStrip = menu; Controls.Add(menu);
        start.Click += async (_, _) => await Start();
        stop.Click += (_, _) => { stop.Enabled = false; stop.Text = "Salvando…"; start.Text = "Finalizando…"; status.Text = "Encerrando testes e preparando relatório…"; dashboard.SetFinalizing(); cancellation?.Cancel(); };
        open.Click += async (_, _) => { using var dialog = new OpenFileDialog { Filter = "Relatórios e checkpoints|*.netinspect;*.json" }; if (dialog.ShowDialog(this) == DialogResult.OK) await LoadReport(dialog.FileName); };
        export.Click += (_, _) => Export();
        games.Click += (_, _) => { using var dialog = new GameTargetsDialog(targets); if (dialog.ShowDialog(this) == DialogResult.OK) targets = dialog.Targets; };
        timer.Tick += (_, _) => { if (monitor != null) dashboard.ShowSession(monitor.Snapshot()); };
        FormClosing += (_, e) => { if (!running) return; e.Cancel = true; closeAfterSave = true; dashboard.SetFinalizing(); cancellation?.Cancel(); status.Text = "Finalizando relatório antes de fechar…"; };
        DragEnter += (_, e) => { if (!running && !loading && e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += async (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) await LoadReport(files[0]); };
        Shown += async (_, _) => { if (initialPath != null) await LoadReport(initialPath); if (autoUpdate.Checked) await CheckUpdatesAtStartup(); };
    }
    private void SetBusy(bool value)
    {
        start.Enabled = open.Enabled = minutes.Enabled = speed.Enabled = budget.Enabled = games.Enabled = !value;
        tools.Enabled = !value;
        export.Enabled = !value && report != null;
        savePdf.Enabled = !value && report != null;
        start.Text = value ? (running ? "●  Teste em execução" : "Carregando…") : "▶  Iniciar teste";
        stop.Text = "■  Parar e salvar";
        stop.Enabled = running && value;
    }
    private async Task Start()
    {
        using var save = new SaveFileDialog { Filter = "Relatório Net Inspector|*.netinspect", FileName = $"net_inspector_{DateTime.Now:yyyyMMdd_HHmmss}.netinspect", Title = "Salvar relatório e checkpoint de recuperação" };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        var checkpoint = Path.ChangeExtension(save.FileName, ".json");
        try { await File.WriteAllTextAsync(checkpoint + ".tmp", "{}"); File.Delete(checkpoint + ".tmp"); }
        catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar nesta pasta: " + ex.Message); return; }
        var limit = new long[] { 2, 5, 20, 50 }[budget.SelectedIndex] * 1_000_000_000;
        cancellation = new(); monitor = new((int)minutes.Value, speed.Checked, limit, targets);
        running = true; SetBusy(true); stop.Enabled = true; timer.Start();
        dashboard.ShowSession(monitor.Snapshot(), [], live: true);
        status.Text = "Monitorando. Os dados de recuperação são salvos a cada 15 segundos.";
        try
        {
            await monitor.Run(checkpoint, cancellation.Token);
            timer.Stop(); stop.Enabled = false; start.Text = "Finalizando…"; stop.Text = "Salvando…"; status.Text = "Coletando eventos Windows e compactando…"; dashboard.SetFinalizing();
            using var logsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
            var logs = await WindowsInventory.Events(logsTimeout.Token);
            foreach (var log in logs.Where(p => p.Key.EndsWith(".error.txt"))) monitor.Log("warning", "Eventos Windows indisponíveis: " + log.Key + " — " + log.Value);
            monitor.Log("info", "Relatório final preparado. Eventos Windows: últimas 24h, até 400 avisos/erros por canal.");
            await monitor.SaveCheckpoint(checkpoint);
            var snapshot = monitor.Snapshot();
            await Task.Run(() => ReportArchive.Write(save.FileName, snapshot, logs));
            report = new(snapshot, logs); dashboard.ShowSession(snapshot, logs, live: false);
            status.Text = "Relatório salvo: " + save.FileName;
        }
        catch (Exception ex) { dashboard.SetSaveFailed(); status.Text = "Falha ao salvar: " + ex.Message; closeAfterSave = false; MessageBox.Show(this, $"{ex.Message}\r\nRecuperação disponível, se já gravada: {checkpoint}", "Falha ao salvar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally
        {
            timer.Stop(); running = false; monitor.Dispose(); monitor = null; cancellation.Dispose(); cancellation = null;
            SetBusy(false); stop.Enabled = false; if (closeAfterSave) Close();
        }
    }
    public async Task LoadReport(string path)
    {
        if (running || loading) return;
        loading = true; SetBusy(true); status.Text = "Interpretando dados e verificando integridade do relatório…";
        try
        {
            var loaded = await Task.Run(() =>
            {
                if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)) return ReportArchive.Read(path);
                if (new FileInfo(path).Length > 64L * 1024 * 1024) throw new InvalidDataException("Checkpoint excede 64 MB");
                var session = JsonSerializer.Deserialize<Session>(File.ReadAllText(path), Session.Json) ?? throw new InvalidDataException("Dados de recuperação inválidos");
                ReportArchive.ValidateSession(session); return new Report(session, []);
            });
            if (IsDisposed) return;
            report = loaded; dashboard.ShowSession(loaded.Session, loaded.WindowsLogs, live: false);
            status.Text = "Relatório aberto: " + Path.GetFileName(path) + (loaded.WindowsLogs.Count == 0 ? " • Sem eventos Windows neste arquivo" : " • Eventos Windows disponíveis para consulta");
        }
        catch (Exception ex) { status.Text = "Não foi possível abrir o relatório."; MessageBox.Show(this, ex.Message, "Relatório inválido", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { loading = false; if (!IsDisposed) SetBusy(false); }
    }
    private void Export()
    {
        if (report == null) return;
        using var save = new SaveFileDialog { Filter = "Diagnóstico legível|*.txt", FileName = "diagnostico_net_inspector.txt" };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(save.FileName, $"Net Inspector — {report.Session.StartedAt.ToLocalTime():dd/MM/yyyy HH:mm}\r\n{Branding.Copyright}\r\n\r\n" + string.Join("\r\n\r\n", Diagnosis.Analyze(report.Session).Select(f => $"[{f.Severity}] {f.Title}\r\nObservado: {f.Evidence}\r\nPossíveis causas: {f.Cause}\r\nAção recomendada: {f.Action}"))); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Falha ao exportar"); }
    }
    private async Task ExportPdf()
    {
        if (report == null || running || loading) return;
        using var save = new SaveFileDialog { Filter = "Relatório ilustrado em PDF|*.pdf", FileName = $"net_inspector_{report.Session.StartedAt.ToLocalTime():yyyyMMdd_HHmmss}.pdf" };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        loading = true; SetBusy(true); status.Text = "Preparando PDF completo com gráficos e categorias…";
        try { var snapshot = report; await Task.Run(() => PdfReport.Write(save.FileName, snapshot)); status.Text = "PDF salvo: " + save.FileName; }
        catch (Exception ex) { status.Text = "Falha ao salvar PDF."; MessageBox.Show(this, ex.Message, "Exportar PDF", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { loading = false; if (!IsDisposed) SetBusy(false); }
    }
    private async Task CheckUpdatesAtStartup()
    {
        try
        {
            using var http = new HttpClient(); var update = await ReleaseUpdates.Check(http, CancellationToken.None);
            if (IsDisposed || update == null || !ReleaseUpdates.IsNewer(update.Version, ReleaseUpdates.Current)) return;
            autoUpdate.Text = $"Nova versão {update.Version.ToString(3)} disponível - verificar atualizações";
            if (!running && !loading) status.Text = $"Net Inspector {update.Version.ToString(3)} disponível. Consulte Ajuda > Verificar atualizações.";
        }
        catch (Exception) { /* Background checks never interrupt a diagnostic; the manual check displays errors. */ }
    }
}

internal sealed class GameTargetsDialog : Form
{
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Theme.Surface, ForeColor = Theme.Text, EnableHeadersVisualStyles = false, BorderStyle = BorderStyle.None, GridColor = Theme.Border, DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Surface, ForeColor = Theme.Text, SelectionBackColor = Theme.Blue, SelectionForeColor = Theme.Text }, ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Raised, ForeColor = Theme.Text } };
    public List<GameTarget> Targets { get; private set; } = [];
    public GameTargetsDialog(IEnumerable<GameTarget> targets)
    {
        BackColor = Theme.Background; ForeColor = Theme.Text; Icon = Theme.ApplicationIcon; Text = "Servidores de jogos — destinos específicos"; Size = new Size(900, 470); MinimumSize = new Size(800, 400); StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10);
        grid.Columns.Add("Name", "Nome / jogo"); grid.Columns.Add("Host", "Host ou IP"); grid.Columns.Add("Port", "Porta");
        grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Protocol", HeaderText = "Protocolo", DataSource = new[] { "Minecraft", "A2S", "TCP" } });
        foreach (var target in targets) grid.Rows.Add(target.Name, target.Host, target.Port, target.Protocol);
        var info = new Label { Dock = DockStyle.Top, Height = 84, Padding = new Padding(10), Text = "Até 10 destinos. Minecraft Java: status + ping/pong TCP (geralmente 25565).\r\nA2S: consulta UDP de servidores Source/CS compatíveis; informe a porta de consulta (geralmente 27015).\r\nTCP apenas verifica conexão. Não use a porta de jogo como TCP se o jogo usa UDP. Remova linhas selecionadas com Delete." };
        var ok = Theme.Action("Aplicar servidores", Theme.Blue); ok.Dock = DockStyle.Bottom;
        ok.Click += (_, _) =>
        {
            try
            {
                grid.EndEdit();
                Targets = grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r => new GameTarget(r.Cells[0].Value?.ToString()?.Trim() ?? "", r.Cells[1].Value?.ToString()?.Trim() ?? "", int.TryParse(r.Cells[2].Value?.ToString(), out var port) ? port : 0, r.Cells[3].Value?.ToString() ?? "")).ToList();
                if (Targets.Count > 10) throw new InvalidDataException("Máximo de 10 destinos");
                foreach (var target in Targets) GameCatalog.Validate(target);
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Revise os servidores"); }
        };
        Controls.Add(grid); Controls.Add(info); Controls.Add(ok);
    }
}

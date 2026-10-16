using System.Diagnostics;
using System.Xml.Linq;
using NetInspector.Core;

namespace NetInspector.Desktop;

public sealed class Dashboard : UserControl
{
    private readonly PowerMeter powerMeter = new();
    private readonly ResourceMeter cpuMeter = new(true), memoryMeter = new(false);
    private readonly ProgressBar testProgress = new TestProgressBar() { Dock = DockStyle.Top, Height = 10, Maximum = 1000, AccessibleName = "Progresso da coleta" };
    private readonly Label eta = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Text = "Progresso do teste • aguardando início", TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private bool liveCollection, finalizing;
    public void SetFinalizing()
    {
        liveCollection = false; finalizing = true;
        eta.Text = "Finalizando relatório • coletando eventos e salvando • ETA indisponível nesta etapa (pode levar cerca de 100s)";
        testProgress.Style = ProgressBarStyle.Marquee;
    }
    public void SetSaveFailed()
    {
        liveCollection = false; finalizing = false; testProgress.Style = ProgressBarStyle.Continuous;
        eta.Text = "Falha ao finalizar relatório • consulte a mensagem de erro e o checkpoint de recuperação";
    }
    private readonly ToolTip summaryDetails = new() { AutoPopDelay = 20000 };
    private readonly Label summary = new() { Dock = DockStyle.Top, Height = 36, Padding = new Padding(14, 8, 14, 0), AutoEllipsis = true, Font = new Font("Segoe UI", 11), BackColor = Theme.Raised, ForeColor = Theme.Text, Text = "Pronto para iniciar ou abrir um relatório." };
    private readonly TabControl tabs = new FlatTabs() { Dock = DockStyle.Fill, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(0, 1), Appearance = TabAppearance.FlatButtons };
    private readonly ListBox navigation = new() { Dock = DockStyle.Left, Width = 232, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 36, BorderStyle = BorderStyle.None, BackColor = Theme.Background, ForeColor = Theme.Muted, IntegralHeight = false };
    private readonly CheckBox showAllAdapters = new ThemeCheckBox() { Text = "Mostrar todos os adaptadores (avançado)", Dock = DockStyle.Top, Height = 36, ForeColor = Theme.Muted };
    private readonly CheckBox showAllHistory = new ThemeCheckBox() { Text = "Mostrar histórico de todos os adaptadores (avançado)", Dock = DockStyle.Top, Height = 36, ForeColor = Theme.Muted };
    private readonly Label connectionInfo = new() { Dock = DockStyle.Top, Height = 70, ForeColor = Theme.Text, Padding = new Padding(8), BackColor = Theme.Raised };
    private readonly Plot latency = new() { Caption = "Latência • Cloudflare (ms)" };
    private readonly Plot jitter = new() { Caption = "Variação entre respostas consecutivas (ms)" };
    private readonly Plot download = new() { Caption = "Download (Mbps)", LineColor = Color.FromArgb(54, 116, 224) };
    private readonly Plot upload = new() { Caption = "Upload (Mbps)", LineColor = Color.FromArgb(140, 90, 210) };
    private readonly DataGridView stats = Grid(), services = Grid(), pings = Grid(), speeds = Grid(), network = Grid(), adapters = Grid(), extensions = Grid(), events = Grid(), windows = Grid(), providers = Grid(), gameServers = Grid();
    private readonly RichTextBox findings = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = new Font("Segoe UI", 11), DetectUrls = false };
    private readonly TextBox machineInfo = new() { Dock = DockStyle.Top, Height = 108, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10), ScrollBars = ScrollBars.Vertical };
    private readonly RichTextBox networkSettings = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = new Font("Consolas", 10), ScrollBars = RichTextBoxScrollBars.Both, WordWrap = false };
    private readonly ComboBox logChoice = new ThemeComboBox() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label logInfo = new() { Dock = DockStyle.Top, Height = 60, Padding = new Padding(6) };
    private Dictionary<string, string> logs = [];
    private Session? current;
    public Dashboard()
    {
        Dock = DockStyle.Fill; BackColor = Theme.Background; ForeColor = Theme.Text;
        navigation.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var selected = e.Index == navigation.SelectedIndex;
            using var background = new SolidBrush(selected ? Theme.Raised : Theme.Background);
            e.Graphics.FillRectangle(background, e.Bounds);
            if (selected) { using var stripe = new SolidBrush(Theme.Orange); e.Graphics.FillRectangle(stripe, e.Bounds.X, e.Bounds.Y + 6, 3, e.Bounds.Height - 12); }
            TextRenderer.DrawText(e.Graphics, navigation.Items[e.Index].ToString(), Font, new Rectangle(e.Bounds.X + 14, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height), selected ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        };
        navigation.SelectedIndexChanged += (_, _) => { if (navigation.SelectedIndex >= 0) tabs.SelectedIndex = navigation.SelectedIndex; };
        showAllAdapters.CheckedChanged += (_, _) => { if (current != null) ShowSession(current); };
        showAllHistory.CheckedChanged += (_, _) => { if (current != null) ShowSession(current); };
        var charts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Background };
        charts.ColumnStyles.Add(new(SizeType.Percent, 50)); charts.ColumnStyles.Add(new(SizeType.Percent, 50));
        charts.RowStyles.Add(new(SizeType.Percent, 50)); charts.RowStyles.Add(new(SizeType.Percent, 50));
        charts.Controls.Add(latency, 0, 0); charts.Controls.Add(jitter, 1, 0); charts.Controls.Add(download, 0, 1); charts.Controls.Add(upload, 1, 1);
        AddTab("Resumo e diagnóstico", findings); AddTab("Gráficos", charts); AddTab("Qualidade da conexão", WithHint(WithDetails(stats, "Interpretacao"), "Durante teste de velocidade = ping medido junto com download/upload; não indica falha por si só. Selecione a linha para detalhes.")); AddTab("Sites e streaming", WithHint(WithDetails(services, "Resultado", "Detalhe"), "✓ HTTPS respondeu   •   ✕ Falha na sondagem   •   ? Acesso restrito. Não mede reprodução de vídeo."));
        var providerPanel = new Panel { Dock = DockStyle.Fill };
        var openStatus = Theme.Action("Abrir fonte de status selecionada", Theme.Surface); openStatus.Dock = DockStyle.Top;
        openStatus.Click += (_, _) =>
        {
            if (providers.CurrentRow?.Cells["Servico"].Value is not string name) return;
            var provider = GameCatalog.Providers.FirstOrDefault(p => p.Name == name);
            if (provider != null) Process.Start(new ProcessStartInfo(provider.Page) { UseShellExecute = true });
        };
        providerPanel.Controls.Add(WithDetails(providers, "Descricao", "ComponentesAfetados", "Incidentes")); providerPanel.Controls.Add(openStatus);
        AddTab("Status dos jogos", providerPanel); AddTab("Servidores de jogos", WithDetails(gameServers, "Informacoes", "Detalhe"));
        AddTab("Pings", pings); AddTab("Velocidade", speeds); var history = new Panel { Dock = DockStyle.Fill }; history.Controls.Add(network); history.Controls.Add(showAllHistory); history.Controls.Add(connectionInfo);
        AddTab("Conexão no tempo", history);
        var machine = new Panel { Dock = DockStyle.Fill }; machine.Controls.Add(adapters); machine.Controls.Add(showAllAdapters); machine.Controls.Add(machineInfo);
        AddTab("Meu PC e rede", machine); AddTab("Navegadores e extensões", extensions); AddTab("Eventos do programa", WithDetails(events, "Mensagem"));
        var logPanel = new Panel { Dock = DockStyle.Fill }; logPanel.Controls.Add(WithDetails(windows, "Origem", "Codigo", "Informacoes")); logPanel.Controls.Add(logInfo); logPanel.Controls.Add(logChoice);
        AddTab("Eventos Windows", logPanel);
        AddTab("Wi-Fi e configurações", networkSettings);
        logChoice.SelectedIndexChanged += (_, _) => ShowWindowsLog();
        tabs.SelectedIndexChanged += (_, _) => { navigation.SelectedIndex = tabs.SelectedIndex; if (current != null) ShowSession(current); };
        var resources = new TableLayoutPanel { Dock = DockStyle.Top, Height = 120, ColumnCount = 3, RowCount = 1 };
        resources.ColumnStyles.Add(new(SizeType.Percent, 34)); resources.ColumnStyles.Add(new(SizeType.Percent, 33)); resources.ColumnStyles.Add(new(SizeType.Percent, 33));
        resources.Controls.Add(cpuMeter, 0, 0); resources.Controls.Add(memoryMeter, 1, 0); resources.Controls.Add(powerMeter, 2, 0);
        var progress = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Background, Padding = new Padding(14, 8, 14, 8) };
        var progressTrack = new Panel { Dock = DockStyle.Right, Width = 280, Padding = new Padding(0, 9, 0, 0) };
        progressTrack.Controls.Add(testProgress);
        progress.Controls.Add(eta); progress.Controls.Add(progressTrack);
        Controls.Add(tabs); Controls.Add(navigation); Controls.Add(resources); Controls.Add(summary); Controls.Add(progress);
        Disposed += (_, _) => summaryDetails.Dispose();
        navigation.SelectedIndex = 0;
        findings.Text = "SUA CONEXÃO, EM FOCO\n\n1. Escolha a duração e clique em Iniciar teste.\n\n2. Use a internet normalmente e mantenha o computador ligado.\n\n3. Ao finalizar, consulte aqui os resultados e as ações recomendadas.\n\nJá tem um relatório? Use Abrir relatório ou arraste o arquivo para esta janela.\n\nOs indicadores ajudam a interpretar os resultados:\n✓ Medição dentro dos critérios observados\n✕ Algo merece atenção\n? Ainda não há dados suficientes para concluir";
    }
    private void AddTab(string title, Control child) { var page = new TabPage(title) { Padding = new Padding(16), BackColor = Theme.Surface, ForeColor = Theme.Text, UseVisualStyleBackColor = false }; page.Controls.Add(child); tabs.TabPages.Add(page); navigation.Items.Add(title); }
    private static Control WithHint(Control content, string text)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(content);
        panel.Controls.Add(new Label { Text = text, Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 4, 6, 4), ForeColor = Theme.Muted });
        return panel;
    }
    private static Control WithDetails(DataGridView grid, params string[] columns)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var detail = new TextBox { Dock = DockStyle.Bottom, Height = 94, Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Background, ForeColor = Theme.Muted, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10), Text = "Selecione uma linha para ler as informações completas." };
        grid.SelectionChanged += (_, _) => detail.Text = grid.CurrentRow is not { } row ? "Selecione uma linha para ler as informações completas." : string.Join("\r\n", columns.Where(grid.Columns.Contains).Select(c => grid.Columns[c]!.HeaderText + ": " + row.Cells[c].Value));
        panel.Controls.Add(grid); panel.Controls.Add(detail); return panel;
    }
    private static DataGridView Grid()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, BackgroundColor = Theme.Surface, BorderStyle = BorderStyle.None, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, RowTemplate = { Height = 38 }, AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Raised }, ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Segoe UI", 10, FontStyle.Bold), BackColor = Theme.Background, ForeColor = Theme.Muted }, EnableHeadersVisualStyles = false, GridColor = Theme.Border, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, ColumnHeadersHeight = 42, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Theme.Text, BackColor = Theme.Surface, SelectionBackColor = Color.FromArgb(30, 73, 112), SelectionForeColor = Theme.Text, Padding = new Padding(6) } };
        grid.ColumnAdded += (_, e) => { if (e.Column.Name == "Interpretacao") e.Column.Visible = false; e.Column.HeaderText = e.Column.Name switch
        {
            "Interpretacao" => "Como interpretar", "Horario" => "Horário", "Servico" => "Serviço", "Situacao" => "Situação", "Descricao" => "Descrição", "Informacoes" => "Informações", "Codigo" => "Código", "Versao" => "Versão", "Extensao" => "Extensão",
            "ConexaoPrincipal" => "Conexão principal", "LinkMbps" => "Link (Mbps)", "EnderecosIP" => "Endereços IP", "LatenciaMediaMs" => "Latência (ms)", "LatenciaP95Ms" => "P95 (ms)", "LatenciaMs" => "Latência (ms)", "JitterMedioMs" => "Jitter (ms)", "PerdaPct" => "Perda ICMP (%)",
            "DuranteVelocidade" => "Teste de velocidade simultâneo", "VelocidadeMbps" => "Velocidade (Mbps)", "DadosMB" => "Dados (MB)", "DuracaoSegundos" => "Duração (s)", "Conexoes" => "Conexões", "Direcao" => "Direção", "DNSms" => "DNS (ms)", "TCPms" => "TCP (ms)", "HTTPSms" => "HTTPS (ms)",
            _ => System.Text.RegularExpressions.Regex.Replace(e.Column.Name, "(?<=[a-z])(?=[A-Z])", " ")
        }; };
        grid.DataBindingComplete += (_, _) =>
        {
            if (!grid.Columns.Contains("Status")) return;
            grid.Columns["Status"]!.Frozen = true;
            foreach (DataGridViewRow row in grid.Rows)
            {
                var cell = row.Cells["Status"]; var value = cell.Value?.ToString() ?? "";
                cell.Style.ForeColor = value.StartsWith("✓") ? Theme.Good : value.StartsWith("✕") ? Theme.Bad : Theme.Orange;

            }
        };
        return grid;
    }
    private static string F(double? value) => value?.ToString("F2") ?? "Não medido";
    private static string When(DateTimeOffset at) => at.ToLocalTime().ToString("dd/MM HH:mm:ss");
    private static string TypeName(string type) => type switch { "Wireless80211" => "Wi-Fi", "Ethernet" or "GigabitEthernet" or "FastEthernetT" => "Cabo Ethernet", "Loopback" => "Interface interna", "Tunnel" => "Túnel / VPN", _ => type };
    private static string NetworkState(string state) => state switch { "Up" => "Conectado", "Down" => "Desconectado", "Dormant" => "Em espera", _ => state };
    private static string BrowserName(string name) => name.StartsWith("Chrome", StringComparison.OrdinalIgnoreCase) ? "Google Chrome" : name.Contains("MSEdge", StringComparison.OrdinalIgnoreCase) ? "Microsoft Edge" : name.StartsWith("Firefox", StringComparison.OrdinalIgnoreCase) ? "Mozilla Firefox" : name.StartsWith("Brave", StringComparison.OrdinalIgnoreCase) ? "Brave" : name;
    private static string LinkSpeed(long bps) => bps > 0 ? (bps / 1_000_000.0).ToString("F0") : "Não informado";
    private static string ServiceOutcome(string outcome) => outcome switch { "reachable" => "HTTPS respondeu", "restricted" => "Acesso restrito / proteção do site", "http_error" => "Erro retornado pelo site", "DNS_failed" => "Falha ao resolver endereço", "TCP_failed" => "Falha de conexão TCP", "HTTPS_failed" => "Falha HTTPS / TLS", _ => outcome };
    public void ShowSession(Session s, Dictionary<string, string>? windowsLogs = null, bool? live = null)
    {
        if (live.HasValue) { liveCollection = live.Value; finalizing = false; }
        current = s;
        var lastResource = s.Resources.LastOrDefault();
        cpuMeter.ShowSample(lastResource); memoryMeter.ShowSample(lastResource); powerMeter.ShowSample(lastResource?.Power);
        if (!finalizing)
        {
            testProgress.Style = ProgressBarStyle.Continuous;
            var durationSeconds = Math.Max(1, s.RequestedMinutes * 60.0);
            var seconds = Math.Max(0, ((s.EndedAt ?? (liveCollection ? DateTimeOffset.UtcNow : s.Resources.LastOrDefault()?.At ?? s.StartedAt)) - s.StartedAt).TotalSeconds);
            testProgress.Value = s.State == "completed" ? 1000 : (int)Math.Clamp(seconds / durationSeconds * 1000, 0, 1000);
            var remaining = TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling(durationSeconds - seconds)));
            eta.Text = liveCollection && s.State == "running"
                ? $"Coletando • {Math.Min(100, seconds / durationSeconds * 100):F0}% • ETA restante: {remaining:hh\\:mm\\:ss}"
                : s.State == "completed" ? "Teste concluído • 100% da coleta"
                : s.State == "stopped" ? $"Teste encerrado pelo usuário • {testProgress.Value / 10.0:F0}% do tempo previsto"
                : s.State == "error" ? "Coleta encerrada com erro • dados parciais"
                : "Checkpoint parcial • coleta inativa";
        }
        var adapter = s.Machine.Adapters.FirstOrDefault(a => a.Id == s.Machine.PrimaryAdapterId);
        var lastRoute = s.Network.LastOrDefault(n => n.IsPrimary == true);
        var state = s.State switch { "running" => "Monitorando", "completed" => "Concluído", "stopped" => "Encerrado pelo usuário", "error" => "Coleta com erro", _ => s.State };
        var elapsed = (s.EndedAt ?? DateTimeOffset.UtcNow) - s.StartedAt;
        var linkBps = lastRoute?.LinkBps ?? adapter?.LinkBps ?? 0;
        summary.Text = $"{lastRoute?.Adapter ?? adapter?.Name ?? "Conexão não identificada"}" +
            (linkBps > 0 ? $"   •   Link: {linkBps / 1_000_000.0:F0} Mbps" : "") +
            $"   •   {s.Speeds.Sum(p => p.Bytes) / 1_000_000.0:F1} MB usados";
        var details = $"Início: {s.StartedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss} • {state}\r\nDuração: {elapsed:hh\\:mm\\:ss}\r\n{s.Pings.Count} sondagens / {s.Pings.Count(p => p.Success)} respostas\r\nTipo: {TypeName(lastRoute?.Type ?? adapter?.Type ?? "Não identificado")}\r\nConsumo referente aos testes de velocidade. Link é a velocidade negociada pelo adaptador.";
        summaryDetails.SetToolTip(summary, details);
        summary.AccessibleDescription = details;
        if (findings.Visible) RenderDiagnosis(s);
        latency.Points = s.Pings.Where(p => p.Target == "Cloudflare").Select(p => (p.At, p.Ms)).ToArray();
        var cloud = s.Pings.Where(p => p.Target == "Cloudflare").ToArray();
        jitter.Points = cloud.Zip(cloud.Skip(1)).Select(p => (p.Second.At, p.First.Ms.HasValue && p.Second.Ms.HasValue && p.Second.At - p.First.At < TimeSpan.FromSeconds(10) ? (double?)Math.Abs(p.Second.Ms.Value - p.First.Ms.Value) : null)).ToArray();
        var interval = Version.TryParse(s.AppVersion, out var version) && version >= new Version(1, 2, 1)
            ? NetInspector.Core.Monitor.SpeedInterval(s.RequestedMinutes) : TimeSpan.FromMinutes(15);
        var cadence = interval.TotalMinutes >= 1 ? $"{interval.TotalMinutes:0.#} min" : $"{interval.TotalSeconds:0} s";
        download.Caption = $"Download (Mbps) • intervalo {cadence}";
        upload.Caption = $"Upload (Mbps) • intervalo {cadence}";
        download.Points = s.Speeds.Where(p => p.Direction == "download").Select(p => (p.At, p.Mbps)).ToArray();
        upload.Points = s.Speeds.Where(p => p.Direction == "upload").Select(p => (p.At, p.Mbps)).ToArray();
        foreach (var plot in new[] { latency, jitter, download, upload }) plot.Invalidate();
        if (stats.Visible) StableView.SetRows(stats, s.Pings.GroupBy(p => new { p.Target, p.UnderLoad }).Select(g => { var p = Diagnosis.Stats(g.Key.Target, g); return new { Status = p.Samples < 10 ? "? Poucas amostras" : p.Received == 0 ? "? Sem resposta" : g.Key.UnderLoad ? "Durante teste de velocidade" : p.LossPercent >= 2 || p.JitterMs > 20 || p.AverageMs > 100 ? "✕ Atenção" : "✓ OK", Destino = p.Target, DuranteVelocidade = g.Key.UnderLoad ? "Sim" : "Não", Tentativas = p.Samples, Respostas = p.Received, PerdaPct = F(p.LossPercent), LatenciaMediaMs = F(p.AverageMs), LatenciaP95Ms = F(p.P95Ms), JitterMedioMs = F(p.JitterMs), Interpretacao = (p.Samples < 10 ? $"Apenas {p.Samples} de pelo menos 10 amostras necessárias para aplicar os critérios de qualidade. Aguarde a coleta continuar ou faça um teste mais longo. " : p.Received == 0 ? "Nenhuma resposta ICMP. O destino pode bloquear ping; isso não comprova queda da conexão. " : "As métricas descrevem apenas este destino e período. ") + (g.Key.UnderLoad ? "Amostras durante download/upload: a transferência pode elevar latência e jitter. Compare com as linhas sem carga; estas amostras não recebem o mesmo veredito das medições em repouso." : "Amostras sem teste de velocidade simultâneo. Critérios de atenção: perda parcial ≥2%, latência média >100ms ou jitter >20ms.") }; }).ToArray());
        if (services.Visible) StableView.SetRows(services, s.Services.OrderByDescending(p => p.At).Select(p => new { Status = p.Outcome == "reachable" ? "✓ OK" : p.Outcome == "restricted" ? "? Restrito" : "✕ Falha", Horario = When(p.At), Servico = p.Name, Categoria = p.Category, DNSms = F(p.DnsMs), TCPms = F(p.TcpMs), HTTPSms = F(p.HttpsMs), HTTP = p.HttpStatus?.ToString() ?? "Sem resposta", Resultado = ServiceOutcome(p.Outcome), Detalhe = p.Error }).ToArray());
        if (pings.Visible) StableView.SetRows(pings, s.Pings.OrderByDescending(p => p.At).Select(p => new { Status = p.Success ? "✓ Respondeu" : "? Sem resposta", Horario = When(p.At), Destino = p.Target, IP = p.Address, Resultado = p.Success ? "Respondeu" : "Sem resposta", LatenciaMs = F(p.Ms), DuranteVelocidade = p.UnderLoad ? "Sim" : "Não", Detalhe = p.Status }).ToArray());
        if (speeds.Visible) StableView.SetRows(speeds, s.Speeds.Select(p => new { Status = p.Mbps.HasValue ? "✓ Medido" : "? Não medido", Horario = When(p.At), Direcao = p.Direction == "download" ? "Download" : "Upload", VelocidadeMbps = F(p.Mbps), DadosMB = (p.Bytes / 1_000_000.0).ToString("F2"), DuracaoSegundos = p.Seconds.ToString("F2"), Conexoes = p.Streams, Resultado = p.Error ?? "Medição concluída" }).ToArray());
        connectionInfo.Text = $"{TypeName(adapter?.Type ?? "Não identificada")}  •  {adapter?.Name ?? "Sem rota principal identificada"}\r\n{(s.Network.Any(n => n.IsPrimary.HasValue) ? "Interface da rota IPv4 para as sondagens (1.1.1.1), verificada a cada 10 s. VPN pode alterar a rota." : "Relatório antigo: interface da rota inicial. Trocas de rede não foram registradas.")}";
        if (network.Visible) StableView.SetRows(network, s.Network.Where(p => showAllHistory.Checked || p.IsPrimary == true || (p.IsPrimary == null && adapter != null && p.Adapter == adapter.Name)).OrderByDescending(p => p.At).Select(p => new { Uso = p.IsPrimary == true ? "✓ Rota das sondagens" : p.IsPrimary == false ? "Secundário" : "Rota inicial (legado)", Horario = When(p.At), Adaptador = p.Adapter, Tipo = TypeName(p.Type), Estado = NetworkState(p.Status), LinkMbps = LinkSpeed(p.LinkBps), RecebidosMB = (p.ReceivedBytes / 1_000_000.0).ToString("F1"), EnviadosMB = (p.SentBytes / 1_000_000.0).ToString("F1"), ErrosRecepcao = p.ReceiveErrors, ErrosEnvio = p.SendErrors }).ToArray());
        if (adapters.Visible)
        {
            machineInfo.Text = $"Conexão principal no início: {TypeName(adapter?.Type ?? "Não identificada")} • {adapter?.Name ?? "Sem rota identificada"}\r\nSistema: {s.Machine.Os}\r\nNavegador padrão para HTTPS: {BrowserName(s.Machine.DefaultBrowser)}\r\nGateway inicial: {s.Machine.Gateway ?? "Não identificado"}\r\n{string.Join(" | ", s.Machine.Limitations)}";
            StableView.SetRows(adapters, s.Machine.Adapters.Where(a => showAllAdapters.Checked || a.Id == s.Machine.PrimaryAdapterId).OrderByDescending(a => a.Id == s.Machine.PrimaryAdapterId).Select(a => new { Adaptador = a.Name, ConexaoPrincipal = a.Id == s.Machine.PrimaryAdapterId ? "Sim" : "Não", Tipo = TypeName(a.Type), Estado = NetworkState(a.Status), LinkMbps = LinkSpeed(a.LinkBps), EnderecosIP = string.Join(", ", a.Addresses), Gateway = string.Join(", ", a.Gateways), DNS = string.Join(", ", a.Dns) }).ToArray());
        }
        if (extensions.Visible) StableView.SetRows(extensions, s.Machine.Extensions.Select(e => new { Navegador = e.Browser, Perfil = e.Profile, Extensao = e.Name.StartsWith("__MSG_") ? "Nome localizado indisponível; ID: " + e.Id : e.Name, Versao = e.Version, Estado = e.State, Identificador = e.Id }).ToArray());
        if (events.Visible) StableView.SetRows(events, s.Events.Select(e => new { Horario = When(e.At), Tipo = e.Level switch { "error" => "Erro", "warning" => "Aviso", _ => "Informação" }, Mensagem = e.Message }).ToArray());
        if (networkSettings.Visible) RenderSettings(s);
        if (providers.Visible)
        {
            StableView.SetRows(providers, s.ProviderStatus.OrderByDescending(p => p.At).Select(p => new { Status = p.State == "operational" ? "✓ Sem incidentes" : p.State == "degraded" ? "✕ Incidente" : "? Consultar", Servico = p.Provider, ConsultadoEm = When(p.At), Situacao = p.State switch { "operational" => "Sem incidentes publicados", "degraded" => "Incidente / degradação publicada", "manual" => "Consultar página oficial", _ => "Consulta indisponível" }, Descricao = p.Summary, ComponentesAfetados = string.Join("; ", p.Components), Incidentes = string.Join("; ", p.Incidents), AtualizacaoDaFonte = p.UpdatedAt.HasValue ? When(p.UpdatedAt.Value) : "Não informada" }).ToArray());

        }
        if (gameServers.Visible) StableView.SetRows(gameServers, s.GameServers.OrderByDescending(p => p.At).Select(p => new { Status = p.Success ? "✓ Respondeu" : "? Sem confirmação", Horario = When(p.At), Servidor = p.Name, Destino = p.Host + ":" + p.Port, Protocolo = p.Protocol, Resultado = p.Success ? "Resposta válida" : "Falha / inconclusivo", LatenciaMs = F(p.Ms), Informacoes = p.Details, Detalhe = p.Error }).ToArray());
        if (windowsLogs != null)
        {
            logs = windowsLogs; logChoice.Items.Clear(); logChoice.Items.AddRange(logs.Keys.Select(k => (object)new LogItem(k)).ToArray());
            if (logChoice.Items.Count > 0) logChoice.SelectedIndex = 0;
            else { windows.DataSource = null; logInfo.Text = "Este arquivo não contém eventos Windows."; }
        }
    }
    private void RenderSettings(Session session)
    {
        using var document = new RichTextBox { Font = networkSettings.Font, WordWrap = false };
        using var title = new Font("Segoe UI", 15, FontStyle.Bold);
        using var heading = new Font("Segoe UI", 11, FontStyle.Bold);
        using var body = new Font("Consolas", 10);
        void Add(string text, Font font, Color color)
        {
            var start = document.TextLength;
            document.AppendText(text);
            document.Select(start, document.TextLength - start);
            document.SelectionFont = font; document.SelectionColor = color;
            document.Select(document.TextLength, 0);
        }
        Add("Wi-Fi e configurações\n", title, Theme.Text);
        Add("Dados registrados no início do teste. Use estas informações com o suporte.\nNenhuma configuração foi alterada. Acentos de relatórios antigos dependem da captura original.\n\n", heading, Theme.Muted);
        foreach (var detail in session.Machine.Details)
        {
            Add((detail.Key switch { "wifi" => "01  /  CONEXÃO WI-FI", "proxyWinHttp" => "02  /  PROXY DO WINDOWS", "tcp" => "03  /  CONFIGURAÇÕES TCP", _ => detail.Key }) + "\n\n", heading, Theme.Orange);
            Add(WindowsSettingsText.Align(detail.Value) + "\n\n", body, Theme.Text);
        }
        StableView.SetDocument(networkSettings, document.Rtf);
    }
    private void RenderDiagnosis(Session s)
    {
        using var document = new RichTextBox { Font = findings.Font };
        using var title = new Font("Segoe UI", 18, FontStyle.Bold);
        using var heading = new Font("Segoe UI", 12, FontStyle.Bold);
        using var body = new Font("Segoe UI", 11);
        using var bold = new Font("Segoe UI", 11, FontStyle.Bold);
        void Add(string text, Font font, Color color)
        {
            document.SelectionStart = document.TextLength; document.SelectionLength = 0;
            document.SelectionFont = font; document.SelectionColor = color; document.AppendText(text);
        }
        var list = Diagnosis.Analyze(s).OrderBy(f => f.Severity switch { "Atenção" => 0, "Inconclusivo" => 1, _ => 2 }).ToArray();
        var alerts = list.Count(f => f.Severity == "Atenção");
        Add(alerts > 0 ? $"{alerts} ponto(s) de atenção na conexão\n" : "Seu diagnóstico de conexão\n", title, alerts > 0 ? Theme.Orange : Theme.Navy);
        Add("As conclusões abaixo se referem ao período e aos destinos testados.\n\n", body, Theme.Muted);
        Add("PRINCIPAIS MEDIÇÕES\n", heading, Theme.Navy);
        Add(ResourceSummary.Describe(s) + "\n", body, Theme.Navy);
        var measures = s.Speeds.Where(p => p.Mbps.HasValue).GroupBy(p => p.Direction).Select(g => $"{(g.Key == "download" ? "Download" : "Upload")}: média {g.Average(p => p.Mbps):F2} Mbps ({g.Count()} teste(s))");
        Add(string.Join("  •  ", measures.DefaultIfEmpty("Velocidade: nenhuma medição concluída")) + "\n", body, Theme.Navy);
        foreach (var group in s.Pings.Where(p => !p.UnderLoad).GroupBy(p => p.Target))
        {
            var p = Diagnosis.Stats(group.Key, group);
            Add($"{group.Key}: latência média {F(p.AverageMs)} ms; jitter {F(p.JitterMs)} ms; perda ICMP {p.LossPercent:F1}% ({p.Samples} tentativas)\n", body, Theme.Muted);
        }
        Add("\n", body, Theme.Text);
        foreach (var f in list)
        {
            var color = f.Severity == "Atenção" ? Theme.Bad : f.Severity == "Inconclusivo" ? Theme.Orange : Theme.Good;
            Add($"●  {f.Severity.ToUpperInvariant()}  |  {f.Title}\n", heading, color);
            Add("O que foi observado: ", bold, Theme.Navy); Add(f.Evidence + "\n", body, Theme.Text);
            Add("Possíveis causas: ", bold, Theme.Navy); Add(f.Cause + "\n", body, Theme.Text);
            Add("Ação recomendada: ", bold, Theme.Good); Add(f.Action + "\n\n", body, Theme.Text);
        }
        Add("COMO INTERPRETAR\n", heading, Theme.Navy);
        Add("Status dos jogos vem das fontes indicadas e pode ter atraso. A resposta de um servidor específico não representa todos os servidores do jogo. Eventos Windows precisam de correlação de horário; não provam causalidade. Nenhuma conclusão atribui automaticamente a falha ao provedor.\n", body, Theme.Muted);
        StableView.SetDocument(findings, document.Rtf);
    }
    private sealed record LogItem(string Key)
    {
        public override string ToString() => Key.Replace("windows/", "").Replace(".xml", "").Replace(".error.txt", " — indisponível").Replace("Microsoft-Windows-", "").Replace("_Operational", "");
    }
    private void ShowWindowsLog()
    {
        if (logChoice.SelectedItem is not LogItem item || !logs.TryGetValue(item.Key, out var text)) return;
        if (item.Key.EndsWith(".error.txt")) { windows.DataSource = null; logInfo.Text = "Canal indisponível: " + text; return; }
        try
        {
            var doc = WindowsEventXml.Parse(text);
            var ns = (XNamespace)"http://schemas.microsoft.com/win/2004/08/events/event";
            windows.DataSource = doc.Descendants(ns + "Event").Select(e =>
            {
                var system = e.Element(ns + "System");
                var date = system?.Element(ns + "TimeCreated")?.Attribute("SystemTime")?.Value;
                var data = e.Descendants(ns + "EventData").Elements().Concat(e.Descendants(ns + "UserData").Descendants().Where(n => !n.HasElements));
                return new { Horario = DateTimeOffset.TryParse(date, out var at) ? When(at) : date, Origem = system?.Element(ns + "Provider")?.Attribute("Name")?.Value, Codigo = system?.Element(ns + "EventID")?.Value, Gravidade = system?.Element(ns + "Level")?.Value switch { "1" => "Crítico", "2" => "Erro", "3" => "Aviso", _ => "Informação" }, Informacoes = e.Descendants(ns + "Message").FirstOrDefault()?.Value ?? string.Join("; ", data.Select(d => (d.Attribute("Name")?.Value ?? d.Name.LocalName) + ": " + d.Value)) };
            }).ToArray();
            logInfo.Text = windows.Rows.Count == 0 ? "Nenhum aviso ou erro retornado neste canal nas últimas 24h." : "Avisos e erros das últimas 24h; compare o horário com a falha. Dados técnicos dependem da origem.";
        }
        catch (Exception ex) { windows.DataSource = null; logInfo.Text = "Não foi possível interpretar os eventos: " + ex.Message; }
    }
}

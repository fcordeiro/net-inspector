using NetInspector.Desktop;
using NetInspector.Core;
namespace NetInspector.UITests;
internal static class Program
{
    private static string wifiOutput = "";
    [STAThread] private static void Main(string[] args)
    {
        try { wifiOutput = WindowsInventory.Command("netsh.exe", ["wlan", "show", "interfaces"], CancellationToken.None).GetAwaiter().GetResult(); }
        catch (IOException) { wifiOutput = "Wi-Fi indisponível neste Windows."; }
        ApplicationConfiguration.Initialize(); Theme.Initialize();
        Directory.CreateDirectory("test-results");
        if (args.Contains("--long-session")) { ReleaseUiChecks.LongSession(); return; }
        ReleaseUiChecks.Run();
        VerifyTools();
        MaintenanceUiChecks.Run();
        UpdateUiChecks.Run();
        Verify(new InspectorForm(), "inspector", false);
        if (args.Length > 0) Verify(new InspectorForm(Path.GetFullPath(args[0])), "report", true);
    }
    private static void VerifyTools()
    {
        foreach (var upgrade in new[] { false, true })
        {
            using var dialog = new ToolsDialog(upgrade, false);
            dialog.ShowInTaskbar = false; dialog.Opacity = 0; dialog.Show();
            var boxes = All(dialog).OfType<CheckBox>().ToArray();
            if (!upgrade && (boxes.Length != 10 || boxes.Where(b => b.Text.StartsWith("Cookies") || b.Text.StartsWith("Downloads") || b.Text.StartsWith("Windows.old")).Any(b => b.Checked))) throw new Exception("Seleção de limpeza incorreta");
            if (!upgrade && boxes.Any(b => b.Bottom > b.Parent!.ClientSize.Height && b.Parent is not ScrollableControl { AutoScroll: true })) throw new Exception("Opções de limpeza cortadas sem rolagem");
            using var bitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
            bitmap.Save($"test-results/tools-{(upgrade ? "upgrade" : "cleanup")}.png");
            dialog.Close();
        }
        foreach (var (dialog, name) in new (Form, string)[] { (new DiskDialog(), "disk"), (new DriverDialog(false), "drivers"), (new WorktreeDialog(false), "worktrees") })
        {
            using (dialog)
            {
                dialog.ShowInTaskbar = false; dialog.Opacity = 0; dialog.Show();
                if (dialog is DiskDialog && All(dialog).OfType<AllocationOrbit>().SingleOrDefault() is { } orbit)
                    orbit.Values = Enumerable.Range(0, 240).Select(i => (i % 24) / 23.0).ToArray();
                using var bitmap = new Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save($"test-results/tools-{name}.png");
                if (All(dialog).OfType<Button>().Any(b => b.Right > b.Parent!.ClientSize.Width || b.Bottom > b.Parent!.ClientSize.Height)) throw new Exception("Botões cortados: " + name);
                foreach (var row in All(dialog).OfType<ControlRow>())
                {
                    var centers = row.Controls.Cast<Control>().Select(c => c.Top + c.Height / 2).ToArray();
                    if (centers.Length > 0 && centers.Max() - centers.Min() > 1) throw new Exception("Seletores e botões desalinhados: " + name);
                }
                dialog.Close();
            }
        }
    }
    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var descendant in All(child)) yield return descendant; }
    }
    private static void VerifyFlowRows(FlowLayoutPanel panel, string name)
    {
        var row = new List<Control>();
        void VerifyRow()
        {
            var centers = row.Select(c => c.Top + c.Height / 2).ToArray();
            if (centers.Length > 0 && centers.Max() - centers.Min() > 1) throw new Exception("Controles desalinhados: " + name);
        }
        foreach (Control control in panel.Controls)
        {
            if (row.Count > 0 && control.Left <= row[^1].Left) { VerifyRow(); row.Clear(); }
            row.Add(control);
        }
        VerifyRow();
    }
    private static void Verify(Form form, string name, bool waitForData)
    {
        form.ShowInTaskbar = false;
        form.Opacity = 0;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        using var timer = new System.Windows.Forms.Timer { Interval = 200 };
        Exception? failure = null;
        timer.Tick += (_, _) =>
        {
            if (waitForData && !All(form).OfType<Label>().Any(g => g.Text.Contains("MB usados")) && elapsed.Elapsed.TotalSeconds < 20) return;
            timer.Stop();
            try
            {
                if (waitForData && !All(form).OfType<Label>().Any(g => g.Text.Contains("MB usados"))) throw new Exception("Relatório não apareceu na interface");
                var menu = All(form).OfType<MenuStrip>().Single();
                var tools = menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "Ferramentas");
                if (tools.DropDownItems.Count != 6 || !menu.Items.OfType<ToolStripMenuItem>().Any(i => i.Text == "Arquivo") || !menu.Items.OfType<ToolStripMenuItem>().Any(i => i.Text == "Ajuda")) throw new Exception("Menus incompletos");
                var tabs = All(form).OfType<TabControl>().Single();
                if (!All(form).OfType<Label>().Any(l => l.Text == NetInspector.Core.Branding.Copyright)) throw new Exception("Copyright ausente");
                if (!All(form).OfType<PictureBox>().Any(p => p.Image is { Width: > 0 })) throw new Exception("Logo ausente");
                if (tabs.TabPages.Cast<TabPage>().Any(p => p.Text.Contains("JSON"))) throw new Exception("JSON não deve aparecer na interface");
                if (waitForData && !All(form).OfType<RichTextBox>().Any(r => r.Text.Contains("PRINCIPAIS MEDIÇÕES") && r.Rtf?.Contains("colortbl") == true)) throw new Exception("Diagnóstico formatado ausente");
                if (!All(form).OfType<Button>().Any(b => b.Text.Contains("Iniciar") && b.Height >= 42 && b.Font.Bold)) throw new Exception("Botão iniciar sem destaque");
                var actions = All(form).OfType<Button>().Where(b => b.Parent is FlowLayoutPanel).ToArray();
                if (actions.Length != 5 || actions.Select(b => b.Size).Distinct().Count() != 1 || actions.Any(b => b.Right > b.Parent!.ClientSize.Width)) throw new Exception("Ações cortadas ou com tamanhos diferentes");
                VerifyFlowRows((FlowLayoutPanel)actions[0].Parent!, "Ações principais");
                var speed = All(form).OfType<ThemeCheckBox>().Single(c => c.Text.Contains("Medir velocidade"));
                if (speed.Width < speed.GetPreferredSize(Size.Empty).Width) throw new Exception("Texto da opção de velocidade sem espaço suficiente");
                var nextSetting = speed.Parent!.Controls.Cast<Control>().First(c => c.Left >= speed.Right && c != speed);
                if (speed.Bounds.IntersectsWith(nextSetting.Bounds)) throw new Exception("Opções de coleta sobrepostas");
                VerifyFlowRows((FlowLayoutPanel)speed.Parent, "Duração");
                var duration = All(form).OfType<DurationPicker>().Single();
                if (duration.Value != TestPolicy.DefaultMinutes || duration.Maximum != TestPolicy.MaximumMinutes) throw new Exception("Duração padrão/máxima incorreta");
                if (!menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "Arquivo").DropDownItems.OfType<ToolStripMenuItem>().Any(i => i.Text!.Contains("PDF"))) throw new Exception("Exportação PDF ausente");
                if (!menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "Ajuda").DropDownItems.OfType<ToolStripMenuItem>().Any(i => i.Text!.Contains("Verificar atualizações"))) throw new Exception("Verificação de atualizações ausente");
                speed.Focus(); speed.Checked = false; speed.Refresh(); speed.Checked = true; speed.Refresh();
                using (var focused = new Bitmap(speed.Parent.Width, speed.Parent.Height))
                {
                    speed.Parent.DrawToBitmap(focused, new Rectangle(Point.Empty, speed.Parent.Size));
                    focused.Save($"test-results/{name}-settings-focused.png");
                }
                var start = actions.Single(b => b.Text.Contains("Iniciar"));
                var stop = actions.Single(b => b.Text.Contains("Parar"));
                if (!start.Enabled || stop.Enabled) throw new Exception("Estado inicial incorreto");
                var status = All(form).OfType<Label>().First(l => l.Dock == DockStyle.Bottom && l.Height == 32);
                var originalStatus = status.Text;
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var dashboard = All(form).OfType<Dashboard>().Single();
                if (All(form).OfType<PowerMeter>().Count() != 1) throw new Exception("Gráfico de bateria/energia ausente");
                if (All(form).OfType<ResourceMeter>().Count() != 2) throw new Exception("Gráficos de CPU e memória ausentes");
                var originalLogs = new Dictionary<string, string>((Dictionary<string, string>)typeof(Dashboard).GetField("logs", flags)!.GetValue(dashboard)!);
                var originalSession = (Session?)typeof(Dashboard).GetField("current", flags)!.GetValue(dashboard);
                var liveSession = new Session { RequestedMinutes = 1, StartedAt = DateTimeOffset.UtcNow.AddSeconds(-30), Resources = [new(DateTimeOffset.UtcNow, 35, 62, 10_000_000_000, 16_000_000_000, Power: new(true, true, 73, true, null))] };
                Session SetState(string state) { liveSession.State = state; liveSession.EndedAt = DateTimeOffset.UtcNow; return liveSession; }
                dashboard.ShowSession(liveSession, live: true);
                var progress = All(form).OfType<ProgressBar>().Single();
                var eta = All(form).OfType<Label>().Single(l => l.Text.Contains("ETA restante"));
                if (progress.Value < 490 || progress.Value > 550 || !eta.Text.Contains("00:00:")) throw new Exception("Progresso ou ETA incorreto");
                if (!All(form).OfType<RichTextBox>().Any(r => r.Text.Contains("CPU: média") && r.Text.Contains("Memória física: média"))) throw new Exception("Resumo de recursos ausente");
                var running = form.GetType().GetField("running", flags)!;
                var busy = form.GetType().GetMethod("SetBusy", flags)!;
                running.SetValue(form, true); busy.Invoke(form, [true]);
                if (start.Enabled || !start.Text.Contains("execução") || !stop.Enabled) throw new Exception("Estado de execução incorreto");
                using (var activeImage = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(activeImage, new Rectangle(Point.Empty, form.Size)); activeImage.Save($"test-results/{name}-running.png"); }
                stop.PerformClick();
                if (stop.Enabled || !stop.Text.Contains("Salvando") || !start.Text.Contains("Finalizando")) throw new Exception("Estado de finalização incorreto");
                if (progress.Style != ProgressBarStyle.Marquee || !eta.Text.Contains("Finalizando")) throw new Exception("Finalização sem estado separado");
                dashboard.ShowSession(liveSession, live: false);
                if (!eta.Text.Contains("Checkpoint parcial") || eta.Text.Contains("ETA restante")) throw new Exception("Checkpoint apresenta ETA de coleta ativa");
                dashboard.ShowSession(SetState("stopped"), live: false);
                if (!eta.Text.Contains("encerrado pelo usuário") || progress.Value >= 1000) throw new Exception("Cancelamento apresentado como concluído");
                dashboard.ShowSession(SetState("completed"), live: false);
                if (progress.Value != 1000 || !eta.Text.Contains("concluído")) throw new Exception("Conclusão não indica 100%");
                dashboard.ShowSession(originalSession ?? liveSession, live: false);
                running.SetValue(form, false); busy.Invoke(form, [false]); status.Text = originalStatus;
                foreach (var index in new[] { 2, 3, 4, 5 })
                {
                    tabs.SelectedIndex = index;
                    var data = All(tabs.SelectedTab!).OfType<DataGridView>().First();
                    if (waitForData && !data.Columns.Contains("Status")) throw new Exception("Indicador por linha ausente");
                }
                tabs.SelectedIndex = 9;
                var adapterGrid = All(tabs.SelectedTab!).OfType<DataGridView>().Single();
                if (waitForData && adapterGrid.Rows.Count > 1) throw new Exception("Adaptadores secundários visíveis por padrão");
                Console.WriteLine($"Viewport {form.ClientSize.Width}x{form.ClientSize.Height}; conteúdo {tabs.Width}x{tabs.Height}");
                if (tabs.TabPages.Count < 10 || tabs.Height <= 0 || tabs.Width <= 0 || tabs.Bottom > tabs.Parent!.ClientSize.Height) throw new Exception("Conteúdo fora da área disponível");
                tabs.SelectedIndex = 13;
                var wifiSession = new Session();
                wifiSession.Machine.Details["wifi"] = wifiOutput;
                wifiSession.Machine.Details["proxyWinHttp"] = "    Proxy do Windows    : Acesso direto\n    Exceções  : Nenhuma";
                wifiSession.Machine.Details["tcp"] = "    Estado de recepção    : habilitado\n    Ajuste automático : normal";
                dashboard.ShowSession(wifiSession);
                var settings = All(tabs.SelectedTab!).OfType<RichTextBox>().Single();
                if (settings.WordWrap || settings.ScrollBars != RichTextBoxScrollBars.Both) throw new Exception("Configurações sem rolagem horizontal");
                foreach (var term in new[] { "Wi-Fi", "Acesso direto", "habilitado" })
                {
                    var position = settings.Text.IndexOf(term, settings.Text.IndexOf("01  /", StringComparison.Ordinal), StringComparison.Ordinal);
                    if (position < 0) throw new Exception("Texto de configuração ausente: " + term);
                    settings.Select(position, term.Length);
                    if (settings.SelectionFont?.Name != "Consolas") throw new Exception("Fonte monoespaçada não aplicada: " + term);
                }
                settings.Select(0, 0); settings.ScrollToCaret();
                using (var wifiImage = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(wifiImage, new Rectangle(Point.Empty, form.Size)); wifiImage.Save($"test-results/{name}-wifi-corrected.png"); }
                tabs.SelectedIndex = 2;
                var loaded = new Session { Pings = Enumerable.Range(0, 15).Select(i => new PingSample(DateTimeOffset.UtcNow.AddSeconds(i), "Teste", "1.1.1.1", true, 10, "Success", true)).ToList() };
                dashboard.ShowSession(loaded);
                var quality = All(tabs.SelectedTab!).OfType<DataGridView>().Single();
                if (quality.Rows[0].Cells["Status"].Value?.ToString()?.Contains("Durante teste de velocidade") != true) throw new Exception("Status sob carga ainda ambíguo");
                tabs.SelectedIndex = 12;
                dashboard.ShowSession(liveSession, new() { ["System.xml"] = "\uFEFF<Events><Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>42</EventID><Level>3</Level></System><RenderingInfo><Message>Evento de teste</Message></RenderingInfo></Event></Events>" });
                var log = All(tabs.SelectedTab!).OfType<DataGridView>().Single();
                if (log.Rows.Count != 1 || log.Rows[0].Cells["Informacoes"].Value?.ToString() != "Evento de teste") throw new Exception("Evento Windows com BOM não aparece");
                using (var logImage = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(logImage, new Rectangle(Point.Empty, form.Size)); logImage.Save($"test-results/{name}-windows-fixed.png"); }
                dashboard.ShowSession(originalSession ?? liveSession, originalLogs);
                foreach (var (index, label) in new[] { (0, "diagnosis"), (1, "overview"), (2, "quality"), (8, "history"), (13, "wifi"), (3, "services"), (4, "game-status"), (5, "game-servers"), (9, "connection"), (12, "windows") })
                {
                    tabs.SelectedIndex = index;
                    form.Refresh();
                    using var image = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                    image.Save($"test-results/{name}-{label}.png");
                }
                Console.WriteLine($"OK: {name}, {tabs.TabPages.Count} abas, gráficos e dados renderizados");
            }
            catch (Exception ex) { failure = ex; }
            form.Close();
        };
        form.Shown += (_, _) => timer.Start();
        Application.Run(form);
        form.Dispose();
        if (failure != null) throw failure;
    }
}

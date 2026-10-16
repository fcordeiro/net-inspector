using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using NetInspector.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Color = MigraDoc.DocumentObjectModel.Color;

namespace NetInspector.Desktop;

public static class PdfReport
{
    private static readonly Color Blue = Color.FromRgb(0, 111, 204), Ink = Color.FromRgb(17, 32, 52), Gray = Color.FromRgb(83, 103, 123);
    public static void Write(string path, Report report)
    {
        var s = report.Session;
        var doc = new Document(); doc.Info.Title = "Net Inspector - Relatório completo"; doc.Info.Author = "CONNECT10 INTERNET";
        var normal = doc.Styles[StyleNames.Normal]!; normal.Font.Name = "Segoe UI"; normal.Font.Size = 9; normal.Font.Color = Ink; normal.ParagraphFormat.SpaceAfter = 5;
        var heading = doc.Styles[StyleNames.Heading1]!; heading.Font.Name = "Segoe UI"; heading.Font.Size = 22; heading.Font.Color = Blue; heading.ParagraphFormat.SpaceAfter = 14;
        var sub = doc.Styles[StyleNames.Heading2]!; sub.Font.Size = 12; sub.Font.Color = Blue; sub.ParagraphFormat.SpaceBefore = 12; sub.ParagraphFormat.SpaceAfter = 7;
        Section NewSection(string title)
        {
            var section = doc.AddSection(); section.PageSetup.PageFormat = PageFormat.A4;
            section.PageSetup.TopMargin = Unit.FromCentimeter(2); section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);
            section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.7);
            var header = section.Headers.Primary.AddParagraph("CONNECT10   /   NET INSPECTOR"); header.Format.Font.Size = 8; header.Format.Font.Color = Gray;
            var footer = section.Footers.Primary.AddParagraph(); footer.Format.Font.Size = 7; footer.Format.Font.Color = Gray;
            footer.AddText(Branding.Copyright + "   |   "); footer.AddPageField(); footer.AddText(" / "); footer.AddNumPagesField();
            section.AddParagraph(title, StyleNames.Heading1); return section;
        }
        void Text(Section section, string text) => section.AddParagraph(Clean(text));
        void Table(Section section, string[] headers, IEnumerable<string[]> rows)
        {
            var table = section.AddTable(); table.Borders.Width = 0.3; table.Borders.Color = Color.FromRgb(216, 225, 234);
            table.LeftPadding = table.RightPadding = 5; table.TopPadding = table.BottomPadding = 4;
            foreach (var _ in headers) table.AddColumn(Unit.FromCentimeter(17.6 / headers.Length));
            var head = table.AddRow(); head.HeadingFormat = true; head.Shading.Color = Ink; head.Format.Font.Color = Colors.White; head.Format.Font.Bold = true;
            for (var i = 0; i < headers.Length; i++) head.Cells[i].AddParagraph(headers[i]);
            var count = 0;
            foreach (var data in rows)
            {
                // A table row cannot split across pages; long messages get explicit continuation rows.
                var parts = data.Select(cell => cell ?? "").Select(cell => Enumerable.Range(0, Math.Max(1, (cell.Length + 599) / 600)).Select(i => cell.Substring(i * 600, Math.Min(600, cell.Length - i * 600))).ToArray()).ToArray();
                for (var part = 0; part < parts.Max(c => c.Length); part++)
                {
                    var row = table.AddRow(); row.Format.Font.Size = 8;
                    if (count % 2 == 0) row.Shading.Color = Color.FromRgb(239, 246, 252);
                    for (var i = 0; i < headers.Length; i++) row.Cells[i].AddParagraph(Clean(i < parts.Length && part < parts[i].Length ? parts[i][part] : ""));
                }
                count++;
            }
            if (count == 0) { var row = table.AddRow(); row.Cells[0].MergeRight = headers.Length - 1; row.Cells[0].AddParagraph("Não medido / sem registros nesta coleta."); }
            section.AddParagraph();
        }
        var cover = NewSection("Diagnóstico completo\nda conexão e do computador");
        using (var stream = typeof(Theme).Assembly.GetManifestResourceStream("NetInspector.Desktop.Assets.logo-connect10.png")!)
        using (var buffer = new MemoryStream()) { stream.CopyTo(buffer); var logo = cover.AddImage("base64:" + Convert.ToBase64String(buffer.ToArray())); logo.Width = Unit.FromCentimeter(5); }
        cover.AddParagraph("EVIDÊNCIAS  /  DESEMPENHO  /  CONFIGURAÇÕES", StyleNames.Heading2);
        Table(cover, ["Sessão", "Informação"], new[] {
            new[] { "Início", When(s.StartedAt) }, new[] { "Fim", s.EndedAt is { } end ? When(end) : "Checkpoint parcial" },
            new[] { "Duração solicitada / estado", $"{s.RequestedMinutes} minutos / {s.State}" }, new[] { "Versão / identificação", $"{s.AppVersion} / {s.Id}" },
            new[] { "Velocidade / orçamento", $"{(s.SpeedEnabled ? "Ativada" : "Desativada")} / {s.SpeedBudgetBytes / 1e9:F1} GB" },
            new[] { "Consumo reservado dos testes", $"{s.Speeds.Sum(p => p.BudgetBytes) / 1e9:F3} GB" } });
        Text(cover, ResourceSummary.Describe(s));
        Text(cover, "Este relatório apresenta observações e hipóteses. Eventos Windows não provam a causa de falhas de conexão. Horários usam o fuso local " + TimeZoneInfo.Local.DisplayName + ". Dados não coletados são identificados; ausência de medição não significa zero.");
        Table(cover, ["Categorias", "Conteúdo"], new[] { new[] { "01 - Diagnóstico", "Achados, evidências e ações recomendadas" }, new[] { "02 - Desempenho", "Gráficos de velocidade, latência, CPU e memória" }, new[] { "03 - Computador e rede", "Inventário, adaptadores, extensões e configurações" }, new[] { "04 - Windows", "Todos os eventos coletados, erros e limitações" }, new[] { "05 - Testes", "Histórico integral das medições e eventos do aplicativo" } });
        var diagnosis = NewSection("01 / Diagnóstico e recomendações");
        foreach (var finding in Diagnosis.Analyze(s))
        {
            var p = diagnosis.AddParagraph($"[{finding.Severity}] {finding.Title}", StyleNames.Heading2);
            if (finding.Severity == "Atenção") p.Format.Font.Color = Color.FromRgb(196, 81, 24);
            Text(diagnosis, "Observado: " + finding.Evidence); Text(diagnosis, "Possíveis causas: " + finding.Cause); Text(diagnosis, "Ação recomendada: " + finding.Action);
        }
        var charts = NewSection("02 / Desempenho ao longo do tempo");
        AddChart(charts, "CPU e memória física (%)", [("CPU", s.Resources.Select(r => (r.At, r.CpuPercent)), Theme.Blue), ("Memória", s.Resources.Select(r => (r.At, r.MemoryPercent)), Theme.Orange)], 100);
        AddChart(charts, "Velocidade (Mbps)", [("Download", s.Speeds.Where(r => r.Direction == "download").Select(r => (r.At, r.Mbps)), Theme.Blue), ("Upload", s.Speeds.Where(r => r.Direction == "upload").Select(r => (r.At, r.Mbps)), Theme.Orange)]);
        AddChart(charts, "Latência por destino (ms)", s.Pings.GroupBy(p => p.Target).Select((g, i) => (g.Key, g.Select(r => (r.At, r.Ms)), new[] { Theme.Blue, Theme.Orange, System.Drawing.Color.SeaGreen, System.Drawing.Color.MediumPurple }[i % 4])).ToArray());
        Text(charts, "Pontos representam amostras disponíveis. Intervalos sem valor interrompem a linha. ICMP durante testes de velocidade pode refletir a carga gerada pela própria medição.");
        var inventory = NewSection("03 / Computador, rede e configurações");
        Table(inventory, ["Sistema", "Valor"], [ ["Windows", s.Machine.Os], ["Navegador padrão", s.Machine.DefaultBrowser], ["Gateway inicial", s.Machine.Gateway ?? "Não identificado"], ["Adaptador principal", s.Machine.PrimaryAdapterId ?? "Não identificado"] ]);
        foreach (var adapter in s.Machine.Adapters)
        {
            inventory.AddParagraph(adapter.Name, StyleNames.Heading2);
            Text(inventory, $"{adapter.Id}\n{adapter.Type} / {adapter.Status} / link {adapter.LinkBps / 1e6:F0} Mbps\nIPs: {string.Join(", ", adapter.Addresses)}\nGateways: {string.Join(", ", adapter.Gateways)}\nDNS: {string.Join(", ", adapter.Dns)}");
        }
        Table(inventory, ["Navegador / perfil", "Extensão / versão", "Estado / ID"], s.Machine.Extensions.Select(e => new[] { e.Browser + " / " + e.Profile, e.Name + " / " + e.Version, e.State + " / " + e.Id }));
        foreach (var detail in s.Machine.Details) { inventory.AddParagraph(detail.Key, StyleNames.Heading2); Text(inventory, detail.Value); }
        inventory.AddParagraph("Limitações da coleta", StyleNames.Heading2);
        foreach (var limitation in s.Machine.Limitations) Text(inventory, limitation);
        var windows = NewSection("04 / Eventos e erros do Windows");
        Text(windows, "Janela de coleta: últimas 24h na finalização, até 400 avisos/erros por canal. Acessos negados e canais indisponíveis são preservados abaixo. Checkpoints e relatórios antigos podem não conter eventos.");
        if (report.WindowsLogs.Count == 0) Text(windows, "Eventos Windows não disponíveis neste relatório.");
        foreach (var log in report.WindowsLogs)
        {
            windows.AddParagraph(log.Key, StyleNames.Heading2);
            if (!log.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) { Text(windows, log.Value); continue; }
            try
            {
                var root = WindowsEventXml.Parse(log.Value);
                var events = root.Descendants().Where(e => e.Name.LocalName == "Event").ToArray();
                if (events.Length == 0) Text(windows, "Nenhum aviso ou erro retornado.");
                foreach (var ev in events)
                {
                    var system = ev.Elements().FirstOrDefault(e => e.Name.LocalName == "System");
                    string Field(string name) => system?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? "";
                    var at = system?.Elements().FirstOrDefault(e => e.Name.LocalName == "TimeCreated")?.Attribute("SystemTime")?.Value;
                    var provider = system?.Elements().FirstOrDefault(e => e.Name.LocalName == "Provider")?.Attribute("Name")?.Value;
                    windows.AddParagraph($"{(DateTimeOffset.TryParse(at, out var time) ? When(time) : at)} / {provider} / ID {Field("EventID")} / nível {Field("Level")}", StyleNames.Heading2);
                    var message = ev.Descendants().FirstOrDefault(e => e.Name.LocalName == "Message")?.Value;
                    if (!string.IsNullOrWhiteSpace(message)) Text(windows, message);
                    foreach (var data in ev.Descendants().Where(e => e.Name.LocalName == "EventData" || e.Name.LocalName == "UserData"))
                        foreach (var item in data.Descendants().Where(e => !e.HasElements)) Text(windows, (item.Attribute("Name")?.Value ?? item.Name.LocalName) + ": " + item.Value);
                }
            }
            catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException) { Text(windows, "Não foi possível interpretar este canal: " + ex.Message); }
        }
        var tests = NewSection("05 / Histórico integral dos testes");
        Text(tests, "Todas as amostras preservadas nesta sessão estão nas tabelas abaixo. O anexo session.json dentro do PDF preserva os dados estruturados originais. O pacote .netinspect permanece o formato para reabrir no aplicativo.");
        tests.AddParagraph("ICMP", StyleNames.Heading2);
        Table(tests, ["Horário / destino", "RTT / resultado", "Endereço / carga"], s.Pings.Select(p => new[] { When(p.At) + " / " + p.Target, N(p.Ms) + " ms / " + p.Status, p.Address + " / " + (p.UnderLoad ? "sob carga" : "sem carga") }));
        tests.AddParagraph("Velocidade", StyleNames.Heading2);
        Table(tests, ["Horário / direção", "Mbps / bytes", "Duração / erro"], s.Speeds.Select(p => new[] { When(p.At) + " / " + p.Direction, N(p.Mbps) + " / " + p.Bytes, $"{p.Seconds:F2}s / {p.Error ?? "sem erro"}; reserva {p.BudgetBytes}; {p.Streams} fluxos; janela {p.MeasurementSeconds:F2}s" }));
        tests.AddParagraph("Sites e streaming", StyleNames.Heading2);
        Table(tests, ["Horário / serviço", "DNS / TCP / HTTPS (ms)", "HTTP / resultado"], s.Services.Select(p => new[] { When(p.At) + " / " + p.Name + " / " + p.Host + " / " + p.Category, $"{N(p.DnsMs)} / {N(p.TcpMs)} / {N(p.HttpsMs)}", $"{p.HttpStatus} / {p.Outcome} / {p.Error}" }));
        tests.AddParagraph("Servidores de jogos configurados", StyleNames.Heading2);
        Table(tests, ["Nome", "Destino", "Protocolo"], s.ConfiguredGameServers.Select(p => new[] { p.Name, p.Host + ":" + p.Port, p.Protocol }));
        tests.AddParagraph("Resultados dos servidores", StyleNames.Heading2);
        Table(tests, ["Horário / destino", "Protocolo / RTT", "Resultado"], s.GameServers.Select(p => new[] { When(p.At) + " / " + p.Name + " / " + p.Host + ":" + p.Port, p.Protocol + " / " + N(p.Ms), $"{(p.Success ? "Respondeu" : "Sem resposta")}: {p.Details} / {p.Error}" }));
        tests.AddParagraph("Status publicado pelos jogos", StyleNames.Heading2);
        foreach (var p in s.ProviderStatus) Text(tests, $"{When(p.At)} / {p.Provider} / {p.State}\n{p.Summary}\nComponentes: {string.Join("; ", p.Components)}\nIncidentes: {string.Join("; ", p.Incidents)}\nFonte: {p.Page} / atualização: {(p.UpdatedAt is { } updated ? When(updated) : "não informada")}");
        tests.AddParagraph("CPU, memória e energia", StyleNames.Heading2);
        Table(tests, ["Horário", "CPU / memória (%)", "Bytes usados / total; energia"], s.Resources.Select(p => new[] { When(p.At), N(p.CpuPercent) + " / " + N(p.MemoryPercent), $"{p.UsedMemoryBytes} / {p.TotalMemoryBytes}; AC={p.Power?.OnAcPower}; bateria={p.Power?.HasBattery}; carga={p.Power?.Percent}%; carregando={p.Power?.Charging}; restante={p.Power?.RemainingSeconds}s; {p.Error}" }));
        tests.AddParagraph("Histórico dos adaptadores", StyleNames.Heading2);
        Table(tests, ["Horário / adaptador", "Link / tráfego (bytes)", "Erros RX / TX; rota"], s.Network.Select(p => new[] { When(p.At) + " / " + p.Adapter + " / " + p.Type + " / " + p.Status, $"{p.LinkBps / 1e6:F0} Mbps / {p.ReceivedBytes} RX / {p.SentBytes} TX", $"{p.ReceiveErrors} / {p.SendErrors}; principal={p.IsPrimary}" }));
        tests.AddParagraph("Eventos do aplicativo", StyleNames.Heading2);
        Table(tests, ["Horário", "Nível", "Mensagem"], s.Events.Select(p => new[] { When(p.At), p.Level, p.Message }));
        var renderer = new PdfDocumentRenderer { Document = doc }; renderer.RenderDocument();
        renderer.PdfDocument.AddEmbeddedFile("session.json", new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(s, Session.Json)));
        foreach (var log in report.WindowsLogs) renderer.PdfDocument.AddEmbeddedFile(log.Key, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(log.Value)));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { renderer.PdfDocument.Save(temporary); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); renderer.PdfDocument.Dispose(); }
    }
    private static string N(double? number) => number?.ToString("F2", CultureInfo.CurrentCulture) ?? "não medido";
    private static string When(DateTimeOffset time) => time.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
    private static string Clean(string? value)
    {
        // Break long identifiers so table cells cannot overflow the printable width.
        var text = value ?? "Não informado";
        return System.Text.RegularExpressions.Regex.Replace(text.Replace("\r", ""), @"[^\s]{25,}", match => string.Join("\u200B", Enumerable.Range(0, (match.Length + 23) / 24).Select(i => match.Value.Substring(i * 24, Math.Min(24, match.Length - i * 24)))));
    }
    private static void AddChart(Section section, string title, (string Name, IEnumerable<(DateTimeOffset At, double? Value)> Data, System.Drawing.Color Color)[] series, double? ceiling = null)
    {
        section.AddParagraph(title, StyleNames.Heading2);
        var values = series.Select(s => (s.Name, Points: s.Data.OrderBy(p => p.At).ToArray(), s.Color)).ToArray();
        var all = values.SelectMany(v => v.Points).Where(p => p.Value.HasValue && double.IsFinite(p.Value.Value)).ToArray();
        if (all.Length == 0) { section.AddParagraph("Não medido nesta sessão."); return; }
        // Vector chart: sharp at any zoom, no screenshot or external plotting dependency.
        using var chart = new PdfDocument(); var page = chart.AddPage(); page.Width = XUnit.FromPoint(500); page.Height = XUnit.FromPoint(170);
        using (var g = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Segoe UI", 8); var max = ceiling ?? Math.Max(1, all.Max(p => p.Value!.Value) * 1.1);
            var start = all.Min(p => p.At); var seconds = Math.Max(1, (all.Max(p => p.At) - start).TotalSeconds);
            for (var i = 0; i <= 4; i++) { var y = 125 - i * 26; g.DrawLine(new XPen(XColors.LightGray, 0.4), 36, y, 490, y); g.DrawString((max * i / 4).ToString("0.#"), font, XBrushes.SlateGray, 0, y + 3); }
            var legend = 38.0;
            foreach (var seriesItem in values)
            {
                var color = XColor.FromArgb(seriesItem.Color.ToArgb()); var pen = new XPen(color, 1.3); XPoint? previous = null;
                foreach (var point in seriesItem.Points)
                {
                    if (point.Value is not { } value || !double.IsFinite(value)) { previous = null; continue; }
                    var next = new XPoint(36 + (point.At - start).TotalSeconds / seconds * 454, 125 - Math.Clamp(value / max, 0, 1) * 104);
                    if (previous is { } last) g.DrawLine(pen, last, next);
                    g.DrawEllipse(new XSolidBrush(color), next.X - 1.2, next.Y - 1.2, 2.4, 2.4); previous = next;
                }
                g.DrawString(seriesItem.Name, font, new XSolidBrush(color), legend, 163); legend += 112;
            }
            g.DrawString(start.ToLocalTime().ToString("HH:mm:ss"), font, XBrushes.SlateGray, 36, 141);
            g.DrawString(all.Max(p => p.At).ToLocalTime().ToString("HH:mm:ss"), font, XBrushes.SlateGray, 450, 141);
        }
        using var stream = new MemoryStream(); chart.Save(stream, false);
        var image = section.AddImage("base64:" + Convert.ToBase64String(stream.ToArray())); image.Width = Unit.FromCentimeter(17.6);
    }
}

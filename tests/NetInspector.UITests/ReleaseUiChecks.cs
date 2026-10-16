using NetInspector.Core;
using NetInspector.Desktop;

namespace NetInspector.UITests;

internal static class ReleaseUiChecks
{
    public static void LongSession()
    {
        using var monitor = new NetInspector.Core.Monitor(120, true, 20_000_000_000);
        var s = monitor.Data; var at = s.StartedAt;
        s.State = "completed"; s.EndedAt = at.AddMinutes(120);
        for (var i = 0; i < 3600; i++)
        {
            var time = at.AddSeconds(i * 2);
            s.Resources.Add(new(time, i % 95, 60, 9_600_000_000, 16_000_000_000));
            foreach (var target in new[] { "Gateway", "Cloudflare", "Google", "Quad9" }) s.Pings.Add(new(time, target, "1.1.1.1", true, 10 + i % 20, "Success", false));
        }
        for (var i = 0; i < 720; i++) s.Network.Add(new(at.AddSeconds(i * 10), "Ethernet", "Ethernet", "Up", 1_000_000_000, i * 1_000_000L, i * 500_000L, 0, 0, true));
        for (var i = 0; i < 120; i++) foreach (var service in Catalog.Services) s.Services.Add(new(at.AddMinutes(i), service.Name, service.Host, service.Category, 1, 2, 3, 200, "ok", null));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 10; i++) _ = monitor.Snapshot();
        Console.WriteLine($"Volume sintético de 120min: {s.Pings.Count} pings; {s.Resources.Count} amostras de recursos; snapshot médio {clock.Elapsed.TotalMilliseconds / 10:F1}ms");
        monitor.SaveCheckpoint("test-results/long-session.json").GetAwaiter().GetResult();
        Console.WriteLine($"Checkpoint: {new FileInfo("test-results/long-session.json").Length:N0} bytes");
        clock.Restart(); PdfReport.Write("test-results/long-session.pdf", new(s, []));
        Console.WriteLine($"PDF integral de 120min: {clock.Elapsed.TotalSeconds:F2}s; {new FileInfo("test-results/long-session.pdf").Length:N0} bytes");
    }
    public static void Run()
    {
        var at = DateTimeOffset.UtcNow;
        var s = new Session { RequestedMinutes = 15, State = "completed", StartedAt = at.AddMinutes(-15), EndedAt = at, SpeedEnabled = true, SpeedBudgetBytes = 20_000_000_000 };
        s.Machine.Details["CPU"] = "Cenário sintético de validação visual; não representa diagnóstico de uma máquina real.";
        s.Machine.Details["wifi"] = "Descrição: conexão de teste\nDNS: 1.1.1.1\nIPv6: fe80::1234\n" + new string('a', 250);
        s.Machine.Limitations.Add("Canal de teste inacessível: exemplo de limitação.");
        s.Machine.Extensions.Add(new("Edge", "Default", new string('f', 32), "Extensão de teste", "1.0", "Ativa"));
        for (var i = 0; i < 40; i++)
        {
            var time = at.AddSeconds(i * 2 - 900);
            s.Resources.Add(new(time, i == 15 ? null : 20 + i, 48 + i / 3, 8_000_000_000, 16_000_000_000, Power: new(true, false, null, null, null)));
            s.Pings.Add(new(time, "Cloudflare", "1.1.1.1", i != 20, i == 20 ? null : 10 + i % 5, i == 20 ? "TimedOut" : "Success", false));
        }
        s.Speeds.Add(new(at, "download", 450, 150_000_000, 8, null, 150_000_000, 4, 7));
        s.Speeds.Add(new(at.AddSeconds(8), "upload", 200, 150_000_000, 8, null, 150_000_000, 4, 7));
        s.Services.Add(new(at, "Netflix", "netflix.com", "Streaming", 4, 12, 70, 200, "ok", null));
        s.GameServers.Add(new(at, "Exemplo", "example.com", 25565, "Minecraft", false, null, "Não respondeu", "timeout"));
        s.Events.Add(new(at, "info", "Fim da coleta sintética de validação."));
        var logs = new Dictionary<string, string> { ["System.xml"] = "<Events><Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\"><System><Provider Name=\"Teste\"/><EventID>1001</EventID><Level>2</Level><TimeCreated SystemTime=\"2026-10-16T12:00:00Z\"/></System><RenderingInfo><Message>Mensagem de erro sintética, com acentos: conexão e memória.</Message></RenderingInfo><EventData><Data Name=\"Detalhes\">Evidência de teste</Data></EventData></Event></Events>", ["DNS.error.txt"] = "Acesso negado (cenário sintético)." };
        PdfReport.Write("test-results/relatorio-validacao.pdf", new(s, logs));
        PdfReport.Write("test-results/relatorio-vazio.pdf", new(new Session { AppVersion = "1.0.0" }, []));
        ReportArchive.Write("test-results/relatorio-validacao.netinspect", s, logs);
        Console.WriteLine("PDFs com dados, gráficos, campos longos, canal indisponível e relatório legado gerados.");
    }
}

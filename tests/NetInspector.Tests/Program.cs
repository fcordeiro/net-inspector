using System.IO.Compression;
using System.Text.Json;
using NetInspector.Core;
using NetMonitor = NetInspector.Core.Monitor;

var count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FALHOU: " + name); Console.WriteLine("OK: " + name); count++; }
void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { Check(true, name); return; } throw new Exception("FALHOU: " + name); }
var now = DateTimeOffset.UtcNow;
Check(TestPolicy.DefaultMinutes == 15 && TestPolicy.MaximumMinutes == 120, "política de duração padrão e máxima");
using (var snapshotMonitor = new NetMonitor(999, false, 0))
{
    snapshotMonitor.Data.Machine.Adapters.Add(new("a", "LAN", "Ethernet", "Up", 1000, ["1.1.1.1"], [], ["8.8.8.8"]));
    snapshotMonitor.Data.Pings.Add(new(now, "Test", "1.1.1.1", true, 1, "Success", false));
    var copy = snapshotMonitor.Snapshot(); copy.Pings.Clear(); copy.Machine.Adapters[0].Dns[0] = "changed";
    Check(snapshotMonitor.Data.RequestedMinutes == 120 && snapshotMonitor.Data.Pings.Count == 1 && snapshotMonitor.Data.Machine.Adapters[0].Dns[0] == "8.8.8.8", "snapshot eficiente preserva isolamento e limite da coleta");
}
Check(StorageMedia.Classify(11, true, false).CanOverwrite, "HDD SATA físico permite sobrescrita");
Check(StorageMedia.Classify(17, false, true).Kind.StartsWith("SSD"), "SSD NVMe detectado");
Check(!StorageMedia.Classify(17, true, true).CanOverwrite, "NVMe com evidência contraditória bloqueado");
foreach (var bus in new int?[] { null, 8, 14, 15, 16, 9, 7 }) Check(!StorageMedia.Classify(bus, true, true).CanOverwrite, "mídia composta/virtual/desconhecida bloqueada: " + bus);
Check(!StorageMedia.Classify(11, null, null).CanOverwrite, "falta de identificação não assume HDD");
Check(ReleaseUpdates.IsNewer(new Version(1, 6, 0), new Version(1, 5, 0, 0)) && !ReleaseUpdates.IsNewer(new Version(1, 5, 0), new Version(1, 5, 0, 0)), "comparação de versões normaliza revisão");
var releaseJson = """{"draft":false,"prerelease":false,"tag_name":"v1.6.0","html_url":"https://github.com/fcordeiro/net-inspector/releases/tag/v1.6.0","assets":[{"name":"NetInspector-win-x64.exe","size":100,"state":"uploaded","browser_download_url":"https://github.com/fcordeiro/net-inspector/releases/download/v1.6.0/NetInspector-win-x64.exe"}]}""";
Check(ReleaseUpdates.Parse(releaseJson, "NetInspector-win-x64.exe")?.Version == new Version(1,6,0), "release estável com executável compatível");
Check(ReleaseUpdates.Parse(releaseJson.Replace("\"prerelease\":false", "\"prerelease\":true"), "NetInspector-win-x64.exe") == null, "prerelease ignorada");
Reject(() => ReleaseUpdates.Parse(releaseJson.Replace("https://github.com/", "https://evil.example/"), "NetInspector-win-x64.exe"), "URL externa de atualização rejeitada");
Reject(() => ReleaseUpdates.Parse(releaseJson, "NetInspector-win-arm64.exe"), "release sem arquitetura compatível não oferece download");
var wifiText = "Há 1 interface no sistema:\r\n\r\n    Nome                   : Wi-Fi\r\n    Descrição            : Intel(R) Wireless-AC 9462\r\n    Endereço físico       : 02:00:00:00:00:01\r\n    Tipo de interface         : Primário\r\n    Status de rádio           : hardware Ativado\r\n                             software Ativado\r\n";
Check(WindowsInventory.DecodeCommandOutput(System.Text.Encoding.UTF8.GetBytes(wifiText)) == wifiText, "UTF-8 do netsh preserva Há, Descrição, físico, Primário e rádio");
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
var oem = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
Check(WindowsInventory.DecodeCommandOutput(oem.GetBytes(wifiText)) == oem.GetString(oem.GetBytes(wifiText)), "saída OEM do Windows preserva caracteres representáveis na página local");
var aligned = WindowsSettingsText.Align(wifiText).Split('\n');
var fields = aligned.Where(l => l.Contains(" : ")).ToArray();
Check(fields.Select(l => l.IndexOf(" : ", StringComparison.Ordinal)).Distinct().Count() == 1, "colunas do netsh alinhadas apesar dos espaços originais");
Check(aligned.Last().IndexOf("software", StringComparison.Ordinal) == fields.Last().IndexOf("hardware", StringComparison.Ordinal), "continuação do rádio alinhada com o valor");
Check(WindowsSettingsText.Align("    IPv6  : fe80::1234\n    MAC   : 02:00:00:00:00:01").Contains("fe80::1234"), "alinhamento preserva dois pontos dos endereços");
try
{
    var actualWifi = await WindowsInventory.Command("netsh.exe", ["wlan", "show", "interfaces"], CancellationToken.None);
    Check(!actualWifi.Contains('\uFFFD') && !actualWifi.Contains('├') && !actualWifi.Contains('┬'), "captura netsh real sem corrupção de UTF-8");
}
catch (IOException ex) { Console.WriteLine("SKIP: WLAN indisponível neste Windows: " + ex.Message); }
var session = new Session { State = "completed", EndedAt = now };
Check(session.Copyright == $"Copyright © {DateTime.Now.Year} Fernando J. Cordeiro @ CONNECT10 INTERNET", "copyright usa ano vigente");
for (var i = 0; i < 20; i++) session.Pings.Add(new(now.AddSeconds(i * 2), "Cloudflare", "1.1.1.1", i != 10, i == 10 ? null : 10 + i, "test", false));
var stats = Diagnosis.Stats("Cloudflare", session.Pings);
Check(stats.LossPercent == 5 && stats.Received == 19, "perda e contagem");
Check(stats.JitterMs == 1, "jitter ignora lacunas e usa pares consecutivos");
Check(stats.P95Ms == 29, "percentil 95");
Check(Diagnosis.Analyze(session).Any(f => f.Title.Contains("perda ICMP")), "diagnóstico de perda parcial");
var blocked = new Session { State = "completed", EndedAt = now, Pings = session.Pings.Select(p => p with { Success = false, Ms = null }).ToList() };
Check(Diagnosis.Analyze(blocked).Any(f => f.Severity == "Inconclusivo" && f.Title.Contains("sem resposta")), "ICMP bloqueado não prova queda");
var loaded = new Session { State = "completed", EndedAt = now, Pings = session.Pings.Select(p => p with { UnderLoad = true }).ToList() };
Check(!Diagnosis.Analyze(loaded).Any(f => f.Title.Contains("perda ICMP")), "carga de velocidade separada");
session.Services.Add(new(now, "X", "x.com", "Social", 1, 2, 3, 403, "restricted", null));
Check(!Diagnosis.Analyze(session).Any(f => f.Title.Contains("falha de acesso")), "403 não é falha de transporte");
var legacy = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(session, Session.Json))!.AsObject();
foreach (var field in new[] { "providerStatus", "gameServers", "configuredGameServers", "resources" }) legacy.Remove(field);
legacy["appVersion"] = "1.0.0";
var previous = legacy.Deserialize<Session>(Session.Json)!;
ReportArchive.ValidateSession(previous);
var oldNetwork = JsonSerializer.Deserialize<NetworkSample>("""{"at":"2026-10-16T12:00:00Z","adapter":"Ethernet","type":"Ethernet","status":"Up","linkBps":1000000000,"receivedBytes":0,"sentBytes":0,"receiveErrors":0,"sendErrors":0}""", Session.Json)!;
Check(oldNetwork.IsPrimary == null, "histórico legado mantém rota como desconhecida");
var routed = oldNetwork with { IsPrimary = true };
Check(JsonSerializer.Deserialize<NetworkSample>(JsonSerializer.Serialize(routed, Session.Json), Session.Json)!.IsPrimary == true, "rota por amostra preservada no relatório");
Check(previous.ProviderStatus.Count == 0 && previous.Pings.Count == 20, "relatório da versão anterior continua legível");
Check(previous.Resources.Count == 0, "relatório legado sem CPU/memória continua legível");
Check(SystemResources.CpuUsage(100, 200, 100, 140, 260, 140) == 60, "CPU desconta tempo ocioso do total kernel + usuário");
Check(SystemResources.CpuUsage(100, 200, 100, 100, 200, 100) == null, "CPU sem intervalo não inventa zero");
Check(SystemResources.CpuUsage(100, 200, 100, 90, 190, 90) == null, "reinício de contador não produz CPU inválida");
session.Resources.Add(new(now, 20, 40, 400, 1000));
session.Resources.Add(new(now.AddSeconds(2), 60, 80, 800, 1000));
var resourceText = ResourceSummary.Describe(session);
Check(resourceText.Contains($"média {40:F1}%") && resourceText.Contains($"pico {60:F1}%") && resourceText.Contains($"pico {80:F1}%"), "média e pico de CPU e memória");
Check(Diagnosis.Analyze(session).Any(f => f.Evidence == resourceText), "recursos incluídos no diagnóstico exportável");
var resourceReader = new SystemResources();
Check(resourceReader.Read().CpuPercent == null, "primeira amostra de CPU aguarda intervalo");
await Task.Delay(200);
var realResources = resourceReader.Read();
Check(realResources.CpuPercent is >= 0 and <= 100 && realResources.MemoryPercent is >= 0 and <= 100 && realResources.TotalMemoryBytes > 0 && realResources.Error == null, "contadores reais de CPU e memória Windows");
var invalidResources = new Session { Resources = [new(now, 101, 40, 400, 1000)] };
Reject(() => ReportArchive.ValidateSession(invalidResources), "CPU fora de faixa rejeitada");
var temp = Path.Combine(Path.GetTempPath(), "net-inspector-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var path = Path.Combine(temp, "test.netinspect");
    ReportArchive.Write(path, session, new() { ["System.xml"] = "<Events />" });
    var report = ReportArchive.Read(path);
    Check(report.Session.Pings.Count == 20 && report.WindowsLogs.Count == 1, "round trip do pacote");
    Check(report.Session.Resources.Count == 2 && ResourceSummary.Describe(report.Session) == resourceText, "recursos preservados no pacote");
    Check(report.Session.Copyright == session.Copyright, "copyright preservado no relatório");
    using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) { zip.GetEntry("session.json")!.Delete(); using var writer = new StreamWriter(zip.CreateEntry("session.json").Open()); writer.Write("{}"); }
    Reject(() => ReportArchive.Read(path), "corrupção detectada por hash");
    var malicious = Path.Combine(temp, "malicious.netinspect");
    using (var zip = ZipFile.Open(malicious, ZipArchiveMode.Create)) zip.CreateEntry("../escape.txt");
    Reject(() => ReportArchive.Read(malicious), "path traversal rejeitado sem extrair");
}
finally { Directory.Delete(temp, true); }
if (args.Contains("--smoke"))
{
    using var monitor = new NetMonitor(1, args.Contains("--speed-smoke"), 12_000_000);
    var path = Path.GetFullPath("test-results/smoke.json");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(35));
    await monitor.Run(path, cancellation.Token);
    var snapshot = monitor.Snapshot();
    Check(snapshot.Resources.Count >= 2 && snapshot.Resources.Any(r => r.CpuPercent.HasValue), "recursos coletados durante teste real");
    Check(snapshot.Events.Any(e => e.Message.Contains("Recursos do computador —")), "médias e picos registrados no log final");
    Check(snapshot.State == "stopped", "cancelamento finaliza sessão");
    Check(snapshot.Pings.Count >= 10, "sondagens reais executadas");
    Check(snapshot.Services.Count > 0, "serviços reais sondados");
    Check(snapshot.Machine.Adapters.Count > 0, "inventário real de adaptadores");
    Check(snapshot.Network.Any(n => n.IsPrimary == true), "interface da rota real identificada nas amostras");
    Check(snapshot.Machine.Details.Values.All(v => !v.Contains('\uFFFD')), "saída netsh sem caracteres perdidos");
    if (args.Contains("--speed-smoke"))
    {
        Check(snapshot.Speeds.Any(s => s.Direction == "download") && snapshot.Speeds.Any(s => s.Direction == "upload"), "transferências de velocidade executadas");
        Check(snapshot.Speeds.Sum(s => s.BudgetBytes) <= 12_000_000, "orçamento de tráfego respeitado");
        foreach (var sample in snapshot.Speeds) Console.WriteLine($"Velocidade: {sample.Direction}, {sample.Mbps:F2} Mbps, {sample.Bytes} bytes, erro={sample.Error ?? "nenhum"}");
    }
    var logs = await WindowsInventory.Events(CancellationToken.None);
    Check(snapshot.ProviderStatus.Count > 0 && snapshot.GameServers.Count > 0, "status e servidores de jogos sondados");
    foreach (var sample in snapshot.ProviderStatus) Console.WriteLine($"Status {sample.Provider}: {sample.State} — {sample.Summary}");
    foreach (var sample in snapshot.GameServers) Console.WriteLine($"Servidor {sample.Name}: sucesso={sample.Success}, latência={sample.Ms:F2}ms, erro={sample.Error}");
    foreach (var entry in logs.Where(e => e.Key.EndsWith(".xml")))
    {
        using var reader = System.Xml.XmlReader.Create(new StringReader(entry.Value), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit });
        while (reader.Read()) { }
        Check(true, "XML de eventos válido: " + entry.Key);
        Check(!entry.Value.Contains('\uFFFD'), "eventos sem caracteres perdidos: " + entry.Key);
    }
    ReportArchive.Write(Path.ChangeExtension(path, ".netinspect"), snapshot, logs);
    Check(ReportArchive.Read(Path.ChangeExtension(path, ".netinspect")).WindowsLogs.Count == 5, "eventos Windows empacotados com falhas explícitas");
    Console.WriteLine($"Smoke: {snapshot.Pings.Count} pings, {snapshot.Services.Count} serviços, {snapshot.Machine.Extensions.Count} extensões; canais indisponíveis: {logs.Count(p => p.Key.EndsWith(".error.txt"))}");
}
if (args.Contains("--download-smoke"))
{
    using var downloadHttp = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    downloadHttp.DefaultRequestHeaders.UserAgent.ParseAdd("NetInspector/1.0");
    var sample = await new SpeedTester(downloadHttp).Measure("download", 128_000_000, CancellationToken.None, seconds: 4, warmup: .2);
    Console.WriteLine($"Download real: {sample.Bytes} bytes, {sample.Mbps:F2} Mbps, {sample.Seconds:F2}s, erro={sample.Error ?? "nenhum"}");
    Check(sample.Bytes > 0 && sample.Mbps > 0 && sample.Error == null, "download real com quatro fluxos e blocos de até 4 MB");
    Check(sample.Bytes <= sample.BudgetBytes && sample.BudgetBytes <= 128_000_000, "limite de consumo mantido no download real");
}
await MaintenanceChecks.Run(Check);
await ProtocolChecks.Run(Check);
await SpeedChecks.Run(Check);
if (args.Contains("--status-smoke"))
{
    using var http = new HttpClient();
    foreach (var provider in GameCatalog.Providers.Where(p => p.Api != null))
    {
        var result = await new ProviderProbe(http).Check(provider, CancellationToken.None);
        Console.WriteLine($"Fonte real {result.Provider}: {result.State} — {result.Summary}");
        Check(result.State is "operational" or "degraded", "API de status real interpretada: " + provider.Name);
    }
}
if (args.Contains("--updates-live"))
{
    using var updatesHttp = new HttpClient();
    var stableRelease = await ReleaseUpdates.Check(updatesHttp, CancellationToken.None);
    Check(stableRelease != null && stableRelease.Version >= new Version(1,5,0) && stableRelease.AssetName == ReleaseUpdates.AssetName, "release pública real consultada pelo mesmo serviço usado no aplicativo");
    Console.WriteLine("Release oficial: " + stableRelease!.Page);
}
Console.WriteLine($"{count} verificações passaram.");

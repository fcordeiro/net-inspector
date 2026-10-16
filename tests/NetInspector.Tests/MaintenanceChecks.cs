using System.Text;
using System.Text.Json;
using NetInspector.Core;

internal static class MaintenanceChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var options = new CleanupOptions();
        check(!options.Cookies && !options.Prefetch && !options.RecycleBin && !options.Downloads && !options.WindowsOld && !options.ToolCaches, "limpezas de maior impacto começam desmarcadas");
        check(Maintenance.RecycleOutcome(0, 0, unchecked((int)0x8000FFFF), 0, 0).Contains("já estava vazia"), "lixeira vazia não vira erro 0x8000FFFF");
        check(Maintenance.RecycleOutcome(0, 2, unchecked((int)0x8000FFFF), 0, 0).Contains("verificada"), "lixeira confirma estado final apesar do HRESULT");
        check(Maintenance.RecycleOutcome(0, 2, 0, 0, 1).Contains("Restam 1"), "sucesso nativo com itens restantes não é sucesso confirmado");
        check(Maintenance.RecycleOutcome(-1, 0, -1, -1, 0).Contains("não foi possível"), "consulta da lixeira inacessível não é tratada como vazia");
        // Exercise the real updater script in a child shell with every external
        // mutation replaced by a local function. No programs are installed.
        const string updaterMocks = """
            function Get-Command { param($Name, $ErrorAction) if ($script:ready) { 'mock-winget' } }
            function Install-PackageProvider { param($Name, $MinimumVersion, $Scope, [switch]$Force) if ($Name -ne 'NuGet' -or $Scope -ne 'CurrentUser') { throw 'provider inesperado' } }
            function Install-Module { param($Name, $Scope, $Repository, [switch]$Force) if ($Name -ne 'Microsoft.WinGet.Client' -or $Scope -ne 'CurrentUser') { throw 'modulo inesperado' } [Console]::WriteLine('MOCK-INSTALL') }
            function Import-Module { param($Name) }
            function Repair-WinGetPackageManager { param([switch]$Force, [switch]$Latest) $script:ready = $true }
            function winget.exe { if (($args -join ' ') -ne 'upgrade --all --silent --accept-source-agreements --accept-package-agreements --disable-interactivity') { throw 'argumentos inesperados' } [Console]::WriteLine('MOCK-UPGRADE'); $global:LASTEXITCODE = 0 }
            """;
        foreach (var installed in new[] { false, true })
        {
            var script = updaterMocks + "\n$script:ready = $" + (installed ? "true" : "false") + "\n" + Maintenance.UpgradeScript;
            var output = await WindowsInventory.Command("powershell.exe", ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], CancellationToken.None);
            check(output.Contains("MOCK-UPGRADE") && output.Contains("MOCK-INSTALL") == !installed, installed ? "updater usa WinGet existente sem instalar novamente (simulado)" : "updater instala WinGet ausente antes de atualizar (simulado)");
        }
        var battery = SystemResources.DecodePower(1, 8, 73, uint.MaxValue);
        check(battery is { HasBattery: true, OnAcPower: true, Charging: true, Percent: 73, RemainingSeconds: null }, "notebook carregando com autonomia desconhecida");
        check(SystemResources.DecodePower(0, 1, 20, 3600) is { OnAcPower: false, Percent: 20, RemainingSeconds: 3600 }, "bateria desconectada preserva carga e autonomia");
        check(SystemResources.DecodePower(1, 128, 255, uint.MaxValue) is { HasBattery: false, Percent: null }, "desktop sem bateria não inventa percentual");
        check(SystemResources.DecodePower(255, 255, 255, uint.MaxValue) is { HasBattery: null, OnAcPower: null, Percent: null, Charging: null }, "energia desconhecida não vira cabo conectado");
        var legacy = JsonSerializer.Deserialize<ResourceSample>("""{"at":"2026-10-16T12:00:00Z","cpuPercent":20,"memoryPercent":40}""", Session.Json)!;
        check(legacy.Power == null, "relatórios antigos sem energia continuam compatíveis");
        var sample = legacy with { Power = battery };
        check(JsonSerializer.Deserialize<ResourceSample>(JsonSerializer.Serialize(sample, Session.Json), Session.Json)!.Power == battery, "energia preservada na serialização");
        var xml = "\uFEFF<Events><Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>42</EventID></System></Event></Events>";
        check(WindowsEventXml.Parse(xml).Descendants().Any(e => e.Name.LocalName == "EventID" && e.Value == "42"), "eventos legados com BOM UTF-16 são interpretados");
        check(WindowsEventXml.Parse("").Root?.Name.LocalName == "Events", "canal vazio é válido");
        try { WindowsEventXml.Parse("<!DOCTYPE Events [<!ENTITY e SYSTEM 'file:///C:/Windows/win.ini'>]><Events>&e;</Events>"); check(false, "DTD deveria ser bloqueado"); }
        catch (System.Xml.XmlException) { check(true, "DTD e entidades externas permanecem bloqueados"); }
        var root = Path.Combine(Path.GetTempPath(), "net-inspector-cleanup-test-" + Guid.NewGuid().ToString("N"));
        try { Maintenance.DeleteContents(Path.GetPathRoot(root)!, DateTime.MaxValue, CancellationToken.None); check(false, "raiz deveria ser rejeitada"); }
        catch (InvalidOperationException) { check(true, "limpeza recusa raiz de volume"); }
        var outside = root + "-outside";
        Directory.CreateDirectory(root); Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(root, "old.tmp"), "old");
            File.SetLastWriteTimeUtc(Path.Combine(root, "old.tmp"), DateTime.UtcNow.AddDays(-2));
            File.WriteAllText(Path.Combine(root, "recent.tmp"), "keep");
            File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
            var result = Maintenance.DeleteContents(root, DateTime.UtcNow.AddDays(-1), CancellationToken.None);
            check(result.Deleted == 1 && result.Bytes == 3 && result.Skipped == 1 && File.Exists(Path.Combine(root, "recent.tmp")), "temporários recentes preservados e contagem de bytes correta");
            // Junction creation is available without symlink privileges on Windows.
            await WindowsInventory.Command("cmd.exe", ["/c", "mklink", "/J", Path.Combine(root, "junction"), outside], CancellationToken.None);
            result = Maintenance.DeleteContents(root, DateTime.MaxValue, CancellationToken.None);
            check(File.Exists(Path.Combine(outside, "keep.txt")) && result.Skipped == 1, "limpeza não atravessa junction para dados fora da seleção");
            Directory.Delete(Path.Combine(root, "junction"));
            File.WriteAllText(Path.Combine(root, "one.pf"), "prefetch");
            File.WriteAllText(Path.Combine(root, "keep.db"), "db");
            result = Maintenance.DeleteContents(root, DateTime.MaxValue, CancellationToken.None, "*.pf");
            check(result.Deleted == 1 && File.Exists(Path.Combine(root, "keep.db")), "Prefetch limita a exclusão a arquivos .pf");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { Maintenance.DeleteContents(root, DateTime.MaxValue, cancelled.Token); check(false, "cancelamento deveria interromper"); }
            catch (OperationCanceledException) { check(File.Exists(Path.Combine(root, "keep.db")), "cancelamento preserva arquivos restantes"); }
        }
        finally
        {
            var junction = Path.Combine(root, "junction");
            if (Directory.Exists(junction)) Directory.Delete(junction);
            Directory.Delete(root, true); Directory.Delete(outside, true);
        }
        var real = await WindowsInventory.Command("wevtutil.exe", ["qe", "System", "/c:2", "/rd:true", "/f:RenderedXml", "/e:Events", "/uni:true"], CancellationToken.None, encoding: Encoding.Unicode);
        check(!real.StartsWith('\uFEFF') && WindowsEventXml.Parse(real).Root != null, "wevtutil real decodifica Unicode e gera XML legível");
    }
}

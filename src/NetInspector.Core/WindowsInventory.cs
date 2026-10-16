using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Win32;

namespace NetInspector.Core;

public static class WindowsInventory
{
    public static async Task<string> Command(string file, string[] args, CancellationToken ct, int seconds = 20, System.Text.Encoding? encoding = null)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Não foi possível executar " + file);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        using var stdoutBytes = new MemoryStream();
        using var stderrBytes = new MemoryStream();
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(stdoutBytes, timeout.Token);
        var stderr = process.StandardError.BaseStream.CopyToAsync(stderrBytes, timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { try { process.Kill(true); } catch (InvalidOperationException) { } throw; }
        await Task.WhenAll(stdout, stderr);
        var output = encoding?.GetString(stdoutBytes.ToArray()).TrimStart('\uFEFF') ?? DecodeCommandOutput(stdoutBytes.ToArray());
        var error = encoding?.GetString(stderrBytes.ToArray()).TrimStart('\uFEFF') ?? DecodeCommandOutput(stderrBytes.ToArray());
        if (process.ExitCode != 0) throw new IOException($"{file}: {error.Trim()}");
        return output;
    }

    [System.Runtime.InteropServices.DllImport("iphlpapi.dll")]
    private static extern uint GetBestInterface(uint destination, out uint index);
    public static string? ProbeRouteAdapter()
    {
        try
        {
            var destination = BitConverter.ToUInt32(System.Net.IPAddress.Parse("1.1.1.1").GetAddressBytes());
            if (GetBestInterface(destination, out var index) != 0) return null;
            return NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(a => a.GetIPProperties().GetIPv4Properties()?.Index == index)?.Id;
        }
        catch (NetworkInformationException) { return null; }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetConsoleOutputCP();
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    public static string DecodeCommandOutput(byte[] bytes)
    {
        // netsh may emit UTF-8 (including when inherited from a UTF-8 console).
        // Decode the original bytes before any replacement characters can be introduced.
        try { return new System.Text.UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (System.Text.DecoderFallbackException) { }
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var codePage = GetConsoleOutputCP();
        if (codePage == 0 || codePage == 65001) codePage = GetOEMCP();
        return System.Text.Encoding.GetEncoding((int)codePage).GetString(bytes);
    }

    public static async Task<Inventory> Collect(CancellationToken ct)
    {
        var result = new Inventory();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var ip = adapter.GetIPProperties();
                result.Adapters.Add(new(adapter.Id, adapter.Name, adapter.NetworkInterfaceType.ToString(), adapter.OperationalStatus.ToString(), adapter.Speed,
                    ip.UnicastAddresses.Select(a => a.Address.ToString()).ToArray(), ip.GatewayAddresses.Select(a => a.Address.ToString()).ToArray(), ip.DnsAddresses.Select(a => a.ToString()).ToArray()));
            }
            catch (Exception ex) { result.Limitations.Add($"Adaptador {adapter.Name}: {ex.Message}"); }
        }
        // Ask Windows which interface owns the best IPv4 default route; account for interface metric.
        try
        {
            var route = await Command("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; Get-NetRoute -DestinationPrefix '0.0.0.0/0' | Where-Object {$_.NextHop -ne '0.0.0.0'} | ForEach-Object {$i=Get-NetIPInterface -InterfaceIndex $_.InterfaceIndex -AddressFamily IPv4; [pscustomobject]@{Index=$_.InterfaceIndex;Gateway=$_.NextHop;Metric=($_.RouteMetric+$i.InterfaceMetric)}} | Sort-Object Metric | Select-Object -First 1 | ConvertTo-Json -Compress"], ct);
            if (!string.IsNullOrWhiteSpace(route))
            {
                using var doc = JsonDocument.Parse(route);
                var index = doc.RootElement.GetProperty("Index").GetInt32();
                result.PrimaryAdapterId = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(a => a.GetIPProperties().GetIPv4Properties()?.Index == index)?.Id;
                result.Gateway = doc.RootElement.GetProperty("Gateway").GetString();
            }
            else result.Limitations.Add("Sem rota IPv4 padrão; conectividade somente IPv6 não é avaliada pelos alvos ICMP IPv4.");
        }
        catch (Exception ex) { result.Limitations.Add("Rota principal não identificada: " + ex.Message); }
        try
        {
            using var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            result.DefaultBrowser = choice?.GetValue("ProgId")?.ToString() ?? "Não identificado";
        }
        catch (Exception ex) { result.Limitations.Add("Navegador: " + ex.Message); }
        try
        {
            result.Details["Hardware e Windows"] = await Command("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $ErrorActionPreference='Stop'; Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer,Model,TotalPhysicalMemory | Format-List | Out-String -Width 180; Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed | Format-List | Out-String -Width 180; Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,OSArchitecture,LastBootUpTime | Format-List | Out-String -Width 180; Get-CimInstance Win32_DiskDrive | Select-Object Model,InterfaceType,Size,Status | Format-Table -AutoSize | Out-String -Width 180"], ct);
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed && d.Name.Length == 3))
            {
                var media = StorageMedia.Detect(drive.Name[..2]);
                result.Details["Armazenamento " + drive.Name[..2]] = $"Tipo: {media.Kind}\n{media.Evidence}\nFormato: {drive.DriveFormat}\nCapacidade: {drive.TotalSize:N0} bytes\nDisponível: {drive.AvailableFreeSpace:N0} bytes";
            }
        }
        catch (Exception ex) { result.Limitations.Add("Inventário de hardware: " + ex.Message); }
        foreach (var (name, file, args) in new (string, string, string[])[] {
            ("wifi", "netsh.exe", ["wlan", "show", "interfaces"]),
            ("proxyWinHttp", "netsh.exe", ["winhttp", "show", "proxy"]),
            ("tcp", "netsh.exe", ["interface", "tcp", "show", "global"]) })
        {
            try { result.Details[name] = await Command(file, args, ct); }
            catch (Exception ex) { result.Limitations.Add(name + ": " + ex.Message); }
        }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var (browser, relative) in new[] { ("Chrome", @"Google\Chrome\User Data"), ("Edge", @"Microsoft\Edge\User Data"), ("Brave", @"BraveSoftware\Brave-Browser\User Data") })
        {
            var root = Path.Combine(local, relative);
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var profile in Directory.EnumerateDirectories(root).Where(p => Path.GetFileName(p) == "Default" || Path.GetFileName(p).StartsWith("Profile ")))
                {
                    var extensions = Path.Combine(profile, "Extensions");
                    if (!Directory.Exists(extensions)) continue;
                    foreach (var id in Directory.EnumerateDirectories(extensions))
                    foreach (var manifest in Directory.EnumerateFiles(id, "manifest.json", SearchOption.AllDirectories).Where(f => Path.GetRelativePath(id, f).Split(Path.DirectorySeparatorChar).Length == 2))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                            var m = doc.RootElement;
                            result.Extensions.Add(new(browser, Path.GetFileName(profile), Path.GetFileName(id), m.GetProperty("name").GetString() ?? "?", m.GetProperty("version").GetString() ?? "?", "Arquivos presentes; ativação não confirmada"));
                        }
                        catch (Exception ex) { result.Limitations.Add($"Extensão {Path.GetFileName(id)}: {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex) { result.Limitations.Add(browser + ": " + ex.Message); }
        }
        var firefox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(firefox))
        {
            try
            {
                foreach (var profile in Directory.EnumerateDirectories(firefox))
                {
                    var path = Path.Combine(profile, "extensions.json");
                    if (!File.Exists(path)) continue;
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    foreach (var addon in doc.RootElement.GetProperty("addons").EnumerateArray())
                        result.Extensions.Add(new("Firefox", Path.GetFileName(profile), addon.GetProperty("id").GetString() ?? "?", addon.TryGetProperty("defaultLocale", out var locale) && locale.TryGetProperty("name", out var name) ? name.GetString() ?? "?" : "?", addon.GetProperty("version").GetString() ?? "?", addon.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True ? "Ativa" : "Inativa ou desconhecida"));
                }
            }
            catch (Exception ex) { result.Limitations.Add("Firefox: " + ex.Message); }
        }
        result.Limitations.Add("Extensões: somente perfis padrão do usuário atual (Chrome, Edge, Brave e Firefox); versões antigas podem aparecer. Nome __MSG_* não resolvido. Não prova interferência no navegador.");
        return result;
    }

    public static async Task<Dictionary<string, string>> Events(CancellationToken ct)
    {
        var logs = new Dictionary<string, string>();
        foreach (var channel in new[] { "System", "Application", "Microsoft-Windows-WLAN-AutoConfig/Operational", "Microsoft-Windows-NetworkProfile/Operational", "Microsoft-Windows-DNS-Client/Operational" })
        {
            var name = channel.Replace('/', '_');
            try { logs[name + ".xml"] = await Command("wevtutil.exe", ["qe", channel, "/q:*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= 86400000]]]", "/c:400", "/rd:true", "/f:RenderedXml", "/e:Events", "/uni:true"], ct, encoding: System.Text.Encoding.Unicode); }
            catch (Exception ex) { logs[name + ".error.txt"] = ex.Message; }
        }
        return logs;
    }
}

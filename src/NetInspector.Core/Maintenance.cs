using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NetInspector.Core;

public sealed record CleanupOptions(bool Ram = true, bool UserTemp = true, bool BrowserCache = true,
    bool Cookies = false, bool RecycleBin = false, bool Prefetch = false,
    bool Downloads = false, bool ToolCaches = false, bool CrashDumps = false, bool WindowsOld = false);

public sealed record CleanupResult(int Deleted, int Skipped, long Bytes);

public static class Maintenance
{
    // No user-supplied executable or script text is accepted by the UI.
    public const string UpgradeScript = """
        $ErrorActionPreference = 'Stop'
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            if (-not (Get-Command winget.exe -ErrorAction SilentlyContinue)) {
                Write-Output 'WinGet ausente. Instalando o cliente oficial Microsoft para o usuario atual...'
                Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Scope CurrentUser -Force | Out-Null
                Install-Module -Name Microsoft.WinGet.Client -Scope CurrentUser -Force -Repository PSGallery | Out-Null
                Import-Module Microsoft.WinGet.Client
                Repair-WinGetPackageManager -Force -Latest
                $env:PATH += ';' + [Environment]::GetFolderPath('LocalApplicationData') + '\Microsoft\WindowsApps'
            }
            if (-not (Get-Command winget.exe -ErrorAction SilentlyContinue)) { throw 'WinGet indisponivel apos instalacao. Reinicie o aplicativo e tente novamente.' }
            & winget.exe upgrade --all --silent --accept-source-agreements --accept-package-agreements --disable-interactivity
            $code = $LASTEXITCODE
            if ($code -eq -1978335189) { Write-Output 'Nenhuma atualizacao disponivel.'; exit 0 }
            exit $code
        } catch { Write-Error $_; exit 1 }
        """;

    public static async Task Cleanup(CleanupOptions options, IProgress<string> progress, CancellationToken ct)
    {
        if (options.Ram)
        {
            var trimmed = 0; var skipped = 0;
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    ct.ThrowIfCancellationRequested();
                    try { if (EmptyWorkingSet(process.Handle)) trimmed++; else skipped++; }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { skipped++; }
                }
            }
            progress.Report($"RAM: conjuntos de trabalho reduzidos em {trimmed} processos; {skipped} protegidos/indisponíveis. O Windows pode reutilizar a RAM imediatamente.");
        }
        if (options.UserTemp)
        {
            // Protect current jobs, runtime extraction and files in active use.
            var result = DeleteContents(Path.GetTempPath(), DateTime.UtcNow.AddDays(-1), ct);
            progress.Report($"Temporários do usuário (mais de 24h): {Describe(result)}");
        }
        if (options.BrowserCache || options.Cookies)
        {
            foreach (var target in BrowserTargets())
            {
                ct.ThrowIfCancellationRequested();
                var active = Process.GetProcessesByName(target.Process);
                try
                {
                    if (active.Length > 0) { progress.Report($"{target.Name}: ignorado; feche o navegador e tente novamente."); continue; }
                }
                finally { foreach (var process in active) process.Dispose(); }
                if (!Directory.Exists(target.Root)) continue;
                try
                {
                    foreach (var profile in Directory.EnumerateDirectories(target.Root))
                    {
                        if (IsLink(profile)) continue;
                        var name = Path.GetFileName(profile);
                        if (!target.Firefox && name != "Default" && !name.StartsWith("Profile ", StringComparison.Ordinal)) continue;
                        if (options.BrowserCache)
                            foreach (var relative in target.Firefox ? new[] { "cache2", "startupCache" } : new[] { "Cache", "Code Cache", "GPUCache" })
                                progress.Report($"{target.Name}/{name}/{relative}: {Describe(DeleteContents(Path.Combine(profile, relative), DateTime.MaxValue, ct))}");
                        if (options.Cookies)
                            foreach (var relative in target.Firefox ? new[] { "cookies.sqlite", "cookies.sqlite-wal", "cookies.sqlite-shm" } : new[] { "Network/Cookies", "Network/Cookies-wal", "Network/Cookies-shm", "Network/Cookies-journal", "Cookies", "Cookies-journal" })
                            {
                                var file = Path.Combine(profile, relative);
                                if (!File.Exists(file)) continue;
                                try { if (IsLink(file) || HasLinkedParent(file)) { progress.Report("Cookies ignorados: link simbólico."); continue; } File.Delete(file); progress.Report($"Cookies removidos: {target.Name}/{name}/{relative}"); }
                                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { progress.Report($"Cookies ignorados: {ex.Message}"); }
                            }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { progress.Report($"{target.Name}: {ex.Message}"); }
            }
        }
        if (options.RecycleBin)
        {
            ct.ThrowIfCancellationRequested();
            progress.Report(EmptyRecycleBin());
        }
        if (options.Downloads)
            progress.Report("Downloads (arquivos com mais de 30 dias): " + Describe(DeleteContents(DownloadsPath(), DateTime.UtcNow.AddDays(-30), ct)));
        if (options.CrashDumps)
            progress.Report("Relatórios de falhas do usuário (mais de 7 dias): " + Describe(DeleteContents(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"), DateTime.UtcNow.AddDays(-7), ct)));
        if (options.ToolCaches)
            foreach (var target in ToolCacheTargets())
            {
                var processes = target.Processes.SelectMany(Process.GetProcessesByName).ToArray();
                try
                {
                    if (processes.Length != 0) { progress.Report(target.Name + ": feche a ferramenta antes de limpar o cache."); continue; }
                    progress.Report(target.Name + ": " + Describe(DeleteContents(target.Path, DateTime.UtcNow.AddDays(-1), ct)));
                }
                finally { foreach (var process in processes) process.Dispose(); }
            }
        await Task.CompletedTask;
    }
    public static string DownloadsPath()
    {
        // Respect redirected Downloads folders, including other volumes.
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        var code = SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var pointer);
        if (code != 0) Marshal.ThrowExceptionForHR(code);
        try { return Marshal.PtrToStringUni(pointer) ?? throw new IOException("Downloads indisponível."); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }
    public static string RecycleOutcome(int beforeCode, long beforeCount, int emptyCode, int afterCode, long afterCount)
    {
        if (beforeCode == 0 && beforeCount == 0) return "Lixeira já estava vazia.";
        if (afterCode == 0 && afterCount == 0) return "Lixeira esvaziada e verificada.";
        return $"Lixeira: não foi possível confirmar a limpeza (Windows 0x{emptyCode:X8}; consulta 0x{afterCode:X8}). " +
            (afterCode == 0 ? $"Restam {afterCount} itens. " : "") + "Verifique as permissões e as unidades conectadas no Explorador.";
    }
    private static string EmptyRecycleBin()
    {
        var before = new RecycleInfo { Size = Marshal.SizeOf<RecycleInfo>() };
        var beforeCode = SHQueryRecycleBin(null, ref before);
        if (beforeCode == 0 && before.Items == 0) return RecycleOutcome(0, 0, 0, 0, 0);
        var code = SHEmptyRecycleBin(IntPtr.Zero, null, 1 | 2 | 4);
        var after = new RecycleInfo { Size = Marshal.SizeOf<RecycleInfo>() };
        var afterCode = SHQueryRecycleBin(null, ref after);
        return RecycleOutcome(beforeCode, before.Items, code, afterCode, after.Items);
    }
    private sealed record CacheTarget(string Name, string Path, string[] Processes);
    private static IEnumerable<CacheTarget> ToolCacheTargets()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var (name, folder, processes) in new[] {
            ("ChatGPT", "ChatGPT", new[] { "ChatGPT" }),
            ("Codex", "Codex", new[] { "Codex" }),
            ("Claude", "Claude", new[] { "Claude", "claude" }),
            ("Visual Studio Code", "Code", new[] { "Code" }),
            ("Antigravity", "Antigravity", new[] { "Antigravity", "antigravity" }) })
            foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache", "DawnGraphiteCache", "DawnWebGPUCache", "CachedData", "logs" })
                yield return new(name + "/" + cache, Path.Combine(roaming, folder, cache), processes);
        var packages = Path.Combine(local, "Packages");
        if (Directory.Exists(packages) && !IsLink(packages))
            foreach (var package in Directory.EnumerateDirectories(packages, "OpenAI.ChatGPT-Desktop_*"))
                foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache" })
                    yield return new("ChatGPT/" + cache, Path.Combine(package, "LocalCache", "Roaming", "ChatGPT", cache), ["ChatGPT", "Codex"]);
        var visualStudio = Path.Combine(local, "Microsoft", "VisualStudio");
        if (Directory.Exists(visualStudio) && !IsLink(visualStudio))
            foreach (var instance in Directory.EnumerateDirectories(visualStudio, "17.*"))
                yield return new("Visual Studio/ComponentModelCache", Path.Combine(instance, "ComponentModelCache"), ["devenv", "ServiceHub.Host.dotnet.x64"]);
    }
    private static string Describe(CleanupResult result) => $"{result.Deleted} arquivos removidos, {result.Bytes / 1048576.0:F1} MiB liberados em disco, {result.Skipped} ignorados (recentes, protegidos ou em uso).";
    private sealed record BrowserTarget(string Name, string Process, string Root, bool Firefox = false);
    private static IEnumerable<BrowserTarget> BrowserTargets()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var (name, process, relative) in new[] { ("Chrome", "chrome", "Google/Chrome/User Data"), ("Edge", "msedge", "Microsoft/Edge/User Data"), ("Brave", "brave", "BraveSoftware/Brave-Browser/User Data") })
            yield return new(name, process, Path.Combine(local, relative));
        yield return new("Firefox cache", "firefox", Path.Combine(local, "Mozilla/Firefox/Profiles"), true);
        yield return new("Firefox perfil", "firefox", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozilla/Firefox/Profiles"), true);
    }
    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static bool HasLinkedParent(string path)
    {
        for (var parent = Directory.GetParent(path); parent != null; parent = parent.Parent)
            if (IsLink(parent.FullName)) return true;
        return false;
    }
    // Iterative traversal never follows junctions or symlinks and never removes directories.
    public static CleanupResult DeleteContents(string root, DateTime olderThanUtc, CancellationToken ct, string pattern = "*")
    {
        root = Path.GetFullPath(root);
        var canonical = Path.TrimEndingDirectorySeparator(root);
        var protectedRoots = new[] { Path.GetPathRoot(root), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) };
        if (protectedRoots.Any(p => !string.IsNullOrEmpty(p) && canonical.Equals(Path.TrimEndingDirectorySeparator(p), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Pasta raiz ou de dados pessoais não pode ser alvo de limpeza.");
        var deleted = 0; var skipped = 0; long bytes = 0;
        if (!Directory.Exists(root)) return new(0, 0, 0);
        if (IsLink(root) || HasLinkedParent(root)) return new(0, 1, 0);
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (IsLink(directory)) { skipped++; continue; }
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var path = Path.GetFullPath(entry);
                        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || IsLink(path)) { skipped++; continue; }
                        if (Directory.Exists(path)) { pending.Push(path); continue; }
                        if (!System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path), true)) continue;
                        var file = new FileInfo(path);
                        if (file.LastWriteTimeUtc >= olderThanUtc) { skipped++; continue; }
                        var length = file.Length; file.Delete(); deleted++; bytes += length;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
        }
        return new(deleted, skipped, bytes);
    }
    public static CleanupResult CleanPrefetch(CancellationToken ct) => DeleteContents(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch"), DateTime.MaxValue, ct, "*.pf");
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr window, string? root, uint flags);
    [StructLayout(LayoutKind.Sequential)]
    private struct RecycleInfo { public int Size; public long Bytes; public long Items; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? root, ref RecycleInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
}

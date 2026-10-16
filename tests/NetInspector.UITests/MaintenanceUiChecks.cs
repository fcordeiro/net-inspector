using System.Diagnostics;
using System.Reflection;
using NetInspector.Desktop;

namespace NetInspector.UITests;

internal static class MaintenanceUiChecks
{
    internal static void Run()
    {
        var runner = typeof(ToolsDialog).Assembly.GetType("NetInspector.Desktop.MaintenanceRunner")!;
        var method = runner.GetMethod("Run", BindingFlags.NonPublic | BindingFlags.Static)!;
        var native = (string)runner.GetField("NativeFunction", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        async Task<int> RunScript(string script, Action<string> report, CancellationToken ct) => await (Task<int>)method.Invoke(null, [script, false, report, ct])!;
        static T Wait<T>(Task<T> task) { while (!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(15); } return task.GetAwaiter().GetResult(); }
        var lines = new List<string>();
        var code = Wait(RunScript("Write-Output 'saída com acentuação'; Write-Output 'fim'", lines.Add, CancellationToken.None));
        if (code != 0 || !string.Join("\n", lines).Contains("saída com acentuação")) throw new Exception("Runner perdeu saída UTF-8");
        const string accented = "Concluído | Versão | Disponível | 2 atualizações disponíveis.";
        var emitter = "$bytes=[Text.Encoding]::UTF8.GetBytes('" + accented + "'); $stream=[Console]::OpenStandardOutput(); $stream.Write($bytes,0,$bytes.Length)";
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(emitter));
        lines.Clear();
        code = Wait(RunScript("& powershell.exe -NoProfile -NonInteractive -EncodedCommand " + encoded, lines.Add, CancellationToken.None));
        if (code != 0 || !string.Join("\n", lines).Contains(accented)) throw new Exception("Saída nativa UTF-8 perdeu acentos na passagem pelo PowerShell");
        lines.Clear();
        code = Wait(RunScript(native + "\nInvoke-Native 'powershell.exe' '-NoProfile -NonInteractive -EncodedCommand " + encoded + "'", lines.Add, CancellationToken.None));
        if (code != 0 || !string.Join("\n", lines).Contains(accented)) throw new Exception("Runner nativo perdeu acentos UTF-8");
        if (Environment.GetEnvironmentVariable("NETINSPECTOR_WINGET_SMOKE") == "1")
        {
            lines.Clear();
            code = Wait(RunScript("& winget.exe upgrade --disable-interactivity; if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189) { throw ('Falha na consulta: ' + $LASTEXITCODE) }", lines.Add, CancellationToken.None));
            var output = string.Join("\n", lines); File.WriteAllText("test-results/winget-encoding.txt", output);
            if (code != 0 || output.Contains('├') || output.Contains('\uFFFD')) throw new Exception("Consulta real WinGet inválida ou com acentuação corrompida");
            Console.WriteLine("OK: consulta real WinGet sem instalação e sem corrupção de acentos");
        }
        code = Wait(RunScript("throw 'falha controlada'", lines.Add, CancellationToken.None));
        if (code != 1 || !string.Join("\n", lines).Contains("falha controlada")) throw new Exception("Runner perdeu falha");
        code = Wait(RunScript(native + "\nInvoke-Native 'cmd.exe' '/c echo native-ok'", lines.Add, CancellationToken.None));
        if (code != 0 || !string.Join("\n", lines).Contains("native-ok")) throw new Exception("Runner nativo falhou");
        using (var stop = new CancellationTokenSource())
        {
            var task = RunScript(native + "\nInvoke-Native 'ping.exe' '-n 30 127.0.0.1'", _ => stop.Cancel(), stop.Token);
            var timeout = Stopwatch.StartNew();
            while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(12)) { Application.DoEvents(); Thread.Sleep(15); }
            stop.Cancel();
            if (!task.IsCompleted || Wait(task) == 0) throw new Exception("Cancelamento nativo não interrompeu a etapa");
        }
        // Validate the bitmap interop type without reading or modifying any real volume.
        var bitmapScript = (string)typeof(DiskDialog).GetField("BitmapScript", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        var declaration = bitmapScript[..bitmapScript.IndexOf("function Read-Allocation", StringComparison.Ordinal)];
        code = Wait(RunScript(declaration + "\nWrite-Output 'bitmap-compiled'", lines.Add, CancellationToken.None));
        if (code != 0 || !string.Join("\n", lines).Contains("bitmap-compiled")) throw new Exception("Interop de alocação não compilou");
        VerifyWorktreeValidation();
        Console.WriteLine("OK: runner UTF-8, falhas, saída nativa, cancelamento, interop e proteções de worktrees");
    }
    private static void VerifyWorktreeValidation()
    {
        var root = Path.Combine(Path.GetTempPath(), "net-inspector-worktree-test-" + Guid.NewGuid().ToString("N"));
        var repo = Path.Combine(root, "repo"); var worktree = Path.Combine(root, "worktree"); Directory.CreateDirectory(repo);
        static void Git(string cwd, params string[] args)
        {
            var info = new ProcessStartInfo("git.exe") { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var p = Process.Start(info)!; var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync(); p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception(error.GetAwaiter().GetResult()); output.GetAwaiter().GetResult();
        }
        var validate = typeof(WorktreeDialog).GetMethod("Validate", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Eligible(string path)
        {
            try { ((Task)validate.Invoke(null, [path, CancellationToken.None])!).GetAwaiter().GetResult(); return true; }
            catch (IOException) { return false; }
        }
        try
        {
            Git(repo, "init"); File.WriteAllText(Path.Combine(repo, "tracked.txt"), "keep");
            Git(repo, "add", "."); Git(repo, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-m", "fixture");
            Git(repo, "worktree", "add", "--detach", worktree);
            if (Eligible(worktree)) throw new Exception("Worktree sem referência remota foi liberada");
            Git(repo, "update-ref", "refs/remotes/fixture/main", "HEAD");
            if (!Eligible(worktree) || Eligible(repo)) throw new Exception("Detecção de worktree secundária incorreta");
            File.WriteAllText(Path.Combine(worktree, "untracked.txt"), "preserve");
            if (Eligible(worktree)) throw new Exception("Arquivo não versionado ignorado");
            File.Delete(Path.Combine(worktree, "untracked.txt"));
            File.AppendAllText(Path.Combine(worktree, "tracked.txt"), "dirty");
            if (Eligible(worktree)) throw new Exception("Alteração local ignorada");
            Git(worktree, "restore", "tracked.txt");
            File.WriteAllText(Path.Combine(repo, ".git", "info", "exclude"), "private.bin\n");
            File.WriteAllText(Path.Combine(worktree, "private.bin"), "private");
            if (Eligible(worktree)) throw new Exception("Arquivo ignorado desprotegido");
            File.Delete(Path.Combine(worktree, "private.bin"));
            Git(repo, "worktree", "lock", worktree);
            if (Eligible(worktree)) throw new Exception("Worktree bloqueada liberada");
            Git(repo, "worktree", "unlock", worktree);
            Git(repo, "worktree", "remove", worktree);
        }
        finally
        {
            var resolved = Path.GetFullPath(root);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("net-inspector-worktree-test-")) throw new Exception("Pasta de teste inválida");
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(resolved, true);
        }
    }
}

using System.Diagnostics;

namespace NetInspector.Desktop;

public sealed class WorktreeDialog : Form
{
    private sealed record Candidate(string Path, string GitDir)
    { public override string ToString() => Path; }
    private readonly CheckedListBox list = new() { Dock = DockStyle.Fill, CheckOnClick = true, BackColor = Theme.Surface, ForeColor = Theme.Text };
    private readonly TextBox log = new() { Dock = DockStyle.Bottom, Height = 160, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Background, ForeColor = Theme.Text };
    private readonly Button scan = Theme.Action("Procurar worktrees", Theme.Surface);
    private readonly Button add = Theme.Action("Escolher pasta…", Theme.Surface);
    private readonly Button remove = Theme.Action("Remover selecionados", Theme.Blue);
    private readonly Button close = Theme.Action("Fechar", Theme.Surface);
    private CancellationTokenSource? cancellation;
    public WorktreeDialog(bool scanOnShow = true)
    {
        Theme.Apply(this, "Ferramentas • Limpar worktrees"); MinimumSize = new(950, 650); Size = new(1050, 760);
        var help = new Label { Dock = DockStyle.Top, Height = 140, Padding = new(14), Text = "Busca worktrees do Codex/ChatGPT, Claude e Antigravity nas pastas conhecidas. Você também pode escolher uma pasta. Só permite remover worktrees secundárias sem alterações, arquivos ignorados, submódulos ou bloqueios, com HEAD contido em uma referência remota. Cada seleção é verificada novamente. Use o arquivamento na própria ferramenta se quiser preservar o estado da conversa." };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 65, Padding = new(14, 8, 0, 0) };
        buttons.Controls.AddRange([scan, add, remove, close]); Controls.Add(list); Controls.Add(log); Controls.Add(help); Controls.Add(buttons);
        Theme.ToolWindow(this, "Worktrees e projetos", help);
        scan.Click += async (_, _) => await Scan(null);
        add.Click += async (_, _) => { using var folder = new FolderBrowserDialog { Description = "Pasta de worktree ou pasta que contém worktrees" }; if (folder.ShowDialog(this) == DialogResult.OK) await Scan(folder.SelectedPath); };
        remove.Click += async (_, _) => await Remove();
        close.Click += (_, _) => { if (cancellation == null) Close(); else cancellation.Cancel(); };
        FormClosing += (_, e) => { if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); } };
        if (scanOnShow) Shown += async (_, _) => await Scan(null);
    }
    private void Append(string text) { if (log.TextLength > 100_000) log.Text = log.Text[^60_000..]; log.AppendText(text + Environment.NewLine); }
    private void Busy(bool value) { scan.Enabled = add.Enabled = remove.Enabled = list.Enabled = !value; close.Text = value ? "Cancelar" : "Fechar"; }
    private static async Task<string> Git(string root, CancellationToken ct, params string[] args)
    {
        var start = new ProcessStartInfo("git.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-C"); start.ArgumentList.Add(root);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Git indisponível.");
        var output = process.StandardOutput.ReadToEndAsync(ct); var error = process.StandardError.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } await process.WaitForExitAsync(); throw; }
        var result = await output; var stderr = await error;
        if (process.ExitCode != 0) throw new IOException(stderr.Trim());
        return result.Trim();
    }
    private static bool Linked(string path)
    {
        for (var dir = new DirectoryInfo(path); dir != null; dir = dir.Parent)
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }
    private static async Task<Candidate> Validate(string path, CancellationToken ct)
    {
        path = Path.GetFullPath(path);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var (folder, names) in new[] { (".codex", new[] { "Codex" }), (".claude", new[] { "Claude", "claude" }), (Path.Combine(".gemini", "antigravity"), new[] { "Antigravity", "antigravity" }) })
            if (path.StartsWith(Path.Combine(profile, folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var active = names.SelectMany(Process.GetProcessesByName).ToArray();
                try { if (active.Length > 0) throw new IOException("feche a ferramenta que gerencia esta worktree antes de removê-la"); }
                finally { foreach (var process in active) process.Dispose(); }
            }
        if (Linked(path) || !File.Exists(Path.Combine(path, ".git")) || (File.GetAttributes(Path.Combine(path, ".git")) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("não é worktree secundária regular");
        var common = await Git(path, ct, "rev-parse", "--path-format=absolute", "--git-common-dir");
        var registered = await Git(path, ct, "worktree", "list", "--porcelain");
        var block = registered.Split("\n\n").FirstOrDefault(b => b.Split('\n').Any(l => l.TrimEnd('\r').Equals("worktree " + path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)));
        if (block == null || block.Contains("\nlocked") || block.Contains("\nprunable")) throw new IOException("worktree não registrada, bloqueada ou indisponível");
        if ((await Git(path, ct, "status", "--porcelain=v1", "--untracked-files=all", "--ignored")).Length != 0) throw new IOException("contém alterações ou arquivos não versionados/ignorados");
        if ((await Git(path, ct, "ls-files", "--stage")).Split('\n').Any(l => l.StartsWith("160000 "))) throw new IOException("contém submódulos");
        if ((await Git(path, ct, "branch", "-r", "--contains", "HEAD")).Length == 0) throw new IOException("HEAD não está preservado em uma referência remota local");
        return new(path, common);
    }
    private async Task Scan(string? selected)
    {
        cancellation = new(); Busy(true);
        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = selected != null ? new[] { selected } : new[] { Path.Combine(profile, ".codex", "worktrees"), Path.Combine(profile, ".claude", "worktrees"), Path.Combine(profile, ".gemini", "antigravity", "worktrees") };
            if (selected == null) list.Items.Clear();
            // Bounded breadth-first discovery; never traverses junctions or project data recursively.
            var pending = new Queue<(string Path, int Depth)>(); foreach (var root in roots) pending.Enqueue((root, 0));
            var visited = 0;
            while (pending.TryDequeue(out var entry) && visited++ < 2000)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                try
                {
                    if (!Directory.Exists(entry.Path) || Linked(entry.Path)) continue;
                    if (File.Exists(Path.Combine(entry.Path, ".git")))
                    {
                        try { var candidate = await Validate(entry.Path, cancellation.Token); if (!list.Items.Cast<Candidate>().Any(c => c.Path.Equals(candidate.Path, StringComparison.OrdinalIgnoreCase))) list.Items.Add(candidate, false); }
                        catch (IOException ex) { Append(entry.Path + ": preservado — " + ex.Message); }
                        continue;
                    }
                    if (entry.Depth < 3 && !Directory.Exists(Path.Combine(entry.Path, ".git")))
                        foreach (var dir in Directory.EnumerateDirectories(entry.Path)) pending.Enqueue((dir, entry.Depth + 1));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Append("Ignorado: " + ex.Message); }
            }
            Append($"Worktrees elegíveis: {list.Items.Count}. Todas começam desmarcadas. Busca limitada a 2.000 pastas e três níveis.");
        }
        catch (Exception ex) { Append(ex is OperationCanceledException ? "Busca cancelada." : "Falha: " + ex.Message); }
        finally { cancellation.Dispose(); cancellation = null; Busy(false); }
    }
    private async Task Remove()
    {
        var selected = list.CheckedItems.Cast<Candidate>().ToArray();
        if (selected.Length == 0) { Append("Selecione uma worktree elegível."); return; }
        if (MessageBox.Show(this, "Remover permanentemente estas pastas de trabalho?\r\n\r\n" + string.Join("\r\n", selected.Select(c => c.Path)) + "\r\n\r\nOs branches serão mantidos. Conversas da ferramenta podem perder o vínculo com a pasta.", "Confirmar worktrees", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        cancellation = new(); Busy(true);
        try
        {
            foreach (var candidate in selected)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                try
                {
                    var verified = await Validate(candidate.Path, cancellation.Token);
                    // Git performs its own final clean/locked check; never use --force or recursive filesystem deletion.
                    await Git(Path.GetDirectoryName(verified.GitDir)!, CancellationToken.None, "--git-dir=" + verified.GitDir, "worktree", "remove", verified.Path);
                    list.Items.Remove(candidate); Append("Removida: " + candidate.Path);
                }
                catch (IOException ex) { Append("Preservada: " + candidate.Path + " — " + ex.Message); }
            }
        }
        catch (OperationCanceledException) { Append("Cancelado antes da próxima worktree."); }
        finally { cancellation.Dispose(); cancellation = null; Busy(false); }
    }
}

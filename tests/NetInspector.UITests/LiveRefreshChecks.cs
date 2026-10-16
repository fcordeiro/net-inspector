using System.Runtime.InteropServices;
using NetInspector.Core;
using NetInspector.Desktop;

namespace NetInspector.UITests;

internal static class LiveRefreshChecks
{
    public static void Run()
    {
        using var form = new Form { ClientSize = new Size(1100, 650), ShowInTaskbar = false };
        using var dashboard = new Dashboard();
        form.Controls.Add(dashboard);
        form.Show();
        var tabs = All(dashboard).OfType<TabControl>().Single();
        var at = DateTimeOffset.UtcNow;
        var session = new Session { State = "running", RequestedMinutes = 15, StartedAt = at };
        session.Machine.Details["wifi"] = string.Join("\n", Enumerable.Range(0, 200).Select(i => $"Configuração {i}: conexão disponível " + new string('x', 160)));
        for (var i = 0; i < 100; i++)
            session.Pings.Add(new(at.AddSeconds(i), "Cloudflare", "1.1.1.1", true, 10 + i % 5, "Success", false));
        dashboard.ShowSession(session, live: true);

        using var other = new Form { ShowInTaskbar = false, Size = new Size(200, 100) };
        other.Show();
        foreach (var focused in new[] { false, true })
        foreach (var tab in new[] { 0, 13 })
        {
            tabs.SelectedIndex = tab;
            var text = All(tabs.SelectedTab!).OfType<RichTextBox>().Single();
            if (focused) { form.Activate(); text.Focus(); } else { other.Activate(); }
            Application.DoEvents();
            text.Select(text.TextLength / 2, 17);
            text.ScrollToCaret();
            if (tab == 13)
            {
                var offset = Scroll(text);
                offset.X = 120;
                SendMessage(text.Handle, 0x4DE, IntPtr.Zero, ref offset);
            }
            var scroll = Scroll(text);
            if (scroll.Y <= 0) throw new Exception("Cenário precisa de texto longo com rolagem");
            if (tab == 13 && scroll.X <= 0) throw new Exception("Cenário precisa de rolagem horizontal");
            var start = text.SelectionStart;
            var selected = text.SelectionLength;
            var changed = 0;
            EventHandler count = (_, _) => changed++;
            text.TextChanged += count;
            for (var i = 0; i < 3; i++) { dashboard.ShowSession(session); Application.DoEvents(); }
            if (changed != 0) throw new Exception("Conteúdo idêntico foi reconstruído");
            if (tab == 0) session.Pings.Add(new(at.AddSeconds(101), "Cloudflare", "1.1.1.1", true, 90, "Success", false));
            else session.Machine.Details["wifi"] += "\nNovo dado de teste";
            dashboard.ShowSession(session);
            Application.DoEvents();
            if (changed == 0) throw new Exception("Dados novos não apareceram");
            if (text.SelectionStart != start || text.SelectionLength != selected || Scroll(text) != scroll)
                throw new Exception($"Texto perdeu seleção/rolagem na aba {tab}, foco {focused}");
            text.TextChanged -= count;
        }

        tabs.SelectedIndex = 6; // Pings: tabela longa, com novas amostras no topo.
        var grid = All(tabs.SelectedTab!).OfType<DataGridView>().Single();
        grid.CurrentCell = grid[1, 45];
        grid.FirstDisplayedScrollingRowIndex = 40;
        var source = grid.DataSource;
        dashboard.ShowSession(session);
        if (!ReferenceEquals(source, grid.DataSource)) throw new Exception("Tabela idêntica foi recarregada");
        session.Pings.Add(new(at.AddSeconds(102), "Cloudflare", "1.1.1.1", true, 11, "Success", false));
        dashboard.ShowSession(session);
        if (grid.Rows.Count != session.Pings.Count || grid.FirstDisplayedScrollingRowIndex != 41 || grid.CurrentCell?.RowIndex != 46)
            throw new Exception("Tabela perdeu posição ou não recebeu a nova amostra");
        Console.WriteLine("Atualização ao vivo: texto e tabelas preservam seleção/rolagem, com e sem foco; conteúdo idêntico não é recarregado.");
    }

    private static Point Scroll(RichTextBox text)
    {
        var point = new Point();
        SendMessage(text.Handle, 0x4DD, IntPtr.Zero, ref point);
        return point;
    }

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var descendant in All(child)) yield return descendant; }
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, ref Point point);
}

using NetInspector.Core;
using NetInspector.Desktop;

namespace NetInspector.UITests;

internal static class UpdateUiChecks
{
    public static void Run()
    {
        foreach (var state in new[] { "current", "new", "failed" })
        {
            var release = new ReleaseUpdate(state == "new" ? new Version(ReleaseUpdates.Current.Major + 1, 0, 0) : ReleaseUpdates.Current, ReleaseUpdates.ReleasesPage + "/tag/test", "", ReleaseUpdates.AssetName);
            using var dialog = new UpdateDialog(_ => state == "failed" ? Task.FromException<ReleaseUpdate?>(new IOException("Falha controlada")) : Task.FromResult<ReleaseUpdate?>(release));
            dialog.ShowInTaskbar = false; dialog.Opacity = 0; dialog.Show(); Application.DoEvents();
            var text = string.Join("\n", dialog.Controls.OfType<Label>().Select(l => l.Text));
            var expected = state switch { "current" => "Nenhuma atualização disponível", "new" => "Nova versão disponível", _ => "Não foi possível verificar atualizações" };
            if (!text.Contains(expected) || text.Contains("GitHub") || text.Contains("releases") || text.Contains("navegador") || text.Contains("EXE")) throw new Exception("Mensagem de atualização incorreta: " + state);
            var button = dialog.Controls.OfType<ControlRow>().Single().Controls.OfType<Button>().Single(b => b.Text == "Abrir download");
            if (button.Visible != (state == "new") || button.Enabled != (state == "new")) throw new Exception("Ação de download incorreta: " + state);
            using var bitmap = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
            bitmap.Save("test-results/update-" + state + ".png"); dialog.Close();
        }
        Console.WriteLine("OK: mensagens concisas de atualização atual, nova versão e falha; download somente quando disponível");
    }
}

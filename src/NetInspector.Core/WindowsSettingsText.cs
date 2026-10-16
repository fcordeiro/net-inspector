namespace NetInspector.Core;

public static class WindowsSettingsText
{
    public static string Align(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        // A field separator has whitespace before the colon; MAC/IPv6 values do not.
        int Separator(string line)
        {
            var colon = line.IndexOf(':');
            return colon > 0 && char.IsWhiteSpace(line[colon - 1]) && colon < line.TrimEnd().Length - 1 ? colon : -1;
        }
        var width = lines.Where(l => Separator(l) >= 0).Select(l => l[..Separator(l)].Trim().Length).DefaultIfEmpty(0).Max();
        var output = new List<string>();
        var inField = false;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) { inField = false; continue; }
            var colon = Separator(line);
            if (colon >= 0)
            {
                output.Add("  " + line[..colon].Trim().PadRight(width) + " : " + line[(colon + 1)..].Trim());
                inField = true;
            }
            else if (inField && char.IsWhiteSpace(line[0]))
                output.Add(new string(' ', width + 5) + line.Trim());
            else { output.Add(line.TrimEnd()); inField = false; }
        }
        return string.Join("\n", output);
    }
}

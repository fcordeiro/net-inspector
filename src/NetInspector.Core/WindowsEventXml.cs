using System.Xml;
using System.Xml.Linq;

namespace NetInspector.Core;

public static class WindowsEventXml
{
    // StringReader retains the UTF-16 BOM emitted by wevtutil. XmlReader expects
    // the BOM to have already been consumed when reading characters, not bytes.
    public static string Normalize(string text)
    {
        var normalized = text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        return string.IsNullOrWhiteSpace(normalized) ? "<Events />" : normalized;
    }
    public static XDocument Parse(string text)
    {
        using var reader = XmlReader.Create(new StringReader(Normalize(text)), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 });
        return XDocument.Load(reader);
    }
}

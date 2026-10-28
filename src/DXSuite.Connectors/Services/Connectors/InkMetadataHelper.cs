using System.Xml;

namespace DXSuite.Connectors.Services.Connectors;

public static class InkMetadataHelper
{
    public static string? GetInkIdentifierFromMetadata(string metadataPath)
    {
        try
        {
            if (!File.Exists(metadataPath)) return null;

            var doc = new XmlDocument();
            doc.Load(metadataPath);
            var nodes = doc.SelectNodes("usermetadata/field");
            if (nodes == null) return null;

            foreach (XmlNode node in nodes)
            {
                var nameAttr = node.Attributes?["name"];
                var valueAttr = node.Attributes?["value"];
                if (nameAttr?.Value == "Identifier" && valueAttr != null)
                    return valueAttr.Value;
            }
        }
        catch { }
        return null;
    }

    public static void RemoveInkIdentifierFromMetadata(string metadataPath)
    {
        if (!File.Exists(metadataPath)) return;

        var doc = new XmlDocument();
        doc.Load(metadataPath);

        var fields = doc.SelectNodes("//usermetadata/field");
        if (fields == null) return;

        foreach (XmlNode field in fields)
        {
            if (field.Attributes?["name"]?.Value == "Identifier")
            {
                field.ParentNode?.RemoveChild(field);
                break;
            }
        }

        doc.Save(metadataPath);
    }
}

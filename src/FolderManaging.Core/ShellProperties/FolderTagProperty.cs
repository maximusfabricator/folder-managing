using System.Globalization;
using System.Text;
using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core.ShellProperties;

/// <summary>
/// Writes folder tag labels into <c>desktop.ini</c> so Explorer can group and sort them A to Z.
/// A folder with several tags is listed in each tag group. Untagged folders are left alone.
/// </summary>
public static class FolderTagProperty
{
    public static readonly Guid FormatId = new("C3A91E70-5D4B-4F18-9E2A-7B6C5D4E3F21");
    public const uint PropId = 2;
    public const string CanonicalName = "FolderManaging.Tags";
    public const string IniKey = "Tags";

    /// <summary>Always English, same reason as the color InfoTip.</summary>
    public const string SortHintInfoTip =
        "Folder Managing tags. Group or sort: Group by or Sort → \"Folder tags\", A to Z.";

    public const string CombinedInfoTip =
        "Folder Managing. Sort color: Sort → \"Folder color\", ascending. Group tags: Group by → \"Folder tags\", A to Z.";

    public static string IniSectionHeader => "[" + FormatId.ToString("B").ToUpperInvariant() + "]";

    public static bool IsOwnedSectionHeader(string trimmedSectionLine) =>
        trimmedSectionLine.Equals(IniSectionHeader, StringComparison.OrdinalIgnoreCase);

    /// <summary>Visible labels for one folder, unique and A to Z.</summary>
    public static IReadOnlyList<string> LabelsFor(AppStateDocument doc, string folderPath)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        FolderStateEntry? entry = null;
        foreach (var folder in doc.Folders)
        {
            try
            {
                var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Path));
                if (string.Equals(candidate, full, StringComparison.OrdinalIgnoreCase))
                {
                    entry = folder;
                    break;
                }
            }
            catch
            {
                // skip a broken path stored in state.json
            }
        }
        if (entry is null || entry.Tags.Count == 0)
            return [];

        var labels = new List<string>();
        foreach (var id in entry.Tags)
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;
            var preset = doc.MenuTags.FirstOrDefault(t =>
                string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
            var label = Sanitize(string.IsNullOrWhiteSpace(preset?.Label) ? id : preset!.Label);
            if (label.Length == 0)
                continue;
            if (labels.Contains(label, StringComparer.CurrentCultureIgnoreCase))
                continue;
            labels.Add(label);
        }

        labels.Sort(StringComparer.CurrentCultureIgnoreCase);
        return labels;
    }

    public static string Join(IReadOnlyList<string> labels) =>
        string.Join(";", labels);

    public static void AppendPropertyBag(StringBuilder body, IReadOnlyList<string> labels)
    {
        if (labels.Count == 0)
            return;

        var nl = Environment.NewLine;
        body.Append(IniSectionHeader).Append(nl)
            .Append("Prop").Append(PropId.ToString(CultureInfo.InvariantCulture))
            .Append('=').Append(FolderColorHueProperty.VarTypeVectorLpstr.ToString(CultureInfo.InvariantCulture))
            .Append(',').Append(Join(labels)).Append(nl);
    }

    static string Sanitize(string label)
    {
        var chars = label.Trim().ToCharArray();
        var sb = new StringBuilder(chars.Length);
        foreach (var c in chars)
        {
            if (c is '\r' or '\n' or '\t' or ';')
                sb.Append(' ');
            else
                sb.Append(c);
        }

        var text = sb.ToString().Trim();
        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        return text.Length > 64 ? text[..64].Trim() : text;
    }
}

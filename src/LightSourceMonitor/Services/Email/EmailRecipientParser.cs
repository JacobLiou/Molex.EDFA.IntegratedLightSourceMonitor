namespace LightSourceMonitor.Services.Email;

/// <summary>
/// Parses the recipients text box: semicolons, commas, or line breaks between addresses.
/// API JSON field <c>To</c> is sent as a comma-separated list.
/// </summary>
public static class EmailRecipientParser
{
    private static readonly char[] Separators = { ';', ',', '\r', '\n' };

    public static IReadOnlyList<string> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var part in raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = part.Trim();
            if (t.Length > 0)
                list.Add(t);
        }

        return list;
    }

    public static string ToApiToField(string? raw) =>
        string.Join(";", Parse(raw));
}

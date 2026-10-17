namespace HMI.Server;

/// <summary>
/// Escapes CSV fields and neutralizes spreadsheet formula prefixes.
/// All values are quoted; formula-like text is prefixed with an apostrophe so
/// opening an exported alarm history in spreadsheet software cannot execute it.
/// </summary>
public static class CsvSafety
{
    public static string EscapeField(string? value)
    {
        var field = value ?? string.Empty;
        var firstMeaningfulCharacter = field.FirstOrDefault(character => !char.IsWhiteSpace(character));
        if (firstMeaningfulCharacter is '=' or '+' or '-' or '@')
            field = "'" + field;

        return "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
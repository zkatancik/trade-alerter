namespace TradeAlerter.Plugins.Utilities;

/// <summary>
/// One-time builder for the ANR Location index located at the checked in AnrLocationData.csv
/// provided by ANR
/// </summary>
internal static class AnrCsvLocationSet
{
    private static readonly string CsvPath = Path.GetFullPath("../../../AnrLocationData.csv");

    private static readonly Lazy<HashSet<string>> _names = new(() =>
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(CsvPath))
            return set;

        using var sr = new StreamReader(CsvPath);
        string? line;
        bool inData = false;

        while ((line = sr.ReadLine()) != null)
        {
            if (!inData)
            {
                if (line.Contains("Location Name", StringComparison.OrdinalIgnoreCase))
                    inData = true;
                continue;
            }

            var cells = line.Split(',');
            if (cells.Length == 0) continue;

            var name = cells[0].Trim();
            if (!string.IsNullOrWhiteSpace(name))
                set.Add(name);
        }

        return set;
    });

    public static IReadOnlyCollection<string> Names => _names.Value;
}
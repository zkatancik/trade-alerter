using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TradeAlerter.Domain.Utilities;

namespace TradeAlerter.Domain.Models;

public class AnrNotice : INotice
{
    private static readonly string[] EventMarkers =
    [
        "CAPACITY REDUCTION",
        "FORCE MAJEURE",
        "CURTAILMENT",
        "OUTAGE",
        "EMERGENCY",
        "MAINTENANCE",
        "OPERATIONAL ALERT",
        "CONSTRAINT"
    ];

    private ILogger? _logger;
    public int Id { get; set; }
    public Pipeline Pipeline { get; set; }
    public NoticeType Type { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string FullText { get; set; } = string.Empty;
    public string Location { get; set; } = "Unknown";
    public DateTimeOffset TimeStamp { get; set; }
    public decimal? CurtailmentVolumeDth { get; set; }
    public Uri Link { get; set; } = null!;

    // ANR-specific trading signal detection based on business requirements
    public bool IsRelevant
    {
        get
        {
            // Check if notice is within the last 3 days
            if (TimeStamp > DateTimeOffset.Now.AddDays(-3))
                return true;

            // Check for critical, maintenance or planned outage types
            if (Type == NoticeType.Critical || Type == NoticeType.PlannedOutage || Type == NoticeType.Maintenance)
                return true;

            // Check for curtailment volume in the notice
            var hasCurtailmentVolume = CurtailmentVolumeDth.HasValue && CurtailmentVolumeDth.Value > 0;
            if (hasCurtailmentVolume)
                return true;

            // Check for relevant keywords in title/summary or full text
            var textToCheck = $"{Summary} {FullText}".ToLowerInvariant();
            var tradingSignalKeywords = new[]
            {
                "force majeure",
                "outage",
                "curtailment",
            };

            var hasRelevantKeywords = tradingSignalKeywords.Any(keyword =>
                textToCheck.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (hasRelevantKeywords)
                return true;

            // ANR-specific: https://en.wikipedia.org/wiki/Henry_Hub
            var locationMentions = new[]
            {
                "louisiana",
                "zone 1", // per AnrLocationData.csv for Louisiana
                "acadian",
                "columbia gulf transmission",
                "gulf south",
                "bridgeline",
                "ngpl",
                "sea robin",
                "southern natural",
                "texas gas",
                "transcontinental",
                "trunkline",
                "jefferson island",
                "sabine"
            };

            var hasLocationMatch = locationMentions.Any(location =>
                textToCheck.Contains(location, StringComparison.OrdinalIgnoreCase) ||
                Location.Contains(location, StringComparison.OrdinalIgnoreCase));

            // can be clever here and return this bool since it's the last check
            return hasLocationMatch;
        }
    }

    /// <summary>
    /// ANR-specific parsing: Populates Location and CurtailmentVolumeDth from the FullText property.
    /// Should be called after FullText is populated.
    /// </summary>
    public void ParseTextDetails(ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(FullText) && string.IsNullOrWhiteSpace(Summary))
            return;

        _logger = logger;
        Location = ParseLocation();
        CurtailmentVolumeDth = ParseCurtailmentVolume();
    }

    /// <summary>
    /// ANR-specific: Extracts location information from the FullText in order of most likely to parse.
    /// </summary>
    private string ParseLocation()
    {
        _logger?.LogTrace("Starting ANR location parsing for Notice ID: {NoticeId}", Id);

        var result = "Unknown";

        // Headlines like "CAPACITY REDUCTION Southeast Mainline – Cottage Grove Southbound"
        // Also handles "LIFTED: CAPACITY REDUCTION..." and "UPDATED: CAPACITY REDUCTION..."
        _logger?.LogTrace("Attempting to match event marker patterns");
        foreach (var marker in EventMarkers)
        {
            // Pattern handles optional LIFTED:/UPDATED: prefixes and various suffix patterns
            var pattern =
                $@"(?:(?:LIFTED|UPDATED):\s+)?{Regex.Escape(marker)}[\s:\-–—]+(.+?)(?:\s*\((?:Posted|Updated|Effective|Supersede|Lifted)[:)]|$|\r?\n|\.|\s+\d{{1,2}}/\d{{1,2}}/\d{{4}})";
            var match = Regex.Match(FullText, pattern, RegexOptions.IgnoreCase);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                result = CleanLocation(match.Groups[1].Value);
                _logger?.LogTrace("Found event marker location: '{Location}' using marker '{Marker}'",
                    result, marker);
                goto ParseComplete;
            }
        }

        // Special case for "NOTICE OF FORCE MAJEURE" patterns
        _logger?.LogTrace("Attempting to match Force Majeure patterns");
        var forceMajeurePattern =
            @"(?:(?:LIFTED|UPDATED):\s+)?NOTICE\s+OF\s+FORCE\s+MAJEURE[\s:\-–—]+(.+?)(?:\s*\((?:Posted|Updated|Effective|Supersede|Lifted)[:)]|$|\r?\n|\.|\s+\d{1,2}/\d{1,2}/\d{4})";
        var forceMajeureMatch = Regex.Match(FullText, forceMajeurePattern, RegexOptions.IgnoreCase);
        if (forceMajeureMatch.Success && !string.IsNullOrWhiteSpace(forceMajeureMatch.Groups[1].Value))
        {
            result = CleanLocation(forceMajeureMatch.Groups[1].Value);
            _logger?.LogTrace("Found Force Majeure location: '{Location}'", result);
            goto ParseComplete;
        }

        _logger?.LogTrace("No event marker patterns matched");

        // Match against the ANR location names in ANR's provided CSV
        _logger?.LogTrace("Attempting to match against ANR CSV location names");
        foreach (var loc in AnrCsvLocationSet.Names)
        {
            var pattern = $@"\b{Regex.Escape(loc)}\b";
            if (Regex.IsMatch(FullText, pattern, RegexOptions.IgnoreCase))
            {
                result = loc;
                _logger?.LogTrace("Found ANR CSV location match: '{Location}' in text: '{TextSnippet}'",
                    loc, FullText.Length > 100 ? FullText.Substring(0, 100) + "..." : FullText);
                goto ParseComplete;
            }
        }

        _logger?.LogTrace("No ANR CSV location matches found");

        // Segment / Zone / Station tokens
        _logger?.LogTrace("Attempting to match structured patterns (Segment/Zone/Point/Station/Location)");
        var structuredPatterns = new[]
        {
            @"Segment\s+\d+",
            @"Zone\s+\d+",
            @"Point\s+\d+",
            @"Station\s+[A-Za-z0-9\-]+",
            @"Location\s+[A-Za-z0-9\-]+"
        };
        foreach (var pattern in structuredPatterns)
        {
            var match = Regex.Match(FullText, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                result = match.Value.Trim();
                _logger?.LogTrace("Found structured pattern location: '{Location}' using pattern '{Pattern}'",
                    result, pattern);
                goto ParseComplete;
            }
        }

        _logger?.LogTrace("No structured patterns matched");

        ParseComplete:
        if (result == "Unknown")
        {
            // womp womp
            _logger?.LogInformation("Failed to parse location from ANR Notice ID: {NoticeId}. Summary: '{Summary}'",
                Id, Summary);
        }
        else
        {
            _logger?.LogTrace("Successfully parsed location: '{Location}' for ANR Notice ID: {NoticeId}", result, Id);
        }

        return result;
    }

    private string CleanLocation(string raw)
    {
        // Return "Unknown" if this looks like an email address or contact info
        if (Regex.IsMatch(raw, @"@\w+\.\w+|email|contact|phone|\d{3}[-.\s]?\d{3}[-.\s]?\d{4}", RegexOptions.IgnoreCase))
        {
            return "Unknown";
        }

        // Return "Unknown" if this looks like administrative/service content
        if (Regex.IsMatch(raw,
                @"(after.?hours|on.?call|hotline|customer\s+service|commercial\s+services|contacts?|marketing|contracts?|security)",
                RegexOptions.IgnoreCase))
        {
            return "Unknown";
        }

        // Strip trailing boiler-plate words & punctuation
        raw = Regex.Replace(raw,
            @"\s*(scheduled|begins|ends|effective|posted|please|customers?|for\s+\d{1,2}/\d{1,2}/\d{4}).*?$", "",
            RegexOptions.IgnoreCase);

        // Remove common non-location patterns
        raw = Regex.Replace(raw, @"\s*(maintenance|pipe|gas|control|noms|scheduling):\s*.*$", "",
            RegexOptions.IgnoreCase);

        var result = raw.Trim(' ', '-', ':', ',', '.');

        // Return "Unknown" if result is too short or looks like a date/time
        if (result.Length < 3 ||
            Regex.IsMatch(result, @"^\d{1,2}/\d{1,2}/\d{4}|\d{1,2}:\d{2}$", RegexOptions.IgnoreCase))
        {
            return "Unknown";
        }

        return result;
    }

    /// <summary>
    /// ANR-specific: Extracts curtailment volume from the FullText.
    /// </summary>
    private decimal? ParseCurtailmentVolume()
    {
        if (string.IsNullOrWhiteSpace(FullText))
            return null;

        _logger?.LogTrace("Starting ANR curtailment volume parsing for Notice ID: {NoticeId}", Id);

        decimal? result = null;

        // "X MMcf/d (leaving Y MMcf/d)" - we want X (the curtailment amount)
        _logger?.LogTrace("Attempting to match curtailment pattern");
        var curtailmentPattern = @"(\d+(?:\.\d+)?)\s*MMcf/d\s*\(leaving\s+\d+(?:\.\d+)?\s*MMcf/d\)";
        var curtailmentMatch = Regex.Match(FullText, curtailmentPattern, RegexOptions.IgnoreCase);
        if (curtailmentMatch.Success && decimal.TryParse(curtailmentMatch.Groups[1].Value, out var curtailmentVolume))
        {
            result = curtailmentVolume;
            _logger?.LogTrace("Found curtailment pattern volume: {Volume} MMcf/d", curtailmentVolume);
            goto ParseComplete;
        }

        // "capped at X MMcf/d" or "restricted to X MMcf/d" - X is the current allowed flow
        _logger?.LogTrace("Attempting to match capped/restricted pattern");
        var cappedPattern = @"(?:capped|restricted)\s+(?:at|to)\s+(\d+(?:\.\d+)?)\s*MMcf/d";
        var cappedMatch = Regex.Match(FullText, cappedPattern, RegexOptions.IgnoreCase);
        if (cappedMatch.Success && decimal.TryParse(cappedMatch.Groups[1].Value, out var cappedVolume))
        {
            result = cappedVolume;
            _logger?.LogTrace("Found capped pattern volume: {Volume} MMcf/d", cappedVolume);
            goto ParseComplete;
        }

        // General "X MMcf/d" but exclude reduction amounts and remaining capacity
        _logger?.LogTrace("Attempting to match general MMcf/d pattern");
        var generalPattern = @"(\d+(?:\.\d+)?)\s*MMcf/d";
        var generalMatches = Regex.Matches(FullText, generalPattern, RegexOptions.IgnoreCase);
        foreach (Match match in generalMatches)
        {
            // Get the context around the match to check for remaining capacity or reduction amounts
            var context = FullText.Substring(Math.Max(0, match.Index - 20),
                Math.Min(FullText.Length - Math.Max(0, match.Index - 20), match.Length + 40));

            // Skip if this is part of a "leaving X MMcf/d" pattern (remaining capacity)
            if (Regex.IsMatch(context, @"leaving\s+\d+(?:\.\d+)?\s*MMcf/d", RegexOptions.IgnoreCase))
                continue;

            // Skip if this is part of a "reduced by X MMcf/d" pattern (reduction amount, not current flow)
            if (Regex.IsMatch(context, @"(?:reduced\s+by|reduction\s+of)\s+\d+(?:\.\d+)?\s*MMcf/d",
                    RegexOptions.IgnoreCase))
                continue;

            // If we get here, we have a valid MMcf/d volume
            if (decimal.TryParse(match.Groups[1].Value, out var generalVolume))
            {
                result = generalVolume;
                _logger?.LogTrace("Found general pattern volume: {Volume} MMcf/d", generalVolume);
                goto ParseComplete;
            }
        }

        _logger?.LogTrace("No MMcf/d patterns matched");

        ParseComplete:
        if (result == null)
        {
            _logger?.LogTrace("No curtailment volume found for ANR Notice ID: {NoticeId}", Id);
            return null;
        }

        // Convert MMcf/d to MMBtu/d (approximation for natural gas)
        // 1 MMcf = 1.038 MMBtu for typical pipeline-quality natural gas.
        // https://www.eia.gov/tools/faqs/faq.php?id=45&t=8
        var volumeInMmbtu = result.Value * 1.038m;

        _logger?.LogTrace(
            "Successfully parsed curtailment volume: {Volume} MMcf/d ({VolumeMmbtu} MMbtu/d) for ANR Notice ID: {NoticeId}",
            result.Value, volumeInMmbtu, Id);

        return volumeInMmbtu;
    }
}
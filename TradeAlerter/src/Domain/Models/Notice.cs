using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TradeAlerter.Domain.Utilities;

namespace TradeAlerter.Domain.Models;

public class Notice
{
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

    public bool IsRelevant =>
        Type == NoticeType.Critical
        || TimeStamp >= DateTimeOffset.UtcNow.AddDays(-3)
        || Regex.IsMatch(Summary, "(force majeure|outage|curtailment)", RegexOptions.IgnoreCase)
        || Regex.IsMatch(Location, "(louisiana|henry hub)", RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses and populates Location and CurtailmentVolumeDth from the FullText property.
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

    // Common event markers used in ANR & other EBBs
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

    /// <summary>
    /// Extracts location information from the FullText and Summary.
    /// </summary>
    private string ParseLocation()
    {
        _logger?.LogTrace("Starting location parsing for Notice ID: {NoticeId}", Id);

        var result = "Unknown";
        var sources = new[] { Summary, FullText };

        foreach (var text in sources)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger?.LogTrace("Skipping empty text source");
                continue;
            }
            
            // Match against the ANR location names in ANR's provided CSV
            _logger?.LogTrace("Attempting to match against ANR CSV location names");
            foreach (var loc in AnrCsvLocationSet.Names)
            {
                var pattern = $@"\b{Regex.Escape(loc)}\b";
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                {
                    result = loc;
                    _logger?.LogTrace("Found ANR CSV location match: '{Location}' in text: '{TextSnippet}'",
                        loc, text.Length > 100 ? text.Substring(0, 100) + "..." : text);
                    goto ParseComplete;
                }
            }
        }

        _logger?.LogTrace("No ANR CSV location matches found");

        // Headlines like "CAPACITY REDUCTION Southeast Mainline – Cottage Grove Southbound"
        _logger?.LogTrace("Attempting to match event marker patterns");
        foreach (var marker in EventMarkers)
        {
            var pattern = $@"{Regex.Escape(marker)}[\s:\-–—]+(.+?)(\r?\n|$|\.)";
            var match = Regex.Match(FullText, pattern, RegexOptions.IgnoreCase);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                result = CleanLocation(match.Groups[1].Value);
                _logger?.LogTrace("Found event marker location: '{Location}' using marker '{Marker}'",
                    result, marker);
                goto ParseComplete;
            }
        }

        _logger?.LogTrace("No event marker patterns matched");

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
            _logger?.LogInformation("Failed to parse location from Notice ID: {NoticeId}. Summary: '{Summary}'",
                Id, Summary);
        }
        else
        {
            _logger?.LogTrace("Successfully parsed location: '{Location}' for Notice ID: {NoticeId}", result, Id);
        }

        return result;
    }

    private static string CleanLocation(string raw)
    {
        // Strip trailing boiler-plate words & punctuation
        raw = Regex.Replace(raw, @"\s*(scheduled|begins|ends|effective).*?$", "",
            RegexOptions.IgnoreCase);
        
        return raw.Trim(' ', '-', ':');
    }

    /// <summary>
    /// Extracts curtailment volume from the FullText.
    /// </summary>
    private decimal? ParseCurtailmentVolume()
    {
        if (string.IsNullOrWhiteSpace(FullText))
            return null;

        return null;
    }
}
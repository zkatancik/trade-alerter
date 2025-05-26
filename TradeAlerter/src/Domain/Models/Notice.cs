using System.Text.RegularExpressions;

namespace TradeAlerter.Domain.Models;

public class Notice
{
    public int Id { get; set; }
    public Pipeline Pipeline { get; set; }
    public NoticeType Type { get; set; }
    public string Summary { get; set; }
    public string Location { get; set; }
    public DateTimeOffset TimeStamp { get; set; }
    public decimal? CurtailmentVolumeDth { get; set; }
    public Uri Link { get; set; }
    
    public bool IsRelevant => 
        Type == NoticeType.Critical
        || TimeStamp >= DateTimeOffset.UtcNow.AddDays(-3)
        || Regex.IsMatch(Summary, "(force majeure|outage|curtailment)", RegexOptions.IgnoreCase)
        || Regex.IsMatch(Location, "(louisiana|henry hub)", RegexOptions.IgnoreCase);
}
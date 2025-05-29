namespace TradeAlerter.Domain.Models;

public interface INotice
{
    int Id { get; set; }
    Pipeline Pipeline { get; set; }
    NoticeType Type { get; set; }
    string Summary { get; set; }
    string Location { get; set; }
    DateTimeOffset TimeStamp { get; set; }
    decimal? CurtailmentVolumeDth { get; set; }
    Uri Link { get; set; }
    
    /// <summary>
    /// Determines if this notice is relevant for trading signals.
    /// Implementation will vary by scraper/pipeline.
    /// </summary>
    bool IsRelevant { get; }
} 
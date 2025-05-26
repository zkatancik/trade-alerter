using TradeAlerter.Domain.Models;

namespace TradeAlerter.Domain.Scraping;

public interface IScraper
{
    Task<IReadOnlyList<Notice>> FetchNoticeAsync();
}
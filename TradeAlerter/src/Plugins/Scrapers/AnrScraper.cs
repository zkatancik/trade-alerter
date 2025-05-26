using TradeAlerter.Domain.Models;
using TradeAlerter.Domain.Scraping;

namespace TradeAlerter.Plugins.Scrapers;

public class AnrScraper : IScraper
{
    /// <summary>
    /// Scrapes the search-results table at
    /// https://ebb.anrpl.com/Notices/NoticesSearch.asp?sPipelineCode=ANR.
    /// It POSTs the same form the UI issues, then follows the
    /// per-row detail link to extract curtailment volume, location and other details
    /// unvailable in just the table row.
    /// </summary>
    public Task<IReadOnlyList<Notice>> FetchNoticeAsync()
    {
        throw new NotImplementedException();
    }
}
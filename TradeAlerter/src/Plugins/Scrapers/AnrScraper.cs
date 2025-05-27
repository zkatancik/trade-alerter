using TradeAlerter.Domain.Models;
using TradeAlerter.Domain.Scraping;
using Microsoft.Extensions.Logging;
using HtmlAgilityPack;

namespace TradeAlerter.Plugins.Scrapers;

public sealed class AnrOptions
{
    public Uri BaseUrl { get; init; } = new("https://ebb.anrpl.com");
    public int LookbackDays { get; init; } = 31;
}
public class AnrScraper : IScraper
{
    private static readonly Dictionary<string, NoticeType> _noticeTypeMap =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["Auction Notice"]               = NoticeType.Critical,
        ["Billing and Payment"]          = NoticeType.Informational,
        ["Cap Rel"]                      = NoticeType.Informational,
        ["Cash Out"]                     = NoticeType.Critical,
        ["Computer Stat"]                = NoticeType.Informational,
        ["Constraint"]                   = NoticeType.Critical,
        ["Critical period"]              = NoticeType.Critical,
        ["Emergency Deviations"]         = NoticeType.Critical,
        ["Force Maj"]                    = NoticeType.Critical,
        ["Gas Qual"]                     = NoticeType.Informational,
        ["Imbal Trade"]                  = NoticeType.Informational,
        ["Information Disclosures"]      = NoticeType.Informational,
        ["Maint"]                        = NoticeType.Maintenance,
        ["Message"]                      = NoticeType.Informational,
        ["News"]                         = NoticeType.Informational,
        ["OFO"]                          = NoticeType.Informational,
        ["Oper Alert"]                   = NoticeType.Critical,
        ["Other"]                        = NoticeType.Informational,
        ["Ovr-Undr Perf"]                = NoticeType.Informational,
        ["Phone"]                        = NoticeType.Informational,
        ["Plnd Outage"]                  = NoticeType.PlannedOutage,
        ["Rates - Chgs"]                 = NoticeType.Informational,
        ["Sched Alert"]                  = NoticeType.Critical,
        ["Storage"]                      = NoticeType.Critical,
        ["Tariff Discretionary Actions"] = NoticeType.Informational,
        ["TSP Cap Offer"]                = NoticeType.Informational,
        ["Voluntary Consent"]            = NoticeType.Informational,
        ["Waste Heat"]                   = NoticeType.WasteHeat,
        ["Weather"]                      = NoticeType.Informational
    };
    
    private readonly HttpClient _httpClient;
    private readonly ILogger<AnrScraper> _logger;
    private readonly AnrOptions _options;
    
    public AnrScraper(AnrOptions options, HttpClient httpClient, ILogger<AnrScraper> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options;
    }
    
    /// <summary>
    /// Scrapes the search-results table at
    /// https://ebb.anrpl.com/Notices/NoticesSearch.asp?sPipelineCode=ANR.
    /// It POSTs the same form the UI issues, then follows the
    /// per-row detail link to extract curtailment volume, location and other details
    /// unvailable in just the table row.
    /// </summary>
    public async Task<IReadOnlyList<Notice>> FetchNoticeAsync()
    {
        try
        {
            var html = await _httpClient.GetStringAsync(
                $"{_options.BaseUrl}/Notices/NoticesSearch.asp?sPipelineCode=ANR");
            
            var doc  = new HtmlDocument(); 
            doc.LoadHtml(html);
            var rows = doc.DocumentNode.SelectNodes(
                           "//tr[td/a[contains(@href,'NoticeView')]]") 
                       ?? Enumerable.Empty<HtmlNode>();
            
            var notices = new List<Notice>();

            foreach (var row in rows)
            {
                Console.WriteLine(row.InnerHtml);
            }
            
            return notices;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch notices from ANR pipeline");
            throw;
        }
    }
}
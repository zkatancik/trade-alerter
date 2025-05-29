using TradeAlerter.Domain.Models;
using TradeAlerter.Domain.Scraping;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HtmlAgilityPack;

namespace TradeAlerter.Plugins.Scrapers;

public sealed class AnrOptions
{
    public Uri BaseUrl { get; set; } = new("https://ebb.anrpl.com/Notices/");
    public int LookbackDays { get; set; } = 31;
}

public class AnrScraper : IScraper
{
    private readonly AnrOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AnrScraper> _logger;

    private static readonly Dictionary<string, NoticeType> NoticeTypeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Auction Notice"] = NoticeType.Critical,
            ["Billing and Payment"] = NoticeType.Informational,
            ["Cap Rel"] = NoticeType.Informational,
            ["Cash Out"] = NoticeType.Critical,
            ["Computer Stat"] = NoticeType.Informational,
            ["Constraint"] = NoticeType.Critical,
            ["Critical period"] = NoticeType.Critical,
            ["Cust Srvc"] = NoticeType.Informational,
            ["Emergency Deviations"] = NoticeType.Critical,
            ["Force Maj"] = NoticeType.Critical,
            ["Gas Qual"] = NoticeType.Informational,
            ["Imbal Trade"] = NoticeType.Informational,
            ["Information Disclosures"] = NoticeType.Informational,
            ["Maint"] = NoticeType.Maintenance,
            ["Message"] = NoticeType.Informational,
            ["News"] = NoticeType.Informational,
            ["OFO"] = NoticeType.Informational,
            ["Oper Alert"] = NoticeType.Critical,
            ["Other"] = NoticeType.Informational,
            ["Ovr-Undr Perf"] = NoticeType.Informational,
            ["Phone"] = NoticeType.Informational,
            ["Plnd Outage"] = NoticeType.PlannedOutage,
            ["Rates - Chgs"] = NoticeType.Informational,
            ["Sched Alert"] = NoticeType.Critical,
            ["Storage"] = NoticeType.Critical,
            ["Tariff Discretionary Actions"] = NoticeType.Informational,
            ["TSP Cap Offer"] = NoticeType.Informational,
            ["Voluntary Consent"] = NoticeType.Informational,
            ["Waste Heat"] = NoticeType.WasteHeat,
            ["Weather"] = NoticeType.Informational
        };

    public AnrScraper(IOptions<AnrOptions> options, HttpClient httpClient, ILogger<AnrScraper> logger)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Scrapes the search-results table at
    /// https://ebb.anrpl.com/Notices/NoticesSearch.asp?sPipelineCode=ANR.
    /// It POSTs the same form the UI issues, then follows the
    /// per-row detail link to extract curtailment volume, location and other details
    /// unavailable in just the table row.
    /// </summary>
    public async Task<IReadOnlyList<INotice>> FetchNoticeAsync()
    {
        try
        {
            // Calculate the "from" date based on LookbackDays
            var fromDate = DateTime.Now.AddDays(-_options.LookbackDays);
            var fromDateString = fromDate.ToString("MM/dd/yyyy");

            var formData = new List<KeyValuePair<string, string>>
            {
                new("frmQueryMode", "1"),
                new("frmNoticeType", ""),
                new("frmNoticeInd", "ALL"),
                new("frmNoticeID", "<ALL>"),
                new("frmNoticeStartDate", fromDateString),
                new("frmNoticeEndDate", "<ALL>"),
                new("frmSubject", "<ALL>"),
                new("B1", "Retrieve")
            };

            var formContent = new FormUrlEncodedContent(formData);

            // POST to the search endpoint
            var response = await _httpClient.PostAsync(
                $"{_options.BaseUrl}NoticesSearch.asp?sPipelineCode=ANR",
                formContent);

            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var rows = doc.DocumentNode.SelectNodes(
                           "//tr[td//a[contains(@href,'NoticeView')]]")
                       ?? Enumerable.Empty<HtmlNode>();

            var notices = new List<INotice>();

            foreach (var row in rows)
            {
                // Extract the detail link href
                var detailLinkNode = row.SelectSingleNode(".//a[contains(@href,'NoticeView')]");
                if (detailLinkNode != null)
                {
                    var href = detailLinkNode.GetAttributeValue("href", "");
                    var detailUrl = new Uri(_options.BaseUrl, href);
                    if (!string.IsNullOrEmpty(href))
                    {
                        var notice = new AnrNotice
                        {
                            Pipeline = Pipeline.ANR,
                            Link = detailUrl
                        };

                        // Fetch the details for this notice by going to it's notice page
                        // ie: https://ebb.anrpl.com/Notices/NoticeView.asp?sPipelineCode=ANR&sSubCategory=ALL&sNoticeId=12372
                        var noticeDoc = await FetchNoticeDetailsAsync(detailUrl);
                        if (noticeDoc != null)
                        {
                            try
                            {
                                notice.Id = int.Parse(ExtractCellValue(noticeDoc, "Notice ID") ?? "-1");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to parse notice ID from details page.");
                            }

                            notice.Type = ParseNoticeTypeFromDetails(noticeDoc);
                            notice.Summary = ExtractCellValue(noticeDoc, "Subject") ?? string.Empty;
                            notice.FullText = ExtractCellValue(noticeDoc, "Notice Text") ?? string.Empty;
                            notice.ParseTextDetails(_logger);

                            try
                            {
                                notice.TimeStamp =
                                    DateTimeOffset.Parse(ExtractCellValue(noticeDoc, "Posting Date/Time") ??
                                                         "1/1/1900");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to parse posting date/time from details page.");
                            }
                        }

                        notices.Add(notice);
                    }
                    else
                    {
                        _logger.LogError("Failed to extract notice details link from row: {row}", row);
                    }
                }
                else
                {
                    _logger.LogError("Failed to extract href from row: {row}", row);
                }
            }

            return notices;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch notices from ANR pipeline");
            throw;
        }
    }

    /// <summary>
    /// Fetches detailed information for a specific notice by visiting its detail page.
    /// </summary>
    /// <param name="detailsUrl">The URL to the notice details page</param>
    /// <returns>The parsed HTML document of the notice details page</returns>
    private async Task<HtmlDocument?> FetchNoticeDetailsAsync(Uri detailsUrl)
    {
        var detailUrlString = detailsUrl.ToString();
        try
        {
            _logger.LogTrace("Fetching notice details from: {DetailUrlString}", detailUrlString);

            var response = await _httpClient.GetAsync(detailUrlString);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            _logger.LogTrace("Successfully fetched details from: {DetailUrlString}", detailUrlString);

            return doc;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to fetch details from: {detailUrlString}");
            return null;
        }
    }

    /// <summary>
    /// Generic helper method to extract the text from the details page table.
    /// </summary>
    /// <param name="doc">The details HTML document to search</param>
    /// <param name="category">The category text to look for in the first cell (e.g., "Notice Type Desc", "Posting Date/Time")</param>
    /// <returns>The trimmed text content of the second cell, or null if not found</returns>
    private string? ExtractCellValue(HtmlDocument doc, string category)
    {
        try
        {
            var row = doc.DocumentNode
                .SelectSingleNode($"//tr[td[1][contains(., '{category}')]]");

            var cell = row?.SelectSingleNode("td[2]");
            var value = cell?.InnerText.Trim();

            if (value == null)
            {
                _logger.LogInformation("Extracted null value for {Category} from details page.", category);
            }

            _logger.LogTrace("Extracted value for {Category}: {Value}", category, value);
            return value;
        }
        catch
        {
            _logger.LogWarning("Unable to extract value for {Category} from details page.", category);
            return null;
        }
    }

    /// <summary>
    /// Parses the Notice Type from the notice details HTML document.
    /// </summary>
    /// <param name="doc">The HTML document containing the notice details</param>
    /// <returns>The parsed NoticeType, or NoticeType.Informational as default if not found or not mapped</returns>
    private NoticeType ParseNoticeTypeFromDetails(HtmlDocument doc)
    {
        var result = NoticeType.Informational; // Default value

        try
        {
            var noticeTypeText = ExtractCellValue(doc, "Notice Type Desc");
            if (!string.IsNullOrEmpty(noticeTypeText))
            {
                _logger.LogTrace("Found notice type: {NoticeType}", noticeTypeText);

                if (NoticeTypeMap.TryGetValue(noticeTypeText, out var noticeType))
                {
                    result = noticeType;
                }
                else
                {
                    _logger.LogWarning("Unknown notice type: {NoticeType}. Defaulting to Informational.",
                        noticeTypeText);
                }
            }
            else
            {
                _logger.LogError("Could not find Notice Type Desc in the HTML document. Defaulting to Informational.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing notice type from HTML document. Defaulting to Informational.");
        }

        return result;
    }
}
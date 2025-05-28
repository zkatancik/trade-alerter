using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using TradeAlerter.Plugins.Scrapers;
using System;
using System.IO;

namespace TradeAlerter.Host;

public class Program
{
    public static void Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.AddConfiguration(configuration.GetSection("Logging"));
        });

        var logger = loggerFactory.CreateLogger<AnrScraper>();
        var httpClient = new HttpClient();

        var anrOptions = new AnrOptions
        {
            BaseUrl = new Uri(configuration["Pipelines:ANR:BaseUrl"] ?? "https://ebb.anrpl.com"),
            LookbackDays = int.TryParse(configuration["Pipelines:ANR:LookbackDays"], out int days) ? days : 31
        };

        var scraper = new AnrScraper(anrOptions, httpClient, logger);
        scraper.FetchNoticeAsync().Wait();
    }
}
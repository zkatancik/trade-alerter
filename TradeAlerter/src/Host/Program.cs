using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradeAlerter.Plugins.Scrapers;
using TradeAlerter.Domain.Scraping;
using System.Diagnostics;

namespace TradeAlerter.Host;

public class Program
{
    public static async Task Main(string[] args)
    {
        // adding an example of adding another scraper in the DI framework
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);

        builder.Services.AddTradeAlerterServices(builder.Configuration);

        var host = builder.Build();

        var anrScraper = host.Services.GetRequiredKeyedService<IScraper>("ANR");
        //var otherScraper = host.Services.GetRequiredKeyedService<IScraper>("Other");

        var anrNotices = await anrScraper.FetchNoticeAsync();
        // var otherNotices = await otherScraper.FetchNoticeAsync();

        Debugger.Break();
    }
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTradeAlerterServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AnrOptions>(options =>
        {
            options.BaseUrl = new Uri(configuration["Pipelines:ANR:BaseUrl"] ?? "https://ebb.anrpl.com/Notices/");
            options.LookbackDays = int.TryParse(configuration["Pipelines:ANR:LookbackDays"], out int days) ? days : 31;
        });

        services.AddHttpClient<AnrScraper>();
        // services.AddHttpClient<OtherScraper>();

        services.AddKeyedTransient<IScraper, AnrScraper>("ANR");
        //services.AddKeyedTransient<IScraper, OtherScraper>("Other");

        return services;
    }
}
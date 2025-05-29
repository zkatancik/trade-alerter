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
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);

        builder.Services.AddTradeAlerterServices(builder.Configuration);

        var host = builder.Build();

        var scraper = host.Services.GetRequiredService<IScraper>();
        var notices = await scraper.FetchNoticeAsync();

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

        services.AddTransient<IScraper, AnrScraper>();

        return services;
    }
}
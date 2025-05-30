using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradeAlerter.Plugins.Scrapers;
using TradeAlerter.Domain.Scraping;
using TradeAlerter.Domain.Notification;
using TradeAlerter.Plugins.Notifiers;
using System.Diagnostics;
using DotNetEnv;

namespace TradeAlerter.Host;

public class Program
{
    public static async Task Main(string[] args)
    {
        Env.Load();
        
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);

        builder.Services.AddTradeAlerterServices(builder.Configuration);

        var host = builder.Build();

        var anrScraper = host.Services.GetRequiredKeyedService<IScraper>("ANR");
        var anrNotices = await anrScraper.FetchNoticeAsync();

        var emailNotifier = host.Services.GetRequiredService<INotifier>();
        var relevantNotices = anrNotices.Where(n => n.IsRelevant).ToList();
        
        if (relevantNotices.Any())
        {
            await emailNotifier.NotifyAsync(relevantNotices);
            Console.WriteLine($"Sent email notification for {relevantNotices.Count} relevant notice(s).");
        }
        else
        {
            Console.WriteLine("No relevant notices found.");
        }

        Debugger.Break();
    }
}

public static class ServiceCollectionExtensions
{
    public static void AddTradeAlerterServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AnrOptions>(options =>
        {
            options.BaseUrl = new Uri(configuration["Pipelines:ANR:BaseUrl"] ?? "https://ebb.anrpl.com/Notices/");
            options.LookbackDays = int.TryParse(configuration["Pipelines:ANR:LookbackDays"], out int days) ? days : 31;
        });

        services.Configure<EmailOptions>(options =>
        {
            configuration.GetSection("Email").Bind(options);
            
            var username = Environment.GetEnvironmentVariable("EMAIL_USERNAME");
            var password = Environment.GetEnvironmentVariable("EMAIL_PASSWORD");
            
            if (!string.IsNullOrEmpty(username))
                options.Username = username;
            
            if (!string.IsNullOrEmpty(password))
                options.Password = password;
        });

        services.AddHttpClient<AnrScraper>();
        services.AddKeyedTransient<IScraper, AnrScraper>("ANR");
        services.AddTransient<INotifier, EmailNotifier>();
    }
}
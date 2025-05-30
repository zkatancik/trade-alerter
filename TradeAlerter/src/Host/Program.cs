using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradeAlerter.Plugins.Scrapers;
using TradeAlerter.Domain.Scraping;
using TradeAlerter.Domain.Notification;
using TradeAlerter.Plugins.Notifiers;
using System.Diagnostics;
using DotNetEnv;
using Microsoft.Extensions.Options;

namespace TradeAlerter.Host;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Grabbing Configuration.");
        Env.Load();
        
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);

        builder.Services.AddTradeAlerterServices(builder.Configuration);

        var host = builder.Build();

        var anrScraper = host.Services.GetRequiredKeyedService<IScraper>("ANR");
        
        Console.Write("Checking ANR Pipeline for new alerts");
        
        var scrapingTask = anrScraper.FetchNoticeAsync();
        while (!scrapingTask.IsCompleted)
        {
            Console.Write(".");
            await Task.Delay(500);
        }
        
        var anrNotices = await scrapingTask;
        Console.WriteLine();
        Console.WriteLine($"Found {anrNotices.Count} ANR Pipeline notices.");
        
        var emailNotifier = host.Services.GetRequiredService<INotifier>();
        var relevantNotices = anrNotices.Where(n => n.IsRelevant).ToList();
        
        Console.WriteLine($"{relevantNotices.Count} trading signal(s) detected.");
        
        if (relevantNotices.Any())
        {
            // Get email configuration to console log toEmail
            var emailOptions = host.Services.GetRequiredService<IOptions<EmailOptions>>().Value;
            
            await emailNotifier.NotifyAsync(relevantNotices);
            Console.WriteLine($"Sent email to: {emailOptions.ToEmail}");
        }
        else
        {
            Console.WriteLine("No email sent - no relevant notices found.");
        }
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
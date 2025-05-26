using TradeAlerter.Domain.Models;
using TradeAlerter.Domain.Notification;

namespace TradeAlerter.Plugins.Notifiers;

public class EmailNotifier : INotifier
{
    public Task NotifyAsync(Notice notice)
    {
        throw new NotImplementedException();
    }
}
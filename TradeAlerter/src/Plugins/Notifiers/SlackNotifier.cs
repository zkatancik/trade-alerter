using TradeAlerter.Domain.Models;
using TradeAlerter.Domain.Notification;

namespace TradeAlerter.Plugins.Notifiers;

public class SlackNotifier : INotifier
{
    public Task NotifyAsync(INotice notice)
    {
        throw new NotImplementedException();
    }
}
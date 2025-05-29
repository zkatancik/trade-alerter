using TradeAlerter.Domain.Models;

namespace TradeAlerter.Domain.Notification;

public interface INotifier
{
    Task NotifyAsync(INotice notice);
}
using WebhookPublisher.Models;

namespace WebhookPublisher.Services;

public interface IWebhookRegistry
{
    void RegisterSubscription(WebhookSubscription subscription);
    void UnregisterSubscription(string subscriptionId);
    WebhookSubscription? GetSubscription(string subscriptionId);
    IEnumerable<WebhookSubscription> GetSubscriptionsForEvent(string eventType);
    IEnumerable<WebhookSubscription> GetAllSubscriptions();
}

public class WebhookRegistry : IWebhookRegistry
{
    private readonly Dictionary<string, WebhookSubscription> _subscriptions = new();
    private readonly object _lock = new();

    public void RegisterSubscription(WebhookSubscription subscription)
    {
        lock (_lock)
        {
            _subscriptions[subscription.Id] = subscription;
        }
    }

    public void UnregisterSubscription(string subscriptionId)
    {
        lock (_lock)
        {
            _subscriptions.Remove(subscriptionId);
        }
    }

    public WebhookSubscription? GetSubscription(string subscriptionId)
    {
        lock (_lock)
        {
            _subscriptions.TryGetValue(subscriptionId, out var subscription);
            return subscription;
        }
    }

    public IEnumerable<WebhookSubscription> GetSubscriptionsForEvent(string eventType)
    {
        lock (_lock)
        {
            return _subscriptions.Values
                .Where(s => s.IsActive && s.Events.Contains(eventType))
                .ToList();
        }
    }

    public IEnumerable<WebhookSubscription> GetAllSubscriptions()
    {
        lock (_lock)
        {
            return _subscriptions.Values.ToList();
        }
    }
}

using WebhookPublisher.Models;

namespace WebhookPublisher.Services;

public class WebhookPublisherBackgroundJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookPublisherBackgroundJob> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, WebhookEvent> _pendingEvents = new();
    private readonly Dictionary<string, WebhookDelivery> _pendingDeliveries = new();
    private readonly object _lock = new();

    public WebhookPublisherBackgroundJob(IServiceProvider serviceProvider, ILogger<WebhookPublisherBackgroundJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public void EnqueueEvent(WebhookEvent webhookEvent)
    {
        lock (_lock)
        {
            _pendingEvents[webhookEvent.Id] = webhookEvent;
        }
        _logger.LogInformation($"Webhook event enqueued: {webhookEvent.EventType} (ID: {webhookEvent.Id})");
    }

    public WebhookDelivery? GetDeliveryStatus(string deliveryId)
    {
        lock (_lock)
        {
            _pendingDeliveries.TryGetValue(deliveryId, out var delivery);
            return delivery;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Webhook Publisher Background Job started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingEventsAsync(stoppingToken);
                await ProcessPendingDeliveriesAsync(stoppingToken);
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in webhook publisher background job");
            }
        }

        _logger.LogInformation("Webhook Publisher Background Job stopped");
    }

    private async Task ProcessPendingEventsAsync(CancellationToken stoppingToken)
    {
        List<string> processedEventIds = new();

        lock (_lock)
        {
            foreach (var eventId in _pendingEvents.Keys.ToList())
            {
                var webhookEvent = _pendingEvents[eventId];

                using (var scope = _serviceProvider.CreateScope())
                {
                    var registry = scope.ServiceProvider.GetRequiredService<IWebhookRegistry>();
                    var publisherService = scope.ServiceProvider.GetRequiredService<IWebhookPublisherService>();

                    var subscriptions = registry.GetSubscriptionsForEvent(webhookEvent.EventType).ToList();

                    if (subscriptions.Count == 0)
                    {
                        _logger.LogWarning($"No subscriptions found for event type: {webhookEvent.EventType}");
                        processedEventIds.Add(eventId);
                        continue;
                    }

                    foreach (var subscription in subscriptions)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var delivery = await publisherService.SendWebhookAsync(webhookEvent, subscription, stoppingToken);
                                lock (_lock)
                                {
                                    _pendingDeliveries[delivery.Id] = delivery;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error sending webhook for subscription {subscription.Id}");
                            }
                        }, stoppingToken);
                    }
                }

                processedEventIds.Add(eventId);
            }

            foreach (var eventId in processedEventIds)
            {
                _pendingEvents.Remove(eventId);
            }
        }
    }

    private async Task ProcessPendingDeliveriesAsync(CancellationToken stoppingToken)
    {
        List<string> deliveriesToRetry = new();

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            foreach (var (deliveryId, delivery) in _pendingDeliveries.ToList())
            {
                if (delivery.Status == DeliveryStatus.Failed && 
                    delivery.NextRetryAt.HasValue && 
                    delivery.NextRetryAt <= now && 
                    delivery.AttemptCount < delivery.MaxRetries)
                {
                    deliveriesToRetry.Add(deliveryId);
                }
            }
        }

        foreach (var deliveryId in deliveriesToRetry)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    WebhookDelivery? delivery;
                    lock (_lock)
                    {
                        _pendingDeliveries.TryGetValue(deliveryId, out delivery);
                    }

                    if (delivery == null) return;

                    // Reconstruct the webhook event (in a real scenario, you might store this)
                    var webhookEvent = new WebhookEvent
                    {
                        Id = delivery.EventId,
                        EventType = "unknown",
                        Data = null,
                        CreatedAt = DateTime.UtcNow
                    };

                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var publisherService = scope.ServiceProvider.GetRequiredService<IWebhookPublisherService>();
                        var updatedDelivery = await publisherService.RetryWebhookAsync(delivery, webhookEvent, stoppingToken);

                        lock (_lock)
                        {
                            _pendingDeliveries[deliveryId] = updatedDelivery;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error retrying webhook delivery {deliveryId}");
                }
            }, stoppingToken);
        }
    }
}

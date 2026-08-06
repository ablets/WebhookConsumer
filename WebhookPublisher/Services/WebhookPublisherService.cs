using System.Text;
using System.Text.Json;
using WebhookPublisher.Models;

namespace WebhookPublisher.Services;

public interface IWebhookPublisherService
{
    Task<WebhookDelivery> SendWebhookAsync(WebhookEvent webhookEvent, WebhookSubscription subscription, CancellationToken cancellationToken = default);
    Task<WebhookDelivery> RetryWebhookAsync(WebhookDelivery delivery, WebhookEvent webhookEvent, CancellationToken cancellationToken = default);
}

public class WebhookPublisherService : IWebhookPublisherService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookPublisherService> _logger;
    private const int TimeoutSeconds = 10;

    public WebhookPublisherService(HttpClient httpClient, ILogger<WebhookPublisherService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<WebhookDelivery> SendWebhookAsync(WebhookEvent webhookEvent, WebhookSubscription subscription, CancellationToken cancellationToken = default)
    {
        var delivery = new WebhookDelivery
        {
            EventId = webhookEvent.Id,
            SubscriptionId = subscription.Id,
            Url = subscription.Url,
            Status = DeliveryStatus.InProgress,
            LastAttemptAt = DateTime.UtcNow,
            AttemptCount = 1
        };

        try
        {
            var json = JsonSerializer.Serialize(webhookEvent);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            var response = await _httpClient.PostAsync(subscription.Url, content, cts.Token);

            delivery.ResponseStatusCode = (int)response.StatusCode;
            delivery.ResponseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                delivery.Status = DeliveryStatus.Success;
                _logger.LogInformation($"Webhook delivered successfully to {subscription.Url}");
            }
            else
            {
                delivery.Status = DeliveryStatus.Failed;
                delivery.NextRetryAt = DateTime.UtcNow.AddSeconds(GetBackoffSeconds(delivery.AttemptCount));
                _logger.LogWarning($"Webhook delivery failed to {subscription.Url} with status {response.StatusCode}");
            }
        }
        catch (OperationCanceledException)
        {
            delivery.Status = DeliveryStatus.Failed;
            delivery.ResponseBody = "Request timeout";
            delivery.NextRetryAt = DateTime.UtcNow.AddSeconds(GetBackoffSeconds(delivery.AttemptCount));
            _logger.LogWarning($"Webhook delivery timeout to {subscription.Url}");
        }
        catch (Exception ex)
        {
            delivery.Status = DeliveryStatus.Failed;
            delivery.ResponseBody = ex.Message;
            delivery.NextRetryAt = DateTime.UtcNow.AddSeconds(GetBackoffSeconds(delivery.AttemptCount));
            _logger.LogError(ex, $"Error delivering webhook to {subscription.Url}");
        }

        return delivery;
    }

    public async Task<WebhookDelivery> RetryWebhookAsync(WebhookDelivery delivery, WebhookEvent webhookEvent, CancellationToken cancellationToken = default)
    {
        if (delivery.AttemptCount >= delivery.MaxRetries)
        {
            delivery.Status = DeliveryStatus.Abandoned;
            _logger.LogWarning($"Webhook delivery abandoned after {delivery.AttemptCount} attempts to {delivery.Url}");
            return delivery;
        }

        delivery.Status = DeliveryStatus.InProgress;
        delivery.AttemptCount++;
        delivery.LastAttemptAt = DateTime.UtcNow;

        try
        {
            var json = JsonSerializer.Serialize(webhookEvent);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            var response = await _httpClient.PostAsync(delivery.Url, content, cts.Token);

            delivery.ResponseStatusCode = (int)response.StatusCode;
            delivery.ResponseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                delivery.Status = DeliveryStatus.Success;
                _logger.LogInformation($"Webhook retry succeeded (attempt {delivery.AttemptCount}) to {delivery.Url}");
            }
            else
            {
                delivery.Status = DeliveryStatus.Failed;
                delivery.NextRetryAt = DateTime.UtcNow.AddSeconds(GetBackoffSeconds(delivery.AttemptCount));
                _logger.LogWarning($"Webhook retry failed (attempt {delivery.AttemptCount}) to {delivery.Url} with status {response.StatusCode}");
            }
        }
        catch (OperationCanceledException)
        {
            delivery.Status = DeliveryStatus.Failed;
            delivery.ResponseBody = "Request timeout";
            delivery.NextRetryAt = DateTime.UtcNow.AddSeconds(GetBackoffSeconds(delivery.AttemptCount));
            _logger.LogWarning($"Webhook retry timeout (attempt {delivery.AttemptCount}) to {delivery.Url}");
        }
        catch (Exception ex)
        {
            delivery.Status = DeliveryStatus.Failed;
            delivery.ResponseBody = ex.Message;
            delivery.NextRetryAt = DateTime.UtcNow.AddSeconds(GetBackoffSeconds(delivery.AttemptCount));
            _logger.LogError(ex, $"Error retrying webhook (attempt {delivery.AttemptCount}) to {delivery.Url}");
        }

        return delivery;
    }

    private static int GetBackoffSeconds(int attemptCount)
    {
        return attemptCount * 5; // 5 seconds for first retry, 10 for second, 15 for third
    }
}

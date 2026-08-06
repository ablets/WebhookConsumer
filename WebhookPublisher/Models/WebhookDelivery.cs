namespace WebhookPublisher.Models;

public class WebhookDelivery
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EventId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int AttemptCount { get; set; } = 0;
    public int MaxRetries { get; set; } = 3;
    public string? ResponseBody { get; set; }
    public int? ResponseStatusCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? NextRetryAt { get; set; }
}

public enum DeliveryStatus
{
    Pending,
    InProgress,
    Success,
    Failed,
    Abandoned
}

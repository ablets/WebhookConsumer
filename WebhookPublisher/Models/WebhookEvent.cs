namespace WebhookPublisher.Models;

public class WebhookEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EventType { get; set; } = string.Empty;
    public object? Data { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

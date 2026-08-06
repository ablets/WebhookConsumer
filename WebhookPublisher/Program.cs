using WebhookPublisher.Models;
using WebhookPublisher.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Trace);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(6000);
    options.ListenLocalhost(6001, listenOptions => listenOptions.UseHttps());
});

// Register services
builder.Services.AddSingleton<IWebhookRegistry, WebhookRegistry>();
builder.Services.AddHttpClient<IWebhookPublisherService, WebhookPublisherService>();
builder.Services.AddSingleton<WebhookPublisherBackgroundJob>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WebhookPublisherBackgroundJob>());

var app = builder.Build();

var backgroundJob = app.Services.GetRequiredService<WebhookPublisherBackgroundJob>();

// Management API endpoints

// Register a new webhook subscription
app.MapPost("/webhooks/subscribe", (SubscribeRequest request, IWebhookRegistry registry) =>
{
    var subscription = new WebhookSubscription
    {
        Url = request.Url,
        Events = request.Events
    };
    registry.RegisterSubscription(subscription);
    return Results.Created($"/webhooks/subscribe/{subscription.Id}", new { id = subscription.Id, status = "registered" });
});

// Unregister a webhook subscription
app.MapDelete("/webhooks/unsubscribe/{subscriptionId}", (string subscriptionId, IWebhookRegistry registry) =>
{
    var subscription = registry.GetSubscription(subscriptionId);
    if (subscription == null)
        return Results.NotFound(new { error = "Subscription not found" });

    registry.UnregisterSubscription(subscriptionId);
    return Results.Ok(new { status = "unregistered" });
});

// Get all subscriptions
app.MapGet("/webhooks/subscriptions", (IWebhookRegistry registry) =>
{
    var subscriptions = registry.GetAllSubscriptions();
    return Results.Ok(subscriptions);
});

// Trigger a webhook event
app.MapPost("/webhooks/trigger", (TriggerRequest request) =>
{
    var webhookEvent = new WebhookEvent
    {
        EventType = request.EventType,
        Data = request.Data
    };
    backgroundJob.EnqueueEvent(webhookEvent);
    return Results.Accepted($"/webhooks/delivery/{webhookEvent.Id}", new { eventId = webhookEvent.Id, status = "queued" });
});

// Get delivery status
app.MapGet("/webhooks/delivery/{deliveryId}", (string deliveryId) =>
{
    var delivery = backgroundJob.GetDeliveryStatus(deliveryId);
    if (delivery == null)
        return Results.NotFound(new { error = "Delivery not found" });

    return Results.Ok(delivery);
});

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

public record SubscribeRequest(string Url, string[] Events);
public record TriggerRequest(string EventType, object? Data);

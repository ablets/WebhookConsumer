var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Trace);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5000);
    options.ListenLocalhost(5001, listenOptions => listenOptions.UseHttps());
});

var app = builder.Build();

static void LogWebhookReceived(string body)
{
    Console.WriteLine($"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]");
    Console.WriteLine(body + "\n");
}

app.MapPost("/webhook/task-counter", async (HttpContext context) =>
{
    try
    {
        string body = await new StreamReader(context.Request.Body).ReadToEndAsync();
        LogWebhookReceived(body);

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(@"{""status"":""success""}");
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync($@"{{""status"":""error"",""message"":""{ex.Message}""}}");
    }
});

app.Run();

using LankaMart.BuildingBlocks;

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatformDefaults("inventory");

var app = builder.Build();
app.UsePlatformDefaults();

app.MapGet("/api/v1/ping", () => Results.Ok(new { service = "inventory", status = "ok", time = DateTimeOffset.UtcNow }));

app.Run();

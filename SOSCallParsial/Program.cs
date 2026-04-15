using Microsoft.EntityFrameworkCore;
using SOSCallParsial.DAL;
using SOSCallParsial.Models.Configs;
using SOSCallParsial.Services;


var builder = WebApplication.CreateBuilder(args);


builder.Services.Configure<TcpSettings>(builder.Configuration.GetSection("Tcp"));
builder.Services.Configure<CallSettings>(builder.Configuration.GetSection("CallSettings"));


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));


builder.Services.AddScoped<CallQueueService>();
builder.Services.AddSingleton<DomoMessageParser>();
builder.Services.AddSingleton<TcpServerStatus>();
builder.Services.AddHostedService<TcpListenerService>();
builder.Services.AddHostedService<PortHealthCheckService>();
builder.Services.AddHttpClient(); 


builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/dashboard", async (AppDbContext db, TcpServerStatus serverStatus, CancellationToken cancellationToken) =>
{
    var recentLogs = await db.AlarmLogs
        .AsNoTracking()
        .OrderByDescending(x => x.Timestamp)
        .Take(50)
        .Select(x => new
        {
            x.Id,
            x.Account,
            x.EventCode,
            x.GroupCode,
            x.ZoneCode,
            x.PhoneNumber,
            x.Timestamp,
            x.RawMessage
        })
        .ToListAsync(cancellationToken);

    var utcNow = DateTime.UtcNow;
    var last24Hours = utcNow.AddHours(-24);

    var totalLogs = await db.AlarmLogs.AsNoTracking().CountAsync(cancellationToken);
    var last24HoursCount = await db.AlarmLogs.AsNoTracking()
        .CountAsync(x => x.Timestamp >= last24Hours, cancellationToken);
    var lastCaller = await db.AlarmLogs.AsNoTracking()
        .Where(x => !string.IsNullOrWhiteSpace(x.PhoneNumber))
        .OrderByDescending(x => x.Timestamp)
        .Select(x => x.PhoneNumber)
        .FirstOrDefaultAsync(cancellationToken);

    return Results.Ok(new
    {
        server = serverStatus.GetSnapshot(),
        metrics = new
        {
            totalLogs,
            last24HoursCount,
            lastCaller,
            lastAlarmAtUtc = recentLogs.FirstOrDefault()?.Timestamp
        },
        logs = recentLogs
    });
});

app.MapGet("/api/live", () => Results.Ok(new
{
    status = "ok",
    timestampUtc = DateTime.UtcNow
}));

app.MapFallbackToFile("index.html");



try
{
    app.Run();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "🔥 Application crashed fatally.");
}

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
builder.Services.AddHostedService<TcpListenerService>();
builder.Services.AddHttpClient(); 


builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();



try
{
    app.Run();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "🔥 Application crashed fatally.");
}

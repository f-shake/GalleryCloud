using GalleryCloud.Api.BackgroundJobs;
using GalleryCloud.Api.Data;
using GalleryCloud.Api.Dtos;
using GalleryCloud.Api.Middleware;
using GalleryCloud.Api.Services;
using GalleryCloud.Core.Interfaces;
using GalleryCloud.Core.Settings;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Microsoft.AspNetCore.ResponseCompression;
// Serilog: 控制台输出 + 按天滚动文件到 App_Data/logs/
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine("App_Data", "logs", "gallerycloud-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=App_Data/gallerycloud.db";
var thumbConnectionString = builder.Configuration.GetConnectionString("Thumbnails")
    ?? "Data Source=App_Data/thumbnails.db";

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDbContext<ThumbnailDbContext>(options =>
    options.UseSqlite(thumbConnectionString));
builder.Services.AddMemoryCache();

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

// Core services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IStatsService, StatsService>();
builder.Services.AddSingleton<IFilesystemBrowserService, FilesystemBrowserService>();
builder.Services.AddSingleton<ISettingService, SettingService>();
builder.Services.AddScoped<UserContext>();
builder.Services.AddScoped<IUserContext>(sp => sp.GetRequiredService<UserContext>());

// Application services (singletons for in-memory state)
builder.Services.AddSingleton<IScanService, ScanService>();
builder.Services.AddSingleton<IThumbnailService, ThumbnailService>();

// Background services
builder.Services.AddSingleton<FileWatcherService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FileWatcherService>());
builder.Services.AddHostedService<ScheduledScanJob>();
builder.Services.AddHostedService<ThumbnailWorker>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.TypeInfoResolver = GalleryCloudJsonContext.Default;
    });

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// Runtime database migration + seed
await DbMigrator.MigrateAsync(app.Services);

// Load dynamic settings from DB
var settingService = app.Services.GetRequiredService<ISettingService>();
await settingService.LoadDefaultsAsync();

// Middleware pipeline — only our custom AuthMiddleware, no ASP.NET JWT Bearer
app.UseResponseCompression();
app.UseCors();
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<AuthMiddleware>();

app.MapControllers();

// SPA: serve Vue frontend
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

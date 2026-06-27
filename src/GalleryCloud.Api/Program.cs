using GalleryCloud.Api.BackgroundJobs;
using GalleryCloud.Api.Data;
using GalleryCloud.Api.Middleware;
using GalleryCloud.Api.Services;
using GalleryCloud.Core.Interfaces;
using GalleryCloud.Core.Settings;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=data/gallerycloud.db";

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddMemoryCache();

// Core services
builder.Services.AddScoped<IAuthService, AuthService>();
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

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
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
app.UseCors();
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<AuthMiddleware>();

app.MapControllers();

// SPA: serve Vue frontend
app.MapFallbackToFile("index.html");
app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();

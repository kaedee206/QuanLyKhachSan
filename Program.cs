using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.BackgroundServices;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Helpers;
using QuanLyKhachSan.Middleware;
using QuanLyKhachSan.Services;
using QuanLyKhachSan.Services.AI;
using Serilog;

// Allow flexible DateTime Kind handling for Npgsql (PostgreSQL)
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// Load .env file (local dev). In Docker/production, env vars are injected directly.
// NoClobber() ensures already-set env vars (e.g. from Docker) are not overwritten.
DotNetEnv.Env.NoClobber().Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);

// Resolve ${VAR_NAME} placeholders in appsettings.json using environment variables
builder.Configuration.AddEnvInterpolation();


// Persist Data Protection keys để antiforgery/auth cookies không bị mất khi container restart
// Docker: set DataProtection:KeysPath=/app/keys via env var
// Local dev: defaults to {ProjectDir}/keys/
var keysPath = builder.Configuration["DataProtection:KeysPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "keys");
Directory.CreateDirectory(keysPath); // Ensure dir exists in all environments
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("SunHotel")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/app-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 10)
    .WriteTo.File("logs/error-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 5,
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error)
    .CreateLogger();

builder.Host.UseSerilog();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<SunHotelDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.Name = "SunHotel.Auth";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Headers["Accept"].ToString().Contains("application/json"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                context.Response.WriteAsync("{\"error\":\"Unauthorized\"}");
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            }
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Headers["Accept"].ToString().Contains("application/json"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                context.Response.WriteAsync("{\"error\":\"Forbidden\"}");
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            }
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<RoomService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<ServiceManagementService>();
builder.Services.AddScoped<TicketService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<MoMoService>();
builder.Services.AddScoped<EmailSftpService>();
builder.Services.AddScoped<SePayService>();
builder.Services.AddSingleton(EmailRateLimiter.Instance);

// AI Services
builder.Services.AddHttpClient();
builder.Services.AddScoped<AiToolRegistry>();
builder.Services.AddScoped<AiPromptBuilder>();
builder.Services.AddScoped<AiSecurityGuard>();
builder.Services.AddScoped<AiToolExecutor>();
builder.Services.AddScoped<AiChatService>();

builder.Services.AddHostedService<QuanLyKhachSan.BackgroundServices.SePayPollingService>();
builder.Services.AddHostedService<QuanLyKhachSan.BackgroundServices.EmailRetryBackgroundService>();

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// CORS for React frontend (dev: localhost:5173, prod: configurable via FRONTEND_URL env var)
var frontendUrl = builder.Configuration["AppSettings:FrontendUrl"] ?? "http://localhost:5173";
builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactFrontend", policy =>
    {
        policy.WithOrigins(frontendUrl)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials(); // Required for cookie-based auth
    });
});

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<AiRateLimitMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseSession();
app.UseCors("ReactFrontend");
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "admin",
    pattern: "Admin/{action=Index}/{id?}",
    defaults: new { controller = "Admin" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = services.GetRequiredService<SunHotelDbContext>();

        // Retry cho đến khi DB sẵn sàng (SQL Server khởi động chậm hơn app)
        var maxRetries = 20;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await db.Database.CanConnectAsync();
                Log.Information("Database connection established on attempt {Attempt}", attempt);
                break;
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries)
                {
                    Log.Fatal(ex, "Cannot connect to database after {MaxRetries} attempts. Exiting.", maxRetries);
                    return;
                }
                Log.Warning("Database not ready (attempt {Attempt}/{MaxRetries}), retrying in 3s...", attempt, maxRetries);
                await Task.Delay(3000);
            }
        }

        // Schema migration handled automatically by EnsureCreated() in SeedData
        Log.Information("Database schema managed by EnsureCreated");

        try
        {
            await SeedData.InitializeAsync(services);
            Log.Information("SeedData initialized successfully");
        }
        catch (InvalidOperationException ex)
        {
            Log.Fatal(ex, "SeedData FAILED - App stopped");
            Console.WriteLine("\n" + new string('=', 70));
            Console.WriteLine("FATAL ERROR: " + ex.Message);
            Console.WriteLine(new string('=', 70));
            Console.WriteLine("\nCÁCH SỬA:");
            Console.WriteLine("  1. Xóa toàn bộ bảng 'User' trong database");
            Console.WriteLine("  2. Restart app - SeedData sẽ tạo lại tài khoản");
            Console.WriteLine(new string('=', 70) + "\n");
            return;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "SeedData FAILED unexpectedly - App stopped");
            Console.WriteLine("\n" + new string('=', 70));
            Console.WriteLine("FATAL ERROR: " + ex.Message);
            Console.WriteLine(new string('=', 70) + "\n");
            return;
        }

        try
        {
            var roomTypeImages = new Dictionary<string, string>
            {
                { "Standard",       "/images/rooms/single-1.jpg" },
                { "Superior",        "/images/rooms/double-1.jpg" },
                { "Deluxe",          "/images/rooms/family-1.jpg" },
                { "Suite",           "/images/rooms/vip-1.jpg" },
                { "President Suite", "/images/hotel-assets/rooms/vip-room.png" }
            };

            foreach (var (name, url) in roomTypeImages)
            {
                var rt = db.RoomTypes.FirstOrDefault(r => r.Name == name);
                if (rt != null && (string.IsNullOrEmpty(rt.ImageUrl) || rt.ImageUrl.Contains("/images/rooms/standard") || rt.ImageUrl.Contains("/images/rooms/superior") || rt.ImageUrl.Contains("/images/rooms/deluxe") || rt.ImageUrl.Contains("/images/rooms/suite") || rt.ImageUrl.Contains("/images/rooms/president")))
                {
                    rt.ImageUrl = url;
                    rt.UpdatedAt = DateTime.UtcNow;
                    Log.Information("Updated ImageUrl for RoomType: {Name}", name);
                }
            }
            await db.SaveChangesAsync();
            Log.Information("RoomType images updated successfully");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RoomType image update failed");
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Startup initialization failed");
    }
}

Log.Information("SunHotel MVC khởi động trên môi trường: {Environment}", app.Environment.EnvironmentName);

app.Run();

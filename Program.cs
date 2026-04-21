using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.BackgroundServices;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://+:5000");

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
    options.UseSqlServer(connectionString));

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.None;
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
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.Name = "SunHotel.Auth";
        options.Cookie.SameSite = SameSiteMode.None;
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

builder.Services.AddHostedService<QuanLyKhachSan.BackgroundServices.SePayPollingService>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSession();
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
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                DECLARE @sql NVARCHAR(MAX) = N'';
                SELECT @sql += N'ALTER TABLE [dbo].[booking] DROP CONSTRAINT ' + QUOTENAME(name) + ';'
                FROM sys.default_constraints
                WHERE parent_object_id = OBJECT_ID('booking')
                AND col_name(parent_object_id, parent_column_id) = 'booking_code';
                IF @sql <> '' EXEC sp_executesql @sql;
                ALTER TABLE [dbo].[booking] ALTER COLUMN booking_code NVARCHAR(20) NOT NULL;
            ");
            Log.Information("Database schema: booking_code column expanded to NVARCHAR(20)");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Schema fix for booking_code failed (may already be correct)");
        }

        try
        {
            var sepayColumns = new[]
            {
                ("sepay_order_id", "NVARCHAR(100)"),
                ("sepay_order_code", "NVARCHAR(100)"),
                ("sepay_transaction_id", "NVARCHAR(100)"),
                ("sepay_payment_method", "NVARCHAR(50)"),
                ("sepay_order_status", "NVARCHAR(50)"),
                ("sepay_created_at", "DATETIME2"),
                ("sepay_paid_at", "DATETIME2"),
                ("sepay_last_check_at", "DATETIME2"),
                ("sepay_polling_expires_at", "DATETIME2")
            };

            var existingColumnsRaw = await db.Database
                .SqlQueryRaw<string>("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'invoice'")
                .ToListAsync();
            var existingColumns = existingColumnsRaw.Select(c => c.ToLower()).ToHashSet();

            foreach (var (colName, colType) in sepayColumns)
            {
                if (!existingColumns.Contains(colName.ToLower()))
                {
                    await db.Database.ExecuteSqlRawAsync(
                        $"ALTER TABLE [dbo].[invoice] ADD [{colName}] {colType} NULL;");
                    Log.Information("Database schema: Added column {ColumnName} to invoice table", colName);
                }
            }
            Log.Information("Database schema: SePay columns checked/added successfully");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Schema fix for SePay columns failed (may already exist)");
        }

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
            Console.WriteLine("\nCACH SUA:");
            Console.WriteLine("  1. Xoa toan bo bang 'User' trong database");
            Console.WriteLine("  2. Restart app - SeedData se tao lai tai khoan");
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

        await SeedData.InitializeAsync(services);
        Log.Information("SeedData initialized successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "SeedData initialization failed");
    }
}

Log.Information("SunHotel MVC khoi dong tren moi truong: {Environment}", app.Environment.EnvironmentName);

app.Run();

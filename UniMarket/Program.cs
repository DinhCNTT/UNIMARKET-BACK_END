using CloudinaryDotNet;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Text;
using UniMarket.DataAccess;
using UniMarket.Hubs;
using UniMarket.Models;
using UniMarket.Services;
using Microsoft.AspNetCore.HttpOverrides;
using UniMarket.DTO;
using UniMarket.Services.Recommendation; // Namespace chứa AI Services

var builder = WebApplication.CreateBuilder(args);

// ====================================================
// 1. CẤU HÌNH SERVICES (Dependency Injection)
// ====================================================

// --- Cloudinary (Upload ảnh/video) ---
builder.Services.Configure<CloudinarySettings>(builder.Configuration.GetSection("CloudinarySettings"));
builder.Services.AddScoped<PhotoService>();
builder.Services.AddHostedService<UniMarket.Services.MediaDeletionService>();
builder.Services.AddSingleton(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var settings = new CloudinarySettings();
    config.GetSection("CloudinarySettings").Bind(settings);

    var cloudinary = new Cloudinary(new Account(settings.CloudName, settings.ApiKey, settings.ApiSecret));
    cloudinary.Api.Timeout = 180000; // 3 phút timeout
    return cloudinary;
});

// --- Email Service (Gmail) ---
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Gmail"));
builder.Services.AddScoped<IEmailSender, GmailEmailSender>();

// --- CORS (Cho phép Frontend React gọi API) ---
var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(MyAllowSpecificOrigins, policy =>
    {
        policy.WithOrigins("http://localhost:5173","http://localhost:3000") // Frontend URL
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Quan trọng cho SignalR/Cookies
    });
});

// --- Database Context (SQL Server) ---
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => {
            sqlOptions.CommandTimeout(120);
            // 👇 THÊM DÒNG NÀY: Tự động thử lại tối đa 5 lần nếu lỗi kết nối
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        }
    ));

// --- MongoDB Service (Chi tiết tin đăng - MỚI THÊM) ---
// Đăng ký Singleton để kết nối MongoDB được tái sử dụng xuyên suốt vòng đời ứng dụng
builder.Services.AddSingleton<UniMarket.Services.TinDangDetailService>();

// --- Tăng giới hạn upload file (150MB) ---
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 157286400;
});

// --- Identity (Quản lý User/Role) ---
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddDefaultUI();

// --- JWT Authentication ---
var jwtSettings = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSettings["Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    // Fallback to default if key is missing
    jwtKey = "default-secret-key-for-development-only-change-in-production-1234567890";
}
var key = Encoding.UTF8.GetBytes(jwtKey);
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.Events = new JwtBearerEvents
    {
        // Logic lấy Token từ Query String cho SignalR
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            // Đã thêm /userNotificationHub vào điều kiện kiểm tra
            if (!string.IsNullOrEmpty(accessToken) &&
                (path.StartsWithSegments("/hub/chat") ||
                 path.StartsWithSegments("/hub/comment") ||
                 path.StartsWithSegments("/SocialChatHub") ||
                 path.StartsWithSegments("/videoHub") ||
                 path.StartsWithSegments("/hub/notifications") ||
                 path.StartsWithSegments("/userNotificationHub"))) // <-- MỚI THÊM
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync("{\"message\": \"Unauthorized - Token không hợp lệ hoặc đã hết hạn.\"}");
        }
    };

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
});

// --- SignalR (Real-time) ---
builder.Services.AddSignalR();
builder.Services.AddSingleton<UserPresenceService>();
builder.Services.AddHostedService<PresenceTimeoutService>();
builder.Services.AddSingleton<ConnectionMapping<string>>();
builder.Services.AddHostedService<CleanUpEmptyConversationsJob>();

// --- Swagger API Docs ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "UniMarket API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập JWT Token vào đây. Ví dụ: Bearer {your-token}"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            new string[] { }
        }
    });
    c.OperationFilter<FileUploadOperationFilter>();
});

builder.Services.AddScoped<UniMarket.Services.PriceAnalysis.PriceAnalysisService>();

// --- Các Service Nghiệp vụ ---
builder.Services.AddScoped<IQuickMessageService, QuickMessageService>();
// ✅ MỚI THÊM: Đăng ký UserNotificationService
builder.Services.AddScoped<IUserNotificationService, UserNotificationService>();

// ✅ [QUAN TRỌNG] Đăng ký AI Services (refactored)
builder.Services.AddScoped<AiIntentService>();          // Phân tích intent
builder.Services.AddScoped<ProductSearchService>();     // Tìm kiếm sản phẩm
builder.Services.AddScoped<ChatPersistenceService>();   // Lưu tin nhắn
builder.Services.AddScoped<AiService>();                // Coordinator chính

// ✅ [QUAN TRỌNG] Worker chạy ngầm để Train AI (Fix lỗi treo Server)
builder.Services.AddHostedService<AITrainingWorker>();
// Đăng ký HttpClient và AiClient cho việc gọi Gemini API
builder.Services.AddHttpClient();
builder.Services.AddScoped<AiClient>();

// ✅ [QUAN TRỌNG] Đăng ký AI Recommendation Services
builder.Services.AddScoped<UserBehaviorService>();          // Service xử lý dữ liệu hành vi
builder.Services.AddSingleton<RecommendationEngine>();      // AI Engine (Singleton để giữ Model)
builder.Services.AddScoped<VideoRecommendationService>();   // Logic tính điểm video
// --- Controllers & JSON ---
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
        // ✅ Cấu hình camelCase naming cho JSON output (để frontend nhận đúng casing)
        options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver();
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .Select(x => new { Field = x.Key, Errors = x.Value?.Errors.Select(e => e.ErrorMessage).ToArray() });
            return new BadRequestObjectResult(new { Message = "Dữ liệu không hợp lệ.", Errors = errors });
        };
    });

var app = builder.Build();

// ====================================================
// 2. CẤU HÌNH PIPELINE (Middleware)
// ====================================================

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

// Xử lý Exception toàn cục
app.UseExceptionHandler(appBuilder =>
{
    appBuilder.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var result = JsonConvert.SerializeObject(new { message = error?.Message });
        await context.Response.WriteAsync(result);
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "UniMarket API v1");
        c.RoutePrefix = "swagger";
    });
}

// File Tĩnh (Images)
app.UseStaticFiles();
// Ensure image directories exist to avoid DirectoryNotFoundException when saving files
var postsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "Posts");
var categoriesDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "categories");
Directory.CreateDirectory(postsDir);
Directory.CreateDirectory(categoriesDir);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/categories")),
    RequestPath = "/images/categories"
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/Posts")),
    RequestPath = "/images/Posts"
});

// ✅ Kích hoạt WebSockets cho SignalR
app.UseWebSockets();

app.UseRouting();
app.UseCors(MyAllowSpecificOrigins); // Đặt sau Routing, trước Auth

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

// ✅ Map SignalR Hubs
app.MapHub<ChatHub>("/hub/chat");
app.MapHub<CommentHub>("/hub/comment");
app.MapHub<SocialChatHub>("/SocialChatHub");
app.MapHub<VideoHub>("/videoHub");
app.MapHub<NotificationHub>("/hub/notifications");
// ✅ MỚI THÊM: Map UserNotificationHub
app.MapHub<UserNotificationHub>("/userNotificationHub");

// ====================================================
// 3. KHỞI TẠO DỮ LIỆU (Seeding & Migration)
// ====================================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    try
    {
        // 👇 1. TỰ ĐỘNG TẠO DB & MIGRATION (Thêm đoạn này vào đầu tiên)
        var context = services.GetRequiredService<UniMarket.DataAccess.ApplicationDbContext>();
        if (context.Database.GetPendingMigrations().Any())
        {
            context.Database.Migrate(); // Tương đương lệnh update-database
            Console.WriteLine("--> Database migration applied successfully.");
        }
        // 👆 HẾT PHẦN THÊM

        // 👇 2. Sau khi có DB rồi mới chạy tạo Admin
        await InitializeRolesAndAdmin(services);
    }
    catch (Exception ex)
    {
        // Database chưa sẵn sàng hoặc lỗi kết nối
        Console.WriteLine($"⚠️ Database initialization skipped: {ex.Message}");
    }
}

// Chạy ứng dụng (Giữ nguyên dòng này của bạn)
await app.RunAsync();

// ====================================================
// 4. CÁC HÀM HELPER
// ====================================================
async Task InitializeRolesAndAdmin(IServiceProvider serviceProvider)
{
    var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    string[] roleNames = { "Admin", "Employee", "User" };
    foreach (var role in roleNames)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    string adminEmail = "admin@unimarket.com";
    string adminPassword = "Admin@123";

    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if (adminUser == null)
    {
        var newAdmin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FullName = "Admin User"
        };

        var result = await userManager.CreateAsync(newAdmin, adminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(newAdmin, "Admin");
        }
        else
        {
            Console.WriteLine("Lỗi tạo Admin: " + string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }
}
using CloudinaryDotNet;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Text;
using UniMarket.DataAccess;
using UniMarket.Hubs;
using UniMarket.Models;
using UniMarket.Services;
using Microsoft.AspNetCore.HttpOverrides;
using UniMarket.DTO;
using UniMarket.Services.Recommendation;
using UniMarket.Services.PriceAnalysis;
using UniMarket.Services.Interfaces;
using UniMarket.Services.Implementations;

var builder = WebApplication.CreateBuilder(args);

// ====================================================
// 1. CẤU HÌNH SERVICES (Dependency Injection)
// ====================================================

// --- Cloudinary (Upload ảnh/video) ---
builder.Services.Configure<CloudinarySettings>(builder.Configuration.GetSection("CloudinarySettings"));
builder.Services.AddScoped<PhotoService>();
builder.Services.AddHostedService<MediaDeletionService>();
builder.Services.AddSingleton(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var settings = new CloudinarySettings();
    config.GetSection("CloudinarySettings").Bind(settings);

    var cloudinary = new Cloudinary(new Account(settings.CloudName, settings.ApiKey, settings.ApiSecret));
    cloudinary.Api.Timeout = 180000; // 3 phút timeout
    return cloudinary;
});

// --- Email Service ---
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Gmail"));
builder.Services.AddScoped<IEmailSender, GmailEmailSender>();

// --- CORS Configuration ---
var AllowReactAppPolicy = "AllowReactApp";

builder.Services.AddCors(options =>
{
    options.AddPolicy(AllowReactAppPolicy, policy =>
    {
        policy.SetIsOriginAllowed(origin => true) // Cho phép mọi nguồn gốc
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Bắt buộc cho SignalR
    });
});

// --- Database Context (SQL Server) ---
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => {
            sqlOptions.CommandTimeout(120);
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        }
    ));

// --- MongoDB Service (ĐÃ CẬP NHẬT) ---
// [MỚI THÊM] Đăng ký MongoDbContext cho tính năng Search & Log
builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddSingleton<TinDangDetailService>();
builder.Services.AddSingleton<NhaTroDetailService>();

// 1. Đăng ký Client
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("MongoDbConnection");
    return new MongoDB.Driver.MongoClient(connectionString);
});

// 2. [QUAN TRỌNG] Đăng ký IMongoDatabase để Controller có thể Inject được
builder.Services.AddScoped<IMongoDatabase>(sp =>
{
    var client = sp.GetRequiredService<IMongoClient>();
    var config = sp.GetRequiredService<IConfiguration>();
    // Lấy tên DB từ appsettings.json section "MongoDbSettings:DatabaseName"
    // Hãy đảm bảo trong appsettings.json bạn có cấu hình này
    var dbName = config["MongoDbSettings:DatabaseName"] ?? "UniMarketMongoDb";
    return client.GetDatabase(dbName);
});

// --- Tăng giới hạn upload file (150MB) ---
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 157286400;
});

// --- Identity ---
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddDefaultUI();

// --- JWT Authentication & SignalR Token Logic ---
var jwtSettings = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSettings["Key"] ?? "default-secret-key-for-development-only-change-in-production-1234567890";
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
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            // Kiểm tra tất cả các Hub Path
            if (!string.IsNullOrEmpty(accessToken) &&
                (path.StartsWithSegments("/hub/chat") ||
                 path.StartsWithSegments("/hub/comment") ||
                 path.StartsWithSegments("/SocialChatHub") ||
                 path.StartsWithSegments("/videoHub") ||
                 path.StartsWithSegments("/hub/notifications") ||
                 path.StartsWithSegments("/userNotificationHub")))
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

// --- SignalR & Background Jobs ---
builder.Services.AddSignalR();
builder.Services.AddSingleton<UserPresenceService>();
builder.Services.AddSingleton<ConnectionMapping<string>>();
builder.Services.AddHostedService<PresenceTimeoutService>();
builder.Services.AddHostedService<CleanUpEmptyConversationsJob>();
builder.Services.AddHostedService<ScoreDecayJob>();

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
    // Lưu ý: Đảm bảo class FileUploadOperationFilter tồn tại
    c.OperationFilter<FileUploadOperationFilter>();
});

// --- Các Service Nghiệp vụ & AI ---
builder.Services.AddHttpClient();
builder.Services.AddScoped<PriceAnalysisService>();
builder.Services.AddScoped<IQuickMessageService, QuickMessageService>();
builder.Services.AddScoped<IUserNotificationService, UserNotificationService>();
builder.Services.AddScoped<ISearchService, SearchService>();

// View History Service (MongoDB)
builder.Services.AddScoped<ViewHistoryMongoService>();

// Logic AI & ChatBot
builder.Services.AddScoped<AiClient>();
builder.Services.AddScoped<AiIntentService>();
builder.Services.AddScoped<ProductSearchService>();
builder.Services.AddScoped<ExternalToolService>();
builder.Services.AddScoped<ChatPersistenceService>();
builder.Services.AddScoped<AiService>();
builder.Services.AddHostedService<AITrainingWorker>();

// Search Fallback Config
builder.Services.Configure<SearchFallbackConfig>(builder.Configuration.GetSection("SearchFallback"));

// Logic AI Recommendation
builder.Services.AddScoped<UserBehaviorService>();
builder.Services.AddScoped<UserRecommendationService>();
builder.Services.AddScoped<VideoRecommendationService>();
builder.Services.AddSingleton<RecommendationEngine>();
builder.Services.AddScoped<IUserAffinityService, UserAffinityService>();

// --- Controllers & JSON Serialization ---
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
        options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
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

builder.Services.AddRazorPages();

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

// Cấu hình File Tĩnh & Thư mục ảnh
app.UseStaticFiles();
var postsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "Posts");
var categoriesDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "categories");
Directory.CreateDirectory(postsDir);
Directory.CreateDirectory(categoriesDir);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(categoriesDir),
    RequestPath = "/images/categories"
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(postsDir),
    RequestPath = "/images/Posts"
});

app.UseWebSockets();
app.UseRouting();

// --- Kích hoạt CORS (Đặt trước Auth & Authorization) ---
app.UseCors(AllowReactAppPolicy);

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

// Map SignalR Hubs
app.MapHub<ChatHub>("/hub/chat");
app.MapHub<CommentHub>("/hub/comment");
app.MapHub<SocialChatHub>("/SocialChatHub");
app.MapHub<VideoHub>("/videoHub");
app.MapHub<NotificationHub>("/hub/notifications");
app.MapHub<UserNotificationHub>("/userNotificationHub");

// ====================================================
// 3. KHỞI TẠO DỮ LIỆU (Seeding & Migration)
// ====================================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        if (context.Database.GetPendingMigrations().Any())
        {
            context.Database.Migrate();
            Console.WriteLine("--> Database migration applied successfully.");
        }
        await InitializeRolesAndAdmin(services);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Database initialization skipped or failed: {ex.Message}");
    }
}

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
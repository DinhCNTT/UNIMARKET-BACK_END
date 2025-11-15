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
using System.Text;
using UniMarket.DataAccess;
using UniMarket.Hubs;
using UniMarket.Models;
using UniMarket.Services;
using Microsoft.AspNetCore.HttpOverrides;
using UniMarket.DTO;

var builder = WebApplication.CreateBuilder(args);

// ==========================
// 🔧 Cloudinary
// ==========================
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

// ==========================
// 📧 Email Service (Gmail)
// ==========================
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Gmail"));
builder.Services.AddScoped<IEmailSender, GmailEmailSender>();

// ==========================
// 🔓 CORS
// ==========================
var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(MyAllowSpecificOrigins, policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ==========================
// 🗄️ DbContext
// ==========================
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ==========================
// 📦 FIX: Tăng giới hạn upload
// ==========================
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 157286400; // 150MB
});

// ==========================
// 👤 Identity
// ==========================
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddDefaultUI();

// ==========================
// 🔐 JWT Authentication
// ==========================
var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? throw new ArgumentNullException("Jwt:Key không được để trống"));

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

            // SỬA LỖI 1: Thêm đường dẫn của SocialChatHub vào
            if (!string.IsNullOrEmpty(accessToken) &&
                (path.StartsWithSegments("/hub/chat") ||
                 path.StartsWithSegments("/hub/comment") ||
                 path.StartsWithSegments("/SocialChatHub") ||
                 path.StartsWithSegments("/videoHub"))) // <-- ✅ THÊM DÒNG NÀY
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

// ==========================
// 💬 SignalR
// ==========================
builder.Services.AddSignalR();
builder.Services.AddSingleton<UserPresenceService>();
builder.Services.AddHostedService<PresenceTimeoutService>();
builder.Services.AddSingleton<ConnectionMapping<string>>();
builder.Services.AddHostedService<CleanUpEmptyConversationsJob>();

// ==========================
// 🔍 Swagger
// ==========================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "UniMarket API",
        Version = "v1"
    });

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

// ==========================
// 💨 Quick Message Service
// ==========================
builder.Services.AddScoped<IQuickMessageService, QuickMessageService>();

// ==========================
// 🌐 Controllers + JSON
// ==========================
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
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

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

// ==========================
// 🧯 Exception Middleware
// ==========================
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

// ==========================
// 🧩 Middlewares
// ==========================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "UniMarket API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseStaticFiles();
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

// SỬA LỖI 2: KÍCH HOẠT WEBSOCKET
app.UseWebSockets();

app.UseRouting();
app.UseCors(MyAllowSpecificOrigins);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

// SỬA LỖI 3: Đồng bộ đường dẫn Hub
app.MapHub<ChatHub>("/hub/chat");
app.MapHub<CommentHub>("/hub/comment");
app.MapHub<SocialChatHub>("/SocialChatHub"); // <-- ĐÃ SỬA
app.MapHub<VideoHub>("/videoHub");

// ==========================
// 👑 Tạo Role + Admin mặc định
// ==========================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await InitializeRolesAndAdmin(services);
}

// ==========================
// 🕵️‍♂️ ✅ DÁN ENDPOINT XEM TẤT CẢ API (TỪ CODE CỦA ĐỊNH) VÀO ĐÂY
// ==========================
app.MapGet("/all-routes", (IActionDescriptorCollectionProvider provider) =>
{
    var routes = provider.ActionDescriptors.Items.Select(item =>
    {
        var action = item as Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor;
        var controller = action?.ControllerName;
        var method = action?.ActionName;

        var httpMethod = item.EndpointMetadata
                   .OfType<HttpMethodMetadata>()
                   .FirstOrDefault()?
                   .HttpMethods
                   .FirstOrDefault(); // Lấy phương thức HTTP (GET, POST...)

        return new
        {
            Path = item.AttributeRouteInfo?.Template,
            Method = httpMethod,
            Controller = controller,
            Action = method
        };
    })
    .Where(r => r.Path != null) // Chỉ lấy các route có định nghĩa Attribute
      .OrderBy(r => r.Path);

    return Results.Ok(routes);
});

await app.RunAsync();

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
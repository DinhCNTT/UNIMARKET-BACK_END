using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using System.Text;
using UniMarket.DataAccess;
using UniMarket.Models;
using Microsoft.AspNetCore.Mvc;
using UniMarket.Services;
using UniMarket.Hubs;
using Swashbuckle.AspNetCore.Filters;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Cloudinary
builder.Services.Configure<CloudinarySettings>(builder.Configuration.GetSection("CloudinarySettings"));
builder.Services.AddScoped<PhotoService>();

// 📧 Email Configuration
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Gmail"));
builder.Services.AddScoped<IEmailSender, GmailEmailSender>();

// CORS Configuration
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

// DbContext Configuration
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity Configuration
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddDefaultUI();

builder.Services.AddSingleton<ConnectionMapping<string>>();

// JWT Configuration
var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? throw new ArgumentNullException("Jwt:Key không được để trống"));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
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
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(key)
        };
    });

builder.Services.AddAuthorization();

// Swagger Configuration
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "UniMarket API",
        Version = "v1",
        Description = "API cho hệ thống mua bán UniMarket",
        Contact = new OpenApiContact
        {
            Name = "Nguyễn Xuân Đạt",
            Email = "contact@unimarket.com"
        }
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
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });

    c.OperationFilter<FileUploadOperationFilter>();
});

// SignalR for chat
builder.Services.AddSignalR();

// Controller and error handling
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
                .Select(x => new
                {
                    Field = x.Key,
                    Errors = x.Value?.Errors.Select(e => e.ErrorMessage).ToArray()
                });

            return new BadRequestObjectResult(new
            {
                Message = "Dữ liệu không hợp lệ.",
                Errors = errors
            });
        };
    });

// Hosted Service for cleaning up empty chats
builder.Services.AddHostedService<CleanUpEmptyConversationsJob>();

var app = builder.Build();

// Middleware: Xử lý lỗi 500 toàn cục (JSON)
app.UseExceptionHandler(appBuilder =>
{
    appBuilder.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerFeature>();
        if (exceptionHandlerPathFeature != null)
        {
            var error = exceptionHandlerPathFeature.Error;
            var result = JsonConvert.SerializeObject(new { message = error.Message });
            await context.Response.WriteAsync(result);
        }
    });
});

// Swagger UI
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "UniMarket API v1");
        c.RoutePrefix = "swagger";
    });
}

// Static files
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/categories")),
    RequestPath = "/images/categories"
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/Posts")),
    RequestPath = "/images/Posts"
});

app.UseCors(MyAllowSpecificOrigins); // Apply CORS policy
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapHub<ChatHub>("/hub/chat");

// Init admin + roles
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await InitializeRolesAndAdmin(services);
}

await app.RunAsync();

async Task InitializeRolesAndAdmin(IServiceProvider serviceProvider)
{
    var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    string[] roleNames = { "Admin", "Employee", "User" };

    foreach (var roleName in roleNames)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole(roleName));
        }
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

        var createAdminResult = await userManager.CreateAsync(newAdmin, adminPassword);
        if (createAdminResult.Succeeded)
        {
            await userManager.AddToRoleAsync(newAdmin, "Admin");
        }
        else
        {
            Console.WriteLine("Lỗi tạo admin: " + string.Join(", ", createAdminResult.Errors.Select(e => e.Description)));
        }
    }
}

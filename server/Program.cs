using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using HackITSentry.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<LicenseEncryptionService>();
builder.Services.AddSingleton<RuntimeSettings>();
builder.Services.AddSingleton<AlertEmailService>();
builder.Services.AddHostedService<DeviceOfflineAlertService>();

var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

// Initialize DB, seed admin, load runtime settings
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // Create AppSettings table for existing deployments (EnsureCreated won't add new tables)
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "AppSettings" (
            "Key"   text NOT NULL,
            "Value" text NOT NULL,
            CONSTRAINT "PK_AppSettings" PRIMARY KEY ("Key")
        )
        """);

    if (!db.Users.Any())
    {
        db.Users.Add(new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
            Role = "Admin"
        });
        db.SaveChanges();
        Console.WriteLine("Created default admin user: admin / admin");
    }

    // Bootstrap RuntimeSettings: env/appsettings first, then DB overrides
    var runtimeSettings = app.Services.GetRequiredService<RuntimeSettings>();
    runtimeSettings.LoadFromConfig(app.Configuration);
    var dbSettings = db.AppSettings.ToList();
    runtimeSettings.LoadFromDb(dbSettings);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

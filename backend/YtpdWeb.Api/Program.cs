using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using YtpdWeb.Api.Auth;
using YtpdWeb.Api.Bot;
using YtpdWeb.Api.Data;
using YtpdWeb.Api.Hubs;
using YtpdWeb.Api.Services;

// `dotnet YtpdWeb.Api.dll hash-password <password>` prints a PasswordHash
// value to paste into Auth__PasswordHash, without needing a separate tool.
if (args.Length == 2 && args[0] == "hash-password")
{
    Console.WriteLine(YtpdWeb.Api.Auth.PasswordHasher.Hash(args[1]));
    return;
}

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.Configure<StorageOptions>(config.GetSection(StorageOptions.SectionName));
builder.Services.Configure<FfmpegOptions>(config.GetSection(FfmpegOptions.SectionName));
builder.Services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
builder.Services.Configure<AuthCredentialsOptions>(config.GetSection(AuthCredentialsOptions.SectionName));
builder.Services.Configure<TelegramOptions>(config.GetSection(TelegramOptions.SectionName));
builder.Services.AddHttpClient<TelegramClient>();
builder.Services.AddSingleton<PendingSelectionCache>();

// Desktop (local sidecar, offline-capable) always uses SQLite via
// Database:Path. Hosted web deployments set ConnectionStrings:Postgres
// instead (e.g. a Supabase connection string) to get a real managed DB
// that survives server rebuilds - same AppDbContext, same migrations,
// just a different provider picked at startup.
var postgresConnectionString = config["ConnectionStrings:Postgres"];
var usingPostgres = !string.IsNullOrWhiteSpace(postgresConnectionString);
if (usingPostgres)
{
    builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(postgresConnectionString));
}
else
{
    var dbPath = config["Database:Path"] ?? "/data/ytpd.db";
    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
    builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
}

builder.Services.AddSingleton<DownloadQueue>();
builder.Services.AddSingleton<YoutubeResolverService>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddHostedService<DownloadWorker>();
builder.Services.AddHostedService<CleanupService>();

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var allowedOrigin = config["Cors:AllowedOrigin"] ?? "http://localhost:3000";
builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
    .WithOrigins(allowedOrigin)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var jwtSecret = config["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret))
    throw new InvalidOperationException("Jwt:Secret must be set (env var Jwt__Secret) to a long random string before starting.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = config["Jwt:Issuer"] ?? "ytpd-web",
            ValidateAudience = true,
            ValidAudience = config["Jwt:Issuer"] ?? "ytpd-web",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
        };

        // SignalR can't attach an Authorization header to its websocket
        // handshake, so it passes the token as a query string instead.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Postgres (hosted): the target database (e.g. Supabase's "postgres")
    // already exists before we ever connect, so EnsureCreated() would see
    // "database exists" and stop there without creating our tables at all.
    // Migrate() actually checks/applies the schema itself.
    // SQLite (desktop): the .db file genuinely doesn't exist on first run,
    // so EnsureCreated() creating it from the model directly is simpler and
    // already proven - no migrations tracked for that provider.
    if (usingPostgres) db.Database.Migrate();
    else db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ProgressHub>("/hubs/progress");
app.MapGet("/healthz", () => Results.Ok("ok")).AllowAnonymous();

app.Run();

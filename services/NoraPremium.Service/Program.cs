using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using NoraPremium.Service;

var builder = WebApplication.CreateBuilder(args);
var dataRoot = Environment.GetEnvironmentVariable("NORA_PREMIUM_DATA_ROOT") ?? "/var/lib/nora-premium";
var pepper = Environment.GetEnvironmentVariable("NORA_PREMIUM_PEPPER")
    ?? throw new InvalidOperationException("NORA_PREMIUM_PEPPER is required.");
var bootstrapPassword = Environment.GetEnvironmentVariable("NORA_PREMIUM_ADMIN_PASSWORD")
    ?? throw new InvalidOperationException("NORA_PREMIUM_ADMIN_PASSWORD is required for first start.");

Directory.CreateDirectory(dataRoot);
var dpRoot = Path.Combine(dataRoot, "dpkeys");
Directory.CreateDirectory(dpRoot);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpRoot))
    .SetApplicationName("NORA.Premium.Admin");
builder.Services.AddSingleton(new PremiumCrypto(dataRoot, pepper));
builder.Services.AddSingleton(sp => new PremiumDb(dataRoot, sp.GetRequiredService<PremiumCrypto>()));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-nora-premium-admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/admin/login";
        options.AccessDeniedPath = "/admin/login";
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        RemoteIp(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 6,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("activation", context => RateLimitPartition.GetFixedWindowLimiter(
        RemoteIp(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
});

var app = builder.Build();
var db = app.Services.GetRequiredService<PremiumDb>();
db.Initialize(bootstrapPassword);

app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    if (context.Request.IsHttps)
        context.Response.Headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'self'; script-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
    if (context.Request.Path.StartsWithSegments("/admin"))
        context.Response.Headers.CacheControl = "no-store, max-age=0";
    await next();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/admin"));
app.MapGet("/health", () => Results.Json(new { status = "ok", service = "nora-premium" }));
app.MapGet("/api/v1/public-key", (PremiumCrypto crypto) => Results.Text(crypto.PublicKeyPem, "text/plain", Encoding.UTF8));

app.MapPost("/api/v1/activate", (ActivationRequest request, HttpContext context, PremiumCrypto crypto, PremiumDb store) =>
{
    var normalized = PremiumCrypto.NormalizeCode(request.Code);
    if (normalized.Length == 0 || !ValidInstallation(request.InstallationId) || request.AppVersion.Length > 40)
        return Results.Json(new ApiError("invalid_request", "Check the activation code and try again."), statusCode: 400);

    var result = store.Activate(normalized, request.InstallationId, request.AppVersion, RemoteIp(context));
    if (result.Grant is null)
    {
        var (status, message, code) = result.Status switch
        {
            "activation_limit" => (result.Status, "This code has reached its device limit.", 409),
            "revoked" or "device_revoked" => (result.Status, "This Premium access has been revoked.", 403),
            "expired" => (result.Status, "This Premium code has expired.", 403),
            _ => ("invalid_code", "The Premium code is invalid.", 400)
        };
        return Results.Json(new ApiError(status, message), statusCode: code);
    }
    return Results.Json(CreateToken(crypto, result.Grant, request.InstallationId));
}).RequireRateLimiting("activation");

app.MapPost("/api/v1/refresh", (RefreshRequest request, HttpContext context, PremiumCrypto crypto, PremiumDb store) =>
{
    if (!ValidInstallation(request.InstallationId) || request.AppVersion.Length > 40 || !crypto.TryVerify(request.Token, out var payload))
        return Results.Json(new ApiError("invalid_token", "Premium access could not be verified."), statusCode: 400);
    if (!string.Equals(payload.InstallationId, request.InstallationId, StringComparison.Ordinal) ||
        payload.ValidUntil + (long)TimeSpan.FromDays(30).TotalSeconds < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        return Results.Json(new ApiError("invalid_token", "Premium access could not be verified."), statusCode: 403);
    if (!store.RefreshAllowed(payload.LicenseId, payload.ActivationId, request.InstallationId, request.AppVersion, RemoteIp(context)))
        return Results.Json(new ApiError("revoked", "Premium access is no longer active."), statusCode: 403);
    return Results.Json(CreateToken(crypto, new ActivationGrant(payload.LicenseId, payload.ActivationId), request.InstallationId));
}).RequireRateLimiting("activation");

app.MapGet("/admin/app.css", () => Results.Text(AdminHtml.Css, "text/css", Encoding.UTF8));
app.MapGet("/admin/app.js", () => Results.Text(AdminHtml.Js, "text/javascript", Encoding.UTF8));
app.MapGet("/admin/login", (HttpContext context) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
        return Results.Redirect("/admin");
    var csrf = NewCsrf();
    SetLoginCsrf(context, csrf);
    return Results.Content(AdminHtml.Login(null, csrf), "text/html", Encoding.UTF8);
});
app.MapPost("/admin/login", async (HttpContext context, PremiumDb store) =>
{
    if (!context.Request.HasFormContentType)
        return Results.BadRequest();
    var form = await context.Request.ReadFormAsync();
    var csrf = form["csrf"].ToString();
    var cookieCsrf = context.Request.Cookies["__Host-nora-login-csrf"] ?? "";
    if (!SecureEquals(csrf, cookieCsrf))
        return Results.BadRequest();
    var username = form["username"].ToString().Trim().ToLowerInvariant();
    var password = form["password"].ToString();
    if (!store.VerifyAdmin(username, password))
    {
        store.WriteAudit("admin.login_failed", $"username={username}", RemoteIp(context));
        var nextCsrf = NewCsrf();
        SetLoginCsrf(context, nextCsrf);
        return Results.Content(AdminHtml.Login("Неверный логин или пароль.", nextCsrf), "text/html", Encoding.UTF8, 401);
    }

    var adminCsrf = NewCsrf();
    var identity = new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.Name, username),
        new Claim("csrf", adminCsrf)
    }, CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    context.Response.Cookies.Delete("__Host-nora-login-csrf", SecureCookie());
    store.WriteAudit("admin.login", $"username={username}", RemoteIp(context));
    return Results.Redirect("/admin");
}).RequireRateLimiting("login");

app.MapGet("/admin", (HttpContext context, PremiumDb store) =>
{
    var csrf = context.User.FindFirstValue("csrf") ?? "";
    var message = context.Request.Query["message"].ToString();
    return Results.Content(AdminHtml.Dashboard(store.Dashboard(), csrf, message), "text/html", Encoding.UTF8);
}).RequireAuthorization();

app.MapPost("/admin/codes/generate", async (HttpContext context, PremiumDb store) =>
{
    var form = await RequireAdminForm(context);
    if (form is null)
        return Results.BadRequest();
    _ = int.TryParse(form["count"], out var count);
    _ = int.TryParse(form["max_devices"], out var maxDevices);
    var note = form["note"].ToString();
    var codes = store.CreateLicenses(count, maxDevices, note, validDays: 0, RemoteIp(context));
    return Results.Content(AdminHtml.Generated(codes, context.User.FindFirstValue("csrf") ?? ""), "text/html", Encoding.UTF8);
}).RequireAuthorization();

app.MapPost("/admin/licenses/{id}/toggle", async (string id, HttpContext context, PremiumDb store) =>
{
    var form = await RequireAdminForm(context);
    if (form is null || !Guid.TryParseExact(id, "N", out _))
        return Results.BadRequest();
    var revoked = string.Equals(form["revoked"], "true", StringComparison.OrdinalIgnoreCase);
    store.SetLicenseRevoked(id, revoked, RemoteIp(context));
    return Results.Redirect("/admin?message=" + Uri.EscapeDataString(revoked ? "Лицензия отозвана." : "Лицензия восстановлена."));
}).RequireAuthorization();

app.MapPost("/admin/activations/{id}/toggle", async (string id, HttpContext context, PremiumDb store) =>
{
    var form = await RequireAdminForm(context);
    if (form is null || !Guid.TryParseExact(id, "N", out _))
        return Results.BadRequest();
    var revoked = string.Equals(form["revoked"], "true", StringComparison.OrdinalIgnoreCase);
    store.SetActivationRevoked(id, revoked, RemoteIp(context));
    return Results.Redirect("/admin?message=" + Uri.EscapeDataString(revoked ? "Устройство заблокировано." : "Устройство восстановлено."));
}).RequireAuthorization();

app.MapPost("/admin/password", async (HttpContext context, PremiumDb store) =>
{
    var form = await RequireAdminForm(context);
    if (form is null)
        return Results.BadRequest();
    var next = form["new_password"].ToString();
    var confirm = form["confirm_password"].ToString();
    if (next != confirm || !store.ChangePassword(context.User.Identity?.Name ?? "admin", form["current_password"].ToString(), next))
        return Results.Redirect("/admin?message=" + Uri.EscapeDataString("Пароль не изменён. Проверьте текущий пароль и минимум 14 символов."));
    store.WriteAudit("admin.password_changed", "username=admin", RemoteIp(context));
    await context.SignOutAsync();
    return Results.Redirect("/admin/login");
}).RequireAuthorization();

app.MapPost("/admin/logout", async (HttpContext context, PremiumDb store) =>
{
    var form = await RequireAdminForm(context);
    if (form is null)
        return Results.BadRequest();
    store.WriteAudit("admin.logout", $"username={context.User.Identity?.Name}", RemoteIp(context));
    await context.SignOutAsync();
    return Results.Redirect("/admin/login");
}).RequireAuthorization();

app.Run();

static TokenResponse CreateToken(PremiumCrypto crypto, ActivationGrant grant, string installationId)
{
    var now = DateTimeOffset.UtcNow;
    var entitlements = new[] { "appearance.equalizer", "appearance.server_slideshow" };
    var payload = new PremiumTokenPayload(
        1,
        grant.LicenseId,
        grant.ActivationId,
        installationId,
        "visual_premium",
        entitlements,
        now.ToUnixTimeSeconds(),
        now.AddDays(7).ToUnixTimeSeconds(),
        now.AddDays(90).ToUnixTimeSeconds(),
        crypto.KeyId);
    return new TokenResponse("active", crypto.Sign(payload), payload.ValidUntil, entitlements);
}

static bool ValidInstallation(string value)
    => Guid.TryParse(value, out _) && value.Length <= 64;

static string RemoteIp(HttpContext context)
    => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static string NewCsrf() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

static CookieOptions SecureCookie() => new()
{
    Secure = true,
    HttpOnly = true,
    SameSite = SameSiteMode.Strict,
    Path = "/",
    MaxAge = TimeSpan.FromMinutes(15)
};

static void SetLoginCsrf(HttpContext context, string value)
    => context.Response.Cookies.Append("__Host-nora-login-csrf", value, SecureCookie());

static bool SecureEquals(string left, string right)
{
    if (left.Length == 0 || left.Length != right.Length)
        return false;
    return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}

static async Task<IFormCollection?> RequireAdminForm(HttpContext context)
{
    if (!context.Request.HasFormContentType)
        return null;
    var form = await context.Request.ReadFormAsync();
    var expected = context.User.FindFirstValue("csrf") ?? "";
    return SecureEquals(form["csrf"].ToString(), expected) ? form : null;
}

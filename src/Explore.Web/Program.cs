using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Explore.Shared;
using Explore.Web.Components;
using Explore.Web.Data;
using Microsoft.EntityFrameworkCore;
using Explore.Web.Security;
using Explore.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddRazorPages(options => options.Conventions.AuthorizeFolder("/Admin"))
    .AddMvcOptions(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AdminCredentials>();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
var keyDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keyDirectory);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
    .SetApplicationName("Explore.Web");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/account/login";
    options.Cookie.Name = "Explore.Admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnValidatePrincipal = context =>
    {
        var credentials = context.HttpContext.RequestServices.GetRequiredService<AdminCredentials>();
        if (!credentials.IsConfigured || context.Principal?.FindFirst("credential_stamp")?.Value != credentials.Stamp)
            context.RejectPrincipal();
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddHttpClient<AdminApiClient>(http =>
{
    http.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5080/");
    http.Timeout = TimeSpan.FromSeconds(15);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
builder.Services.AddHttpClient<ExploreApiClient>(http => {
    http.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5080/");
    http.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddDbContext<ExploreDb>(options => {
    if (builder.Configuration["Database:Provider"] == "SqlServer")
        options.UseSqlServer(builder.Configuration.GetConnectionString("Explore")
            ?? throw new InvalidOperationException("ConnectionStrings:Explore is required."));
    else {
        Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
        options.UseSqlite(builder.Configuration.GetConnectionString("Explore") ?? "Data Source=App_Data/explore.db");
    }
});
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => {
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));
var app = builder.Build();
app.UseExceptionHandler();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapOpenApi();
app.MapGet("/health", async (ExploreDb db) => await db.Database.CanConnectAsync()
    ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(503));
var api = app.MapGroup("/api/v1");
api.MapGet("/places", async (ExploreDb db, string? q, string? category) => {
    var query = db.Places.AsNoTracking().Where(p => p.IsPublished);
    if (!string.IsNullOrWhiteSpace(q)) query = query.Where(p => p.Name.Contains(q) || p.Summary.Contains(q));
    if (!string.IsNullOrWhiteSpace(category)) query = query.Where(p => p.Category == category);
    return await query.OrderBy(p => p.Id).Take(100).ToArrayAsync();
});
api.MapGet("/places/{id:int}", async (int id, ExploreDb db) =>
    await db.Places.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.IsPublished) is { } place
        ? Results.Ok(place) : Results.NotFound());
api.MapGet("/trails", async (ExploreDb db) =>
{
    var published = await db.Places.Where(p => p.IsPublished).Select(p => p.Id).ToArrayAsync();
    return Seed.Trails.Where(t => t.PlaceIds.All(published.Contains)).ToArray();
});
// This key is for server-side content administration. Never include it in a mobile app.
var admin = api.MapGroup("/admin").AddEndpointFilter(async (context, next) => {
    context.HttpContext.Response.Headers.CacheControl = "no-store";
    if (context.HttpContext.User.IsInRole("Admin"))
    {
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
        {
            try { await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>()
                .ValidateRequestAsync(context.HttpContext); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(new { error = "Invalid antiforgery token." }); }
        }
        return await next(context);
    }
    var expected = app.Configuration["Admin:ApiKey"];
    var supplied = context.HttpContext.Request.Headers["X-Admin-Key"].ToString();
    if (string.IsNullOrEmpty(expected) || expected.Length < 32 ||
        !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)))) return Results.Unauthorized();
    return await next(context);
});
admin.MapGet("/places", async (ExploreDb db) => await db.Places.AsNoTracking().OrderByDescending(p => p.Id).ToArrayAsync());
admin.MapGet("/places/{id:int}", async (int id, ExploreDb db) =>
    await db.Places.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id) is { } place
        ? Results.Ok(place) : Results.NotFound());
admin.MapPost("/places", async (Place place, ExploreDb db) => {
    place.ImageUrl = place.ImageUrl?.Trim() ?? "";
    place.Theme ??= "forest";
    var errors = Validate(place);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    place.Id = 0;
    db.Places.Add(place);
    await db.SaveChangesAsync();
    return Results.Created($"/api/v1/places/{place.Id}", place);
});
admin.MapPut("/places/{id:int}", async (int id, Place place, ExploreDb db) => {
    place.ImageUrl = place.ImageUrl?.Trim() ?? "";
    place.Theme ??= "forest";
    var errors = Validate(place);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var existing = await db.Places.FindAsync(id);
    if (existing is null) return Results.NotFound();
    place.Id = id;
    db.Entry(existing).CurrentValues.SetValues(place);
    await db.SaveChangesAsync();
    return Results.Ok(existing);
});
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapRazorPages();
await app.Services.GetRequiredService<AdminCredentials>().InitializeAsync();
using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<ExploreDb>();
    await SchemaUpgrade.ApplyAsync(db);
    if (builder.Configuration.GetValue("Database:SeedDemo", true) && !await db.Places.AnyAsync()) {
        db.Places.AddRange(Seed.Places);
        await db.SaveChangesAsync();
    }
}
app.Run();
static Dictionary<string, string[]> Validate(Place place) {
    var results = new List<ValidationResult>();
    Validator.TryValidateObject(place, new ValidationContext(place), results, true);
    return results.SelectMany(r => r.MemberNames.Select(m => (m, r.ErrorMessage)))
        .GroupBy(x => x.m).ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Invalid").ToArray());
}

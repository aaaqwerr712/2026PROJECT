using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Explore.Shared;
using Explore.Web.Components;
using Explore.Web.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
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
app.UseCors();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapOpenApi();
app.MapGet("/health", async (ExploreDb db) => await db.Database.CanConnectAsync()
    ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(503));
var api = app.MapGroup("/api/v1");
api.MapGet("/places", async (ExploreDb db, string? q, string? category) => {
    var query = db.Places.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(q)) query = query.Where(p => p.Name.Contains(q) || p.Summary.Contains(q));
    if (!string.IsNullOrWhiteSpace(category)) query = query.Where(p => p.Category == category);
    return await query.OrderBy(p => p.Id).Take(100).ToArrayAsync();
});
api.MapGet("/places/{id:int}", async (int id, ExploreDb db) =>
    await db.Places.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id) is { } place
        ? Results.Ok(place) : Results.NotFound());
api.MapGet("/trails", () => Seed.Trails);
// This key is for server-side content administration. Never include it in a mobile app.
var admin = api.MapGroup("/admin").AddEndpointFilter(async (context, next) => {
    var expected = app.Configuration["Admin:ApiKey"];
    var supplied = context.HttpContext.Request.Headers["X-Admin-Key"].ToString();
    if (string.IsNullOrEmpty(expected) || expected.Length < 32 ||
        !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)))) return Results.Unauthorized();
    return await next(context);
});
admin.MapPost("/places", async (Place place, ExploreDb db) => {
    var errors = Validate(place);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    place.Id = 0;
    db.Places.Add(place);
    await db.SaveChangesAsync();
    return Results.Created($"/api/v1/places/{place.Id}", place);
});
admin.MapPut("/places/{id:int}", async (int id, Place place, ExploreDb db) => {
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
using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<ExploreDb>();
    // Initial prototype only; introduce migrations before evolving a production schema.
    await db.Database.EnsureCreatedAsync();
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

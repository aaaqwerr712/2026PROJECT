using System.Net.Http.Json;
using Explore.Shared;
using Microsoft.AspNetCore.Antiforgery;

namespace Explore.Web.Services;

// The admin HTML forms use the same API as external clients, without exposing a service key.
public sealed class AdminApiClient(HttpClient client, IHttpContextAccessor accessor, IAntiforgery antiforgery)
{
    private HttpRequestMessage Request(HttpMethod method, string path, Place? place = null)
    {
        var context = accessor.HttpContext ?? throw new InvalidOperationException("A request is required.");
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Cookie", context.Request.Headers.Cookie.ToString());
        if (place is not null)
        {
            request.Content = JsonContent.Create(place);
            request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", antiforgery.GetTokens(context).RequestToken);
        }
        return request;
    }

    public async Task<Place[]> ListAsync()
    {
        using var request = Request(HttpMethod.Get, "api/v1/admin/places");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Place[]>() ?? [];
    }

    public async Task<Place?> GetAsync(int id)
    {
        using var request = Request(HttpMethod.Get, $"api/v1/admin/places/{id}");
        using var response = await client.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Place>();
    }

    public async Task<bool> SaveAsync(Place place, bool create)
    {
        using var request = Request(create ? HttpMethod.Post : HttpMethod.Put,
            create ? "api/v1/admin/places" : $"api/v1/admin/places/{place.Id}", place);
        using var response = await client.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }
}

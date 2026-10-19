using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;

namespace Explore.Shared;

public class Place
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, StringLength(40)] public string Category { get; set; } = "";
    [Required, StringLength(300)] public string Summary { get; set; } = "";
    [Required, StringLength(4000)] public string Description { get; set; } = "";
    [Required, StringLength(200)] public string Address { get; set; } = "";
    [Range(-90, 90)] public double Latitude { get; set; }
    [Range(-180, 180)] public double Longitude { get; set; }
    [Range(5, 480)] public int VisitMinutes { get; set; } = 30;
    [StringLength(20)] public string Theme { get; set; } = "forest";
}

public record Trail(int Id, string Name, string Description, int Minutes, int[] PlaceIds);

// Reference this assembly from both the website and a future MAUI application.
public class ExploreApiClient(HttpClient http)
{
    public async Task<Place[]> GetPlacesAsync(string? query = null, string? category = null) =>
        await http.GetFromJsonAsync<Place[]>($"api/v1/places?q={Uri.EscapeDataString(query ?? "")}&category={Uri.EscapeDataString(category ?? "")}") ?? [];
    public async Task<Place?> GetPlaceAsync(int id)
    {
        using var response = await http.GetAsync($"api/v1/places/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Place>();
    }
    public async Task<Trail[]> GetTrailsAsync() => await http.GetFromJsonAsync<Trail[]>("api/v1/trails") ?? [];
}

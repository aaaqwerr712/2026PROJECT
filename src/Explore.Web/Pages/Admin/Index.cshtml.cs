using Explore.Shared;
using Explore.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Explore.Web.Pages.Admin;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(AdminApiClient api) : PageModel
{
    public Place[] Places { get; private set; } = [];
    public bool Error { get; private set; }
    [BindProperty(SupportsGet = true)] public bool Saved { get; set; }
    public async Task OnGetAsync()
    {
        try { Places = await api.ListAsync(); }
        catch (HttpRequestException) { Error = true; }
        catch (TaskCanceledException) { Error = true; }
    }
}

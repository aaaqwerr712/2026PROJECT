using Explore.Shared;
using Explore.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Explore.Web.Pages.Admin;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class EditModel(AdminApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public Place Input { get; set; } = new();
    public bool LoadError { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!Id.HasValue) return Page();
        try
        {
            var place = await api.GetAsync(Id.Value);
            if (place is null) return NotFound();
            Input = place;
        }
        catch (HttpRequestException) { LoadError = true; }
        catch (TaskCanceledException) { LoadError = true; }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Input.ImageUrl = Input.ImageUrl?.Trim() ?? "";
        Input.Id = Id ?? 0;
        if (!ModelState.IsValid) return Page();
        try
        {
            if (await api.SaveAsync(Input, !Id.HasValue))
                return LocalRedirect("/admin?saved=true");
            ModelState.AddModelError("", "資料格式不正確，請檢查欄位後再儲存。");
        }
        catch (HttpRequestException) { ModelState.AddModelError("", "儲存失敗，請確認景點仍存在後再試一次。"); }
        catch (TaskCanceledException) { ModelState.AddModelError("", "連線逾時，請先查看管理列表確認是否已儲存。"); }
        return Page();
    }
}

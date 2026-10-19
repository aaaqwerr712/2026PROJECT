using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Explore.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.Web.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("login")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class LoginModel(AdminCredentials credentials) : PageModel
{
    [BindProperty, Required, StringLength(100)] public string Username { get; set; } = "";
    [BindProperty, Required, StringLength(256)] public string Password { get; set; } = "";
    public bool Configured => credentials.IsConfigured;
    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? LocalRedirect("/admin") : Page();
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid || !credentials.Verify(Username, Password))
        {
            ModelState.AddModelError("", "帳號或密碼不正確，請再試一次。");
            Password = "";
            ModelState.Remove(nameof(Password));
            return Page();
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Username),
            new Claim(ClaimTypes.Name, Username),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("credential_stamp", credentials.Stamp)
        ], CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false });
        return LocalRedirect("/admin");
    }
}

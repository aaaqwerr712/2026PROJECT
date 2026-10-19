using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;

namespace Explore.Web.Security;

public sealed record AdminAccount(string Username, string PasswordHash);

public sealed class AdminCredentials(IConfiguration config, IWebHostEnvironment environment)
{
    private readonly PasswordHasher<AdminAccount> hasher = new();
    private AdminAccount? account;
    public bool IsConfigured => account is not null;
    public string Stamp => account is null ? "" :
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(account.PasswordHash)));

    public async Task InitializeAsync()
    {
        var configuredHash = config["Admin:PasswordHash"];
        if (!string.IsNullOrWhiteSpace(configuredHash))
        {
            account = new(config["Admin:Username"] ?? "admin", configuredHash);
            return;
        }
        // Local development only. Production must explicitly configure a password hash.
        if (!environment.IsDevelopment()) return;
        var folder = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "admin-account.json");
        if (File.Exists(path))
        {
            account = JsonSerializer.Deserialize<AdminAccount>(await File.ReadAllTextAsync(path));
            return;
        }
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        var initial = new AdminAccount("admin", "");
        account = initial with { PasswordHash = hasher.HashPassword(initial, password) };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(account));
        await File.WriteAllTextAsync(Path.Combine(folder, "admin-login.txt"),
            $"本機開發專用，請勿上傳或分享。\n登入網址：http://localhost:5080/account/login\n帳號：admin\n密碼：{password}\n帳號資料與此檔案皆由 .gitignore 排除。\n");
    }

    public bool Verify(string username, string password)
    {
        if (account is null || password.Length > 256) return false;
        try
        {
            var result = hasher.VerifyHashedPassword(account, account.PasswordHash, password);
            return result != PasswordVerificationResult.Failed &&
                string.Equals(username, account.Username, StringComparison.Ordinal);
        }
        catch (FormatException) { return false; }
    }
}

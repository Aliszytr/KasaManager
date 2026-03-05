using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using KasaManager.Domain.Identity;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Collections.Concurrent;

namespace KasaManager.Web.Controllers;

/// <summary>
/// Kimlik doğrulama controller'ı.
/// Login, Logout, AccessDenied işlemleri.
/// </summary>
[AllowAnonymous]
public sealed class AccountController : Controller
{
    private readonly KasaManagerDbContext _db;
    private readonly ILogger<AccountController> _logger;

    // Brute-force koruması: IP başına başarısız deneme sayacı
    private static readonly ConcurrentDictionary<string, (int Count, DateTime LastAttempt)> _failedAttempts = new();

    public AccountController(KasaManagerDbContext db, ILogger<AccountController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
    {
        // Brute-force koruması: IP başına kontrol
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (_failedAttempts.TryGetValue(clientIp, out var attempts))
        {
            // 15 dakika sonra sayaç sıfırlanır
            if (attempts.LastAttempt.AddMinutes(15) < DateTime.UtcNow)
                _failedAttempts.TryRemove(clientIp, out _);
            else if (attempts.Count >= 5)
            {
                _logger.LogWarning("Brute-force koruması: {IP} kilitlendi ({Count} deneme)", clientIp, attempts.Count);
                ViewBag.Error = "Çok fazla başarısız deneme. Lütfen 15 dakika bekleyin.";
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ViewBag.Error = "Kullanıcı adı ve şifre gereklidir.";
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        var user = await _db.KasaUsers
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        if (user is null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            // Başarısız deneme sayacını artır
            _failedAttempts.AddOrUpdate(clientIp,
                _ => (1, DateTime.UtcNow),
                (_, old) => (old.Count + 1, DateTime.UtcNow));

            _logger.LogWarning("Başarısız giriş denemesi: {Username} (IP: {IP})", username, clientIp);
            ViewBag.Error = "Kullanıcı adı veya şifre hatalı.";
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        // Başarılı giriş — sayacı sıfırla
        _failedAttempts.TryRemove(clientIp, out _);

        // Claims oluştur
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, user.DisplayName),
            new(ClaimTypes.Role, user.Role),
            new("UserId", user.Id.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

        // Son giriş tarihini güncelle
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Kullanıcı giriş yaptı: {Username} ({Role})", user.Username, user.Role);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var username = User.Identity?.Name;
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _logger.LogInformation("Kullanıcı çıkış yaptı: {Username}", username);
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    // ────────────── ŞİFRE DEĞİŞTİR ──────────────

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword()
    {
        return View(new ChangePasswordViewModel());
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return RedirectToAction("Login");

        var user = await _db.KasaUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null)
            return RedirectToAction("Login");

        // Mevcut şifreyi doğrula
        if (!BCrypt.Net.BCrypt.Verify(model.CurrentPassword, user.PasswordHash))
        {
            ModelState.AddModelError("CurrentPassword", "Mevcut şifre hatalı.");
            return View(model);
        }

        // Yeni şifreyi kaydet
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Kullanıcı şifresini değiştirdi: {Username}", username);
        TempData["Success"] = "Şifreniz başarıyla değiştirildi.";
        return RedirectToAction(nameof(ChangePassword));
    }
}

public sealed class ChangePasswordViewModel
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Mevcut şifre gereklidir.")]
    public string? CurrentPassword { get; set; }

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Yeni şifre gereklidir.")]
    [System.ComponentModel.DataAnnotations.MinLength(6, ErrorMessage = "Şifre en az 6 karakter olmalıdır.")]
    public string? NewPassword { get; set; }

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Şifre tekrarı gereklidir.")]
    [System.ComponentModel.DataAnnotations.Compare("NewPassword", ErrorMessage = "Şifreler eşleşmiyor.")]
    public string? ConfirmPassword { get; set; }
}

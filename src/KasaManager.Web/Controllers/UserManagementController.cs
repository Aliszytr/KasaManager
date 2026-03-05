using KasaManager.Domain.Identity;
using KasaManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Web.Controllers;

/// <summary>
/// Kullanıcı yönetimi — CRUD + şifre sıfırlama.
/// Sadece Admin rolü erişebilir.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed class UserManagementController : Controller
{
    private readonly KasaManagerDbContext _db;
    private readonly ILogger<UserManagementController> _logger;

    public UserManagementController(KasaManagerDbContext db, ILogger<UserManagementController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ────────────── LİSTE ──────────────

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var users = await _db.KasaUsers
            .OrderBy(u => u.Username)
            .ToListAsync(ct);
        return View(users);
    }

    // ────────────── EKLE ──────────────

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateUserViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(model);

        // Kullanıcı adı benzersiz mi?
        if (await _db.KasaUsers.AnyAsync(u => u.Username == model.Username, ct))
        {
            ModelState.AddModelError("Username", "Bu kullanıcı adı zaten mevcut.");
            return View(model);
        }

        var user = new KasaUser
        {
            Username = model.Username!.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
            DisplayName = model.DisplayName!.Trim(),
            Role = model.Role ?? "User",
            IsActive = model.IsActive
        };

        _db.KasaUsers.Add(user);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Yeni kullanıcı oluşturuldu: {Username} ({Role})", user.Username, user.Role);
        TempData["Success"] = $"'{user.Username}' kullanıcısı başarıyla oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    // ────────────── DÜZENLE ──────────────

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var user = await _db.KasaUsers.FindAsync(new object[] { id }, ct);
        if (user is null) return NotFound();

        var model = new EditUserViewModel
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role,
            IsActive = user.IsActive
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditUserViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _db.KasaUsers.FindAsync(new object[] { model.Id }, ct);
        if (user is null) return NotFound();

        user.DisplayName = model.DisplayName!.Trim();
        user.Role = model.Role ?? "User";
        user.IsActive = model.IsActive;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Kullanıcı güncellendi: {Username} ({Role}, Active={IsActive})",
            user.Username, user.Role, user.IsActive);
        TempData["Success"] = $"'{user.Username}' kullanıcısı güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    // ────────────── ŞİFRE SIFIRLA (Admin) ──────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int id, string newPassword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            TempData["Error"] = "Şifre en az 6 karakter olmalıdır.";
            return RedirectToAction(nameof(Index));
        }

        var user = await _db.KasaUsers.FindAsync(new object[] { id }, ct);
        if (user is null) return NotFound();

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Şifre sıfırlandı: {Username} (admin tarafından)", user.Username);
        TempData["Success"] = $"'{user.Username}' kullanıcısının şifresi sıfırlandı.";
        return RedirectToAction(nameof(Index));
    }

    // ────────────── SİL ──────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var currentUsername = User.Identity?.Name;
        var user = await _db.KasaUsers.FindAsync(new object[] { id }, ct);
        if (user is null) return NotFound();

        // Kendi kendini silemez
        if (user.Username.Equals(currentUsername, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Kendinizi silemezsiniz!";
            return RedirectToAction(nameof(Index));
        }

        _db.KasaUsers.Remove(user);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Kullanıcı silindi: {Username} (silen: {Admin})", user.Username, currentUsername);
        TempData["Success"] = $"'{user.Username}' kullanıcısı silindi.";
        return RedirectToAction(nameof(Index));
    }
}

// ────────────── ViewModels ──────────────

public sealed class CreateUserViewModel
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Kullanıcı adı gereklidir.")]
    [System.ComponentModel.DataAnnotations.StringLength(50, MinimumLength = 3, ErrorMessage = "Kullanıcı adı 3-50 karakter olmalıdır.")]
    public string? Username { get; set; }

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Görünen ad gereklidir.")]
    public string? DisplayName { get; set; }

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Şifre gereklidir.")]
    [System.ComponentModel.DataAnnotations.MinLength(6, ErrorMessage = "Şifre en az 6 karakter olmalıdır.")]
    public string? Password { get; set; }

    public string? Role { get; set; } = "User";
    public bool IsActive { get; set; } = true;
}

public sealed class EditUserViewModel
{
    public int Id { get; set; }

    public string? Username { get; set; }

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Görünen ad gereklidir.")]
    public string? DisplayName { get; set; }

    public string? Role { get; set; } = "User";
    public bool IsActive { get; set; } = true;
}

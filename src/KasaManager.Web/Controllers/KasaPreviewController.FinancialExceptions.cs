#nullable enable
using System.ComponentModel.DataAnnotations;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.FinancialExceptions;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KasaManager.Web.Controllers;

/// <summary>
/// Financial Exceptions API endpoints — partial (KasaPreviewController).
/// Faz 1: CRUD + Karar durumu + Yaşam döngüsü.
/// </summary>
[Authorize]
public sealed partial class KasaPreviewController
{
    // =====================================================================
    // Financial Exceptions Actions
    // =====================================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFinansalIstisna(
        [FromForm] FinansalIstisnaFormModel form,
        CancellationToken ct)
    {
        var userName = User.Identity?.Name ?? "anonymous";

        var request = new FinansalIstisnaCreateRequest(
            IslemTarihi: form.IslemTarihi,
            Tur: form.Tur,
            Kategori: form.Kategori,
            HesapTuru: form.HesapTuru,
            BeklenenTutar: form.BeklenenTutar,
            GerceklesenTutar: form.GerceklesenTutar,
            SistemeGirilenTutar: form.SistemeGirilenTutar,
            EtkiYonu: form.EtkiYonu,
            Neden: form.Neden,
            Aciklama: form.Aciklama,
            HedefHesapAciklama: form.HedefHesapAciklama,
            OlusturanKullanici: userName);

        try
        {
            await _finansalIstisna.CreateAsync(request, ct);
        }
        catch (ValidationException ex)
        {
            _log.LogWarning("Finansal istisna doğrulama hatası: {Message}", ex.Message);
            TempData["ErrorMessage"] = $"❌ Doğrulama hatası: {ex.Message}";
            return RedirectToAction("Index", new { kasaType = form.RedirectKasaType });
        }

        TempData["SuccessMessage"] = "✅ Finansal istisna başarıyla oluşturuldu (İnceleme Bekliyor).";
        return RedirectToAction("Index", new { kasaType = form.RedirectKasaType });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetFinansalIstisnaKarar(
        Guid id, KararDurumu karar, string? redirectKasaType, CancellationToken ct)
    {
        var userName = User.Identity?.Name ?? "anonymous";
        var result = await _finansalIstisna.SetKararAsync(id, karar, userName, ct);

        if (result is null)
        {
            TempData["ErrorMessage"] = "❌ İstisna kaydı bulunamadı.";
        }
        else
        {
            var label = karar == KararDurumu.Onaylandi ? "onaylandı ✅" : "reddedildi ❌";
            TempData["SuccessMessage"] = $"Finansal istisna {label}.";
        }

        return RedirectToAction("Index", new { kasaType = redirectKasaType });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetFinansalIstisnaDurum(
        Guid id, IstisnaDurumu durum, decimal? sistemeGirilenTutar,
        string? redirectKasaType, CancellationToken ct)
    {
        var userName = User.Identity?.Name ?? "anonymous";
        var result = await _finansalIstisna.SetDurumAsync(id, durum, userName, sistemeGirilenTutar, ct);

        if (result is null)
        {
            TempData["ErrorMessage"] = "❌ İstisna kaydı bulunamadı.";
        }
        else
        {
            TempData["SuccessMessage"] = $"İstisna durumu → {durum} olarak güncellendi.";
        }

        return RedirectToAction("Index", new { kasaType = redirectKasaType });
    }

    // ─── Faz 2: Devredilmiş İstisnadan Yeni Kayıt ───

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFromDevredilmis(
        Guid parentId, string? redirectKasaType, CancellationToken ct)
    {
        var userName = User.Identity?.Name ?? "anonymous";
        var bugun = DateOnly.FromDateTime(DateTime.Today);

        try
        {
            await _finansalIstisna.CreateFromDevredilmisAsync(parentId, bugun, userName, ct);
            TempData["SuccessMessage"] = "✅ Devredilen istisnadan bugün için yeni kayıt oluşturuldu (İnceleme Bekliyor).";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "CreateFromDevredilmis başarısız: {ParentId}", parentId);
            TempData["ErrorMessage"] = $"❌ Hata: {ex.Message}";
        }

        return RedirectToAction("Index", new { kasaType = redirectKasaType });
    }
}

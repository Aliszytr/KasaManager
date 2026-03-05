using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KasaManager.Web.Controllers;

[Authorize]
public sealed class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public IActionResult Error()
    {
        return Content("Hata oluştu.");
    }
}

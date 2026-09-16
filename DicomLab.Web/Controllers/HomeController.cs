using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using DicomLab.Web.Models;

namespace DicomLab.Web.Controllers;

public class HomeController : Controller
{
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

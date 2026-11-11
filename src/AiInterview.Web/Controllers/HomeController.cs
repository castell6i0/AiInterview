using AiInterview.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace AiInterview.Web.Controllers;
public sealed class HomeController : Controller
{
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", User.IsInRole(Roles.Admin) ? "Admin" : User.IsInRole(Roles.Lecturer) ? "Lecturer" : "Dashboard");
        return View();
    }
    public IActionResult Privacy() => View();
    [AllowAnonymous, HttpGet] public IActionResult Help() => View();
    [Authorize] public IActionResult Setup() => RedirectToAction("Index", "AiSettings");
    [AllowAnonymous, IgnoreAntiforgeryToken] public IActionResult Error() { Response.StatusCode = 500; return View(); }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;

namespace AiInterview.Web.Services;

// A fetch should not follow a redirect and consume TempData before browser navigation.
public sealed class AjaxRedirectFilter(IUrlHelperFactory urls) : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.HttpContext.Request.Headers["X-Requested-With"] != "XMLHttpRequest"
            || !HttpMethods.IsPost(context.HttpContext.Request.Method)) return;
        var action = context.ActionDescriptor as ControllerActionDescriptor;
        if (action?.ControllerName != "AiSettings" && !(action?.ControllerName == "Profile" && action.ActionName == "Upload")) return;
        if (context.Result is RedirectToActionResult redirect)
        {
            var helper = urls.GetUrlHelper(context);
            var target = helper.Action(new UrlActionContext { Action = redirect.ActionName, Controller = redirect.ControllerName, Values = redirect.RouteValues });
            if (target != null && helper.IsLocalUrl(target)) context.Result = new JsonResult(new { redirectUrl = target });
        }
    }
    public void OnResultExecuted(ResultExecutedContext context) { }
}

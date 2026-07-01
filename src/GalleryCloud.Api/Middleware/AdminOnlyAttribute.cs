using GalleryCloud.Api.Dtos;
using GalleryCloud.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GalleryCloud.Api.Middleware;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AdminOnlyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var userContext = context.HttpContext.RequestServices.GetRequiredService<UserContext>();

        if (!userContext.IsAuthenticated)
        {
            context.Result = new UnauthorizedObjectResult(new ErrorResult("Authentication required"));
            return;
        }

        // Admin is identified by hardcoded userId "admin"
        if (!userContext.IsAdmin)
        {
            context.Result = new ObjectResult(new ErrorResult("Forbidden")) { StatusCode = 403 };
        }
    }
}

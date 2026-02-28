using Microsoft.AspNetCore.Mvc.Filters;

namespace InertiaCore.Utils;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class EncryptHistoryAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        Inertia.EncryptHistory();
        base.OnActionExecuting(context);
    }
}

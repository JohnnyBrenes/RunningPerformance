using System.Security.Claims;

namespace RunningPerformance.Api.Authentication;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetRequiredOwnerId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var subject = principal.FindFirstValue("sub");
        if (!Guid.TryParse(subject, out var ownerId) || ownerId == Guid.Empty)
        {
            throw new InvalidOperationException("The verified token has no valid subject.");
        }

        return ownerId;
    }
}

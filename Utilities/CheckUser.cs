using BoilerPlateApi.Contexts;
using BoilerPlateApi.Models.Users;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BoilerPlateApi.Utilities
{
    public static class CheckUser
    {
        public static UserApp? GetUserFromClaim(ClaimsPrincipal principal, MainContext context)
        {
            var email = principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(email))
                return null;

            return context.Users
                .Include(u => u.Status)
                .FirstOrDefault(u => u.Email == email);
        }
    }
}

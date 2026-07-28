using Microsoft.AspNetCore.Identity;

namespace BoilerPlateApi.Models.Users
{
    public class RoleApp : IdentityRole<Guid>
    {
        public RoleApp() { }

        public RoleApp(string name) : base(name) { }
    }
}

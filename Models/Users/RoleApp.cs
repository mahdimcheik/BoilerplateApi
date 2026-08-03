using Microsoft.AspNetCore.Identity;

namespace InteractivesApi.Models.Users
{
    public class RoleApp : IdentityRole<Guid>
    {
        public RoleApp() { }

        public RoleApp(string name) : base(name) { }
    }
}

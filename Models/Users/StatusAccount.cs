using InteractivesApi.Models.Interfaces;

namespace InteractivesApi.Models.Users
{
    public class StatusAccount : BaseModelOption
    {
        public ICollection<UserApp> Users { get; set; } = new List<UserApp>();
    }
}

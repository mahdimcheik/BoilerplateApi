using BoilerPlateApi.Models.Interfaces;

namespace BoilerPlateApi.Models.Users
{
    public class StatusAccount : BaseModelOption
    {
        public ICollection<UserApp> Users { get; set; } = new List<UserApp>();
    }
}

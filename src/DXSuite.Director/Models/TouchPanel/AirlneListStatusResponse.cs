using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace DXSuite.Director.Models.TouchPanel
{
    public class AirlineStatusListResponse
    {
        public List<AirlineStatus> Airlines { get; set; } = new List<AirlineStatus>();
    }
}

using DXSuite.Director.Models.TouchPanel;
using System.Net;

namespace DXSuite.Director.Models.Atrius
{
    public class APICallResponse
    {
        public string Data { get; set; } = "";
        public dynamic? DataObject { get; set; }
        public HttpStatusCode StatusCode { get; set; }
    }
}

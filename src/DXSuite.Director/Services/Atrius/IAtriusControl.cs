using DXSuite.Director.Models.Atrius;

namespace DXSuite.Director.Services.Atrius
{
    public interface IAtriusControl
    {
        Task<APICallResponse> GetLastSync();
        Task<APICallResponse> ResyncData();

    }
}

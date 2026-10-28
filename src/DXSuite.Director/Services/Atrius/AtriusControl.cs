using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using DXSuite.Director.Models.Atrius;

namespace DXSuite.Director.Services.Atrius
{
    public class AtriusControl: IAtriusControl
    {
        public string? endpointAddress { get; set; } = "";
        public string? bearerToken { get; set; } = "";
        public IHttpClientFactory httpFactory { get; set; }

        public AtriusControl(IHttpClientFactory clientFactory, IConfiguration config)
        {
            httpFactory = clientFactory;
            endpointAddress = config["Atrius:Endpoint"];
            bearerToken = config["Atrius:BearerToken"];
        }

        public async Task<APICallResponse> GetLastSync()
        {
            string apiAddressCall = string.Format(@"{0}/GetAtriusLastSync", endpointAddress);

            using (HttpClient httpClient = httpFactory.CreateClient())
            {
                APICallResponse response = new APICallResponse();
                try
                {
                    if (bearerToken != null && bearerToken != "")
                    {
                        httpClient.DefaultRequestHeaders.Add("Authentication", "Bearer " + bearerToken);
                    }

                    httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                    var result = await httpClient.GetAsync(apiAddressCall);
                    response.Data = await result.Content.ReadAsStringAsync();
                    response.StatusCode = result.StatusCode;

                    if (!string.IsNullOrEmpty(response.Data))
                    {
                        response.DataObject = JsonConvert.DeserializeObject<ResyncResponse>(response.Data);
                    }
                }
                catch (Exception ex)
                {
                    response.Data = ex.Message;
                    response.StatusCode = HttpStatusCode.BadRequest;
                }
                return response;
            }
        }

        public async Task<APICallResponse> ResyncData()
        {
            string apiAddressCall = string.Format(@"{0}/ResyncAtriusData", endpointAddress);

            using (HttpClient httpClient = httpFactory.CreateClient())
            {
                APICallResponse response = new APICallResponse();
                try
                {
                    if (bearerToken != null && bearerToken != "")
                    {
                        httpClient.DefaultRequestHeaders.Add("Authentication", "Bearer " + bearerToken);
                    }

                    httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                    var result = await httpClient.PostAsync(apiAddressCall, null);
                    response.Data = await result.Content.ReadAsStringAsync();
                    response.StatusCode = result.StatusCode;

                    if (!string.IsNullOrEmpty(response.Data))
                    {
                        response.DataObject = JsonConvert.DeserializeObject<ResyncResponse>(response.Data);
                    }
                }
                catch (Exception ex)
                {
                    response.Data = ex.Message;
                    response.StatusCode = HttpStatusCode.BadRequest;
                }
                return response;
            }
        }
    }
}

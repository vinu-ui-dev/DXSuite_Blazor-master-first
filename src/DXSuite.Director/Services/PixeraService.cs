using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Newtonsoft.Json.Linq;

namespace DXSuite.Director.Services
{
    public class PixeraService
    {
        private readonly Func<string, PixeraApiClient> _apiClientFactory;
        private readonly string _baseRoot;
        private readonly string _baseUNC;
        private readonly ILogger<PixeraService> _logger;

        public PixeraService(Func<string, PixeraApiClient> apiClientFactory, IConfiguration config, ILogger<PixeraService> logger, IWebHostEnvironment env)
        {
            _apiClientFactory = apiClientFactory;
            _baseRoot = env.WebRootPath;
            _baseUNC = config["FilePaths:BaseUNC"] ?? throw new Exception("Missing BaseUNC config");
            _logger = logger;
        }

        public async Task<bool> UploadContentToPixeraAsync(string directorRelativePath, string endpointIp)
        {
            try
            {
                var _apiClient = _apiClientFactory(endpointIp);

                //string normalizedRelativePath = directorRelativePath.TrimStart('/', '\\').Replace("/", "\\");
                string localFilePath = Path.Combine(_baseUNC, directorRelativePath);
                string resourcePath = Path.Combine("Media", directorRelativePath).Replace("\\", "/");
                string folderPath = Path.GetDirectoryName(resourcePath)?.Replace("\\", "/") ?? "";

                var rootFolderHandle = await _apiClient.GetResourceFolderWithNamePathAsync("Media");
                if (rootFolderHandle == null)
                    throw new Exception("Unable to locate root Media folder in Pixera.");

                await _apiClient.CreateFoldersFromAsync(rootFolderHandle, folderPath.Substring("Media/".Length));

                var targetFolderHandle = await _apiClient.GetResourceFolderWithNamePathAsync(folderPath);
                if (targetFolderHandle == null)
                    throw new Exception($"Unable to locate or create folder path {folderPath} in Pixera.");

                await _apiClient.AddResourceAsync(targetFolderHandle, localFilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pixera upload failed");
                return false;
            }
        }

        public async Task<bool> SoftDeleteResourceAsync(string pixeraResourcePath, string endpointIp)
        {
            try
            {
                var _apiClient = _apiClientFactory(endpointIp);

                var resourceHandle = await _apiClient.GetResourceByPathAsync(pixeraResourcePath);
                if (resourceHandle == null)
                    return false;

                await _apiClient.RemoveResourceFromUIAsync(resourceHandle);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pixera soft delete failed");
                return false;
            }
        }

        public async Task<bool> HardDeleteResourceAsync(string pixeraResourcePath, string endpointIp)
        {
            try
            {
                var _apiClient = _apiClientFactory(endpointIp);

                var resourceHandle = await _apiClient.GetResourceByPathAsync(pixeraResourcePath);
                if (resourceHandle == null)
                    return false;

                await _apiClient.DeleteResourceIncludingAssetsAsync(resourceHandle);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pixera hard delete failed");
                return false;
            }
        }
    }
}
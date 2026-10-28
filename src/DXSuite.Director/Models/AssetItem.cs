using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Models
{
    public class AssetItem
    {
        public int AssetId { get; set; }
        public string AssetPath { get; set; }
        public string AssetWebPath { get; set; }
        public string? AssetInkPath { get; set; }
        public int AssetTypeId { get; set; }
        public double DefaultDuration { get; set; }
        public string? VideoMaskPath { get; set; }
        public string? EndpointPath { get; set; }

        public AssetItem()
        {

        }

        public AssetItem(TblAsset asset, string siteUrl)
        {
            AssetId = asset.AssetId;
            AssetPath = asset.AssetPath;
            AssetWebPath = siteUrl + asset.AssetPath;
            AssetInkPath = asset.AssetInkPath;
            AssetTypeId = asset.AssetTypeId;
            DefaultDuration = asset.Duration;
            VideoMaskPath = asset.VideoMaskPath;
            EndpointPath = asset.EndpointPath;
        }
    }
}

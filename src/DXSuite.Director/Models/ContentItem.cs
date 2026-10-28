using System.Text.Json;
using Newtonsoft.Json;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Models
{

    public class ContentAssignment
    {
        public List<ContentItem> Content { get; set; } = new List<ContentItem>();

        // Clock properties
        public bool UseClock { get; set; } = false;
        public double ClockX { get; set; } = 0;
        public double ClockY { get; set; } = 0;
        public double ClockWidth { get; set; } = 0;
        public double ClockHeight { get; set; } = 0;

        public ContentAssignment()
        {

        }

        public ContentAssignment(List<ContentItem> content)
        {
            Content = content;
        }

        public List<int> GetAssetIDList()
        {
            List<int> assetIDList = new List<int>();
            foreach (ContentItem contentItem in Content)
            {
                assetIDList.Add(contentItem.AssetId);
            }
            return assetIDList;
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public class ContentItem
    {
        public int AssetId { get; set; }
        public double Duration { get; set; }

        // Plays per loop; rotations persisted before weights existed deserialize to 1
        public int Weight { get; set; } = 1;

        // Two serializers touch this type: Newtonsoft persists ContentAssignment to the DB, while
        // System.Text.Json shapes the SignageController HTTP response. Each honors only its own
        // [JsonIgnore]. Transient/derived members below carry both attributes so they are excluded
        // from persistence and from the API response; AssetItem carries only the Newtonsoft attribute
        // because it is a read-time projection that must still be returned to API consumers.

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool IsCustomizingDuration { get; set; } = false;

        // Resolved at read time; excluded from persistence to avoid serializing the full EF entity graph.
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public TblAsset? Asset { get; set; } //Populate when resolving Content

        [Newtonsoft.Json.JsonIgnore]
        public AssetItem? AssetItem { get; set; }


        public ContentItem() { }


        public ContentItem(int assetId, double duration)
        {
            AssetId = assetId;
            Duration = duration;
        }

        public ContentItem(TblAsset asset)
        {
            AssetId = asset.AssetId;
            Duration = asset.Duration;
            Asset = asset;
        }

        public ContentItem(TblAsset asset, double duration)
        {
            AssetId = asset.AssetId;
            Duration = duration;
        }
    }
}

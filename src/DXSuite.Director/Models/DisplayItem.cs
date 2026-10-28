using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;

namespace DXSuite.Director.Models
{

    public class LocationDisplayResponse
    {
        public int LocationId { get; set; }
        public string? LocationName { get; set; }
        public int LocationTypeId { get; set; }
        public List<DisplayItem> Displays { get; set; } = new List<DisplayItem>();
    }

    public class DisplayItem
    {
        public int DisplayId { get; set; }
        public string DisplayName { get; set; }
        public int DisplayMode { get; set; }
        public ContentAssignment? Content { get; set; }
        public string? EndpointType { get; set; }
        public string? EndpointIp { get; set; }
        public string? DisplayEndpointPath { get; set; }


        public DisplayItem()
        {

        }

        public DisplayItem(TblDisplay display)
        {
            DisplayId = display.DisplayId;
            DisplayName = display.DisplayName;
            DisplayMode = display.DisplayMode;

            if (display.Endpoint != null)
            {
                EndpointType = display.Endpoint.Type;
                EndpointIp = display.Endpoint.Ipaddress;
            }

            DisplayEndpointPath = display.EndpointPath;
        }

        /// <summary>
        /// Picks the event active at <paramref name="localNow"/>, the local wall-clock moment in
        /// whatever time zone the caller chose. A player request resolves it from the requesting
        /// device's zone; a preview passes the host's clock knowingly. There is no default, so no
        /// caller can read the host's clock without saying so.
        /// </summary>
        public TblScheduleEvent? GetActiveEvent(ICollection<TblScheduleEvent> scheduleEvents, DateTime localNow)
            // The fall-back to default content is also what the display path serves when a device's
            // time zone will not resolve: the caller declines to filter and falls toward the
            // display's own designed default rather than toward a blank screen.
            => ScheduleEventSelection.PickActive(scheduleEvents, localNow, e => e.DefaultContent);

        public async Task<ContentAssignment> GetContentAssignmentAsync(TblDisplay display, IDirectorDBContext dbContext, string siteUrl, DateTime localNow)
            => await GetContentAssignmentForEventAsync(
                display, GetActiveEvent(display.TblScheduleEvents, localNow), dbContext, siteUrl);

        /// <summary>
        /// Hydrates the content of an event the caller has already chosen. Split out because the
        /// layout path chooses its own event, against the requesting device's clock, and a caller
        /// whose device has no resolvable zone chooses the default event rather than a moment.
        /// </summary>
        public async Task<ContentAssignment> GetContentAssignmentForEventAsync(
            TblDisplay display, TblScheduleEvent? schedule, IDirectorDBContext dbContext, string siteUrl)
        {
            ContentAssignment contentAssignment = new ContentAssignment();
            if (display.DisplayMode == (int)Types.DisplayMode.Schedule ||
                display.DisplayMode == (int)Types.DisplayMode.StatusSelector)
            {
                // When a timed airline allocation event is active but carries no explicit content,
                // the DefaultContent event holds the StatusSelector selection to display.
                // DefaultAssetActivatorService writes here on block activation; operators can also
                // update it manually via the StatusSelector UI.
                if (schedule != null && !schedule.DefaultContent)
                {
                    var parsed = JsonConvert.DeserializeObject<ContentAssignment>(schedule.ContentAssignment);
                    if (parsed?.Content?.Any() != true)
                        schedule = display.TblScheduleEvents.FirstOrDefault(x => x.DefaultContent);
                }

                if (schedule != null)
                {
                    ContentAssignment? contentAssignmentObject = JsonConvert.DeserializeObject<ContentAssignment>(schedule.ContentAssignment);
                    if (contentAssignmentObject?.Content?.Any() == true)
                    {
                        // Normal path: content was explicitly set on the schedule event.
                        contentAssignment = contentAssignmentObject;
                        IList<TblAsset> assetList = await dbContext.TblAssets.Where(x => contentAssignment.GetAssetIDList().Contains(x.AssetId)).ToListAsync();

                        foreach (ContentItem item in contentAssignment.Content)
                        {
                            TblAsset? asset = assetList.FirstOrDefault(x => x.AssetId == item.AssetId);
                            if (asset != null)
                            {
                                item.Asset = asset;
                                item.AssetItem = new AssetItem(asset, siteUrl);
                            }
                        }
                    }
                }
            }
            else if (display.DisplayMode == (int)Types.DisplayMode.Override)
            {
                TblAsset? asset = await dbContext.TblAssets.FirstOrDefaultAsync(x => x.AssetId == display.OverrideAssetId);
                if (asset != null)
                {
                    contentAssignment.Content.Add(new ContentItem() { AssetId = asset.AssetId, Duration = asset.Duration, Asset = asset, AssetItem = new AssetItem(asset,siteUrl)});
                }
            }
            return contentAssignment;
        }

    }
}

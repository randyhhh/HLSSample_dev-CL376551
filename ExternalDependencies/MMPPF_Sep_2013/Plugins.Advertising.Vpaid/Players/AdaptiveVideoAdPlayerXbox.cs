using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.SilverlightMediaFramework.Utilities.Extensions;
using Microsoft.Web.Media.SmoothStreaming;

namespace Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID
{
    /// <summary>
    /// A VPAID ad player that can play adaptive video ads
    /// </summary>
    public class AdaptiveVideoAdPlayerXbox : AdaptiveVideoAdPlayer
    {
        internal AdaptiveVideoAdPlayerXbox(ICreativeSource AdSource, IAdTarget AdTarget, IDvrPlayer AdHost)
            : base(AdSource, AdTarget, AdHost.ActiveMediaPlugin)
        {
            this.AdHost = AdHost;
            ((Microsoft.SilverlightMediaFramework.Core.SMFPlayer)AdHost).PreBack += new EventHandler(OnAdUserClose);
        }

        private IDvrPlayer AdHost;
        private AdaptiveMediaElementAdapter AdMediaElementAdapter = null;

        public override void StartAd()
        {
            base.StartAd();
            if (AdMediaElementAdapter != null)
            {
                AdMediaElementAdapter.Pause += new EventHandler(AdMediaElementAdapter_Pause);
                AdMediaElementAdapter.Play += new EventHandler(AdMediaElementAdapter_Play);
                AdMediaElementAdapter.StartAd(Duration.GetValueOrDefault(TimeSpan.Zero));
            }
        }

        protected override void LoadLinear()
        {
            base.LoadLinear();

            AdMediaElementAdapter = new AdaptiveMediaElementAdapter(MediaElement, AdHost.MediaTransport);
            AdMediaElementAdapter.LoadAd();
        }

        protected override void UnloadLinear()
        {
            if (AdMediaElementAdapter != null)
            {
                AdMediaElementAdapter.Pause -= new EventHandler(AdMediaElementAdapter_Pause);
                AdMediaElementAdapter.Play -= new EventHandler(AdMediaElementAdapter_Play);
                AdMediaElementAdapter.UnloadAd();
            }
            base.UnloadLinear();
        }

        void AdMediaElementAdapter_Play(object sender, EventArgs e)
        {
            if (IsPaused)
                ResumeAd();
            else
                MediaElement.Play();
        }

        void AdMediaElementAdapter_Pause(object sender, EventArgs e)
        {
            PauseAd();
        }

        protected override void OnManifestReady()
        {
            // restrict the tracks to ones 720p or less
            var segment = MediaElement.ManifestInfo.Segments[MediaElement.CurrentSegmentIndex.Value];
            foreach (var videoStream in segment.SelectedStreams.Where(i => i.Type == MediaStreamType.Video))
            {
                var availableTracks = videoStream.AvailableTracks;
                var eligableTracks = videoStream.AvailableTracks.Where(o => GetResolution(o).Height <= 720 && GetResolution(o).Width <= 1280).ToList();
                if (eligableTracks.Count != availableTracks.Count && eligableTracks.Any())
                {
                    videoStream.RestrictTracks(eligableTracks);
                }
            }

            base.OnManifestReady();
        }

        private const string HeightAttribute = "height";
        private const string WidthAttribute = "width";
        private const string MaxHeightAttribute = "maxheight";
        private const string MaxWidthAttribute = "maxwidth";
        private static Size GetResolution(TrackInfo _trackInfo)
        {
            string heightStr = _trackInfo.Attributes.GetEntryIgnoreCase(MaxHeightAttribute) ?? _trackInfo.Attributes.GetEntryIgnoreCase(HeightAttribute);
            string widthStr = _trackInfo.Attributes.GetEntryIgnoreCase(MaxWidthAttribute) ?? _trackInfo.Attributes.GetEntryIgnoreCase(WidthAttribute);
            double height, width;
            return double.TryParse(heightStr, out height)
                   && double.TryParse(widthStr, out width)
                       ? new Size(width, height)
                       : Size.Empty;
        }

        public override void Dispose()
        {
            ((Microsoft.SilverlightMediaFramework.Core.SMFPlayer)AdHost).PreBack -= new EventHandler(OnAdUserClose);
            base.Dispose();
        }
    }
}

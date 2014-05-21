using System;
using System.Windows;
using Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID;

namespace Microsoft.SilverlightMediaFramework.Plugins.UAC
{
    /// <summary>
    /// Represents a UAC linear creative to be used by a VPAID plugin
    /// </summary>
    public class AdCreativeSource : ICreativeSource
    {
        Microsoft.Advertising.VideoResource m_videoResource;
        Microsoft.Advertising.ActivityReporter m_reporter;

        internal AdCreativeSource(Microsoft.Advertising.AdPackage package, Microsoft.Advertising.VideoResource videoResource)
        {
            m_videoResource = videoResource;
            if (package != null)
            {
                m_reporter = package.ActivityReporter;
            }
        }

        public string MediaSource
        {
            get { return m_videoResource.Location.ToString(); }
        }

        public string Id
        {
            get { return null; }
        }

        public void Track(TrackingEventEnum EventToTrack)
        {
            try
            {
                m_reporter.Report(EventToTrack.ToString());
            }
            catch { /* ignore */ }
        }

        public void Track(string Activity)
        {
            try
            {
                m_reporter.Report(Activity);
            }
            catch { /* ignore */ }
        }

        public TimeSpan? Duration
        {
            get { return null; }
        }

        public string MimeType
        {
            get { return m_videoResource.MimeType; }
        }

        public string ClickUrl
        {
            get { return null; }
        }

        public Size Dimensions
        {
            get { return new Size(m_videoResource.Width, m_videoResource.Height); }
        }

        public Size ExpandedDimensions
        {
            get { return Size.Empty; }
        }

        // Currently only supports linear ads
        public CreativeSourceType Type
        {
            get { return CreativeSourceType.Linear; }
        }

        public MediaSourceEnum MediaSourceType
        {
            get { return MediaSourceEnum.Static; }
        }

        public string ExtraInfo
        {
            get { return String.Empty; }
        }

        public bool IsScalable
        {
            get { return true; }
        }

        public bool MaintainAspectRatio
        {
            get { return true; }
        }

        public string AltText
        {
            get { return string.Empty; }
        }

        public bool IsStreaming
        {
            get { return m_videoResource.Delivery == Microsoft.Advertising.DeliveryType.Streaming; }
        }
    }

}

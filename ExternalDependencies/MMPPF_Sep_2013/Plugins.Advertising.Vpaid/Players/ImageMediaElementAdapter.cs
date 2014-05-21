using System;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Xbox.Controls;

namespace Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID
{
    public class ImageMediaElementAdapter : MediaElementAdapterBase
    {
        QuartileAdPlayerBase MediaPlugin;
        Image Image;

        public ImageMediaElementAdapter(QuartileAdPlayerBase MediaPlugin, MediaTransport MediaTransport, Image Image)
            : base(MediaTransport)
        {
            MediaPlugin.AdPaused += new EventHandler(MediaPlugin_AdPaused);
            MediaPlugin.AdResumed += new EventHandler(MediaPlugin_AdResumed);
            this.MediaPlugin = MediaPlugin;
            this.Image = Image;
        }

        protected override double BufferingProgressCore
        {
            get { return 0; }
        }

        protected override double DownloadOffsetCore
        {
            get { return 0; }
        }

        protected override double DownloadProgressCore
        {
            get { return 0; }
        }

        protected override bool CanPauseCore
        {
            get { return true; }
        }

        protected override bool CanSeekCore
        {
            get { return true; }
        }

        protected override MediaElementAdapterState CurrentStateCore
        {
            get
            {
                if (MediaPlugin.IsPaused)
                    return MediaElementAdapterState.Paused;
                else
                    return MediaElementAdapterState.Playing;
            }
        }

        protected override void DisposeCore()
        {
            MediaPlugin.AdPaused -= new EventHandler(MediaPlugin_AdPaused);
            MediaPlugin.AdResumed -= new EventHandler(MediaPlugin_AdResumed);
            MediaPlugin = null;
        }

        protected override TimeSpan PositionCore
        {
            get { return MediaPlugin.Position; }
        }

        protected override void SeekCore(TimeSpan position)
        {
            MediaPlugin.SeekToPosition(position);
        }

        protected override void SetStretchCore(Stretch stretch)
        {
            Image.Stretch = stretch;
        }

        protected override void StopCore()
        {
            MediaPlugin.Stop();
        }

        protected override void PlayCore()
        {
            MediaPlugin.Resume();
        }

        protected override void PauseCore()
        {
            MediaPlugin.Pause();
        }

        void MediaPlugin_AdResumed(object sender, EventArgs e)
        {
            OnCurrentStateChanged();
        }

        void MediaPlugin_AdPaused(object sender, EventArgs e)
        {
            OnCurrentStateChanged();
        }
    }
}

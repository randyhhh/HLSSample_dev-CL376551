using System;
using Microsoft.SilverlightMediaFramework.Utilities.Extensions;

namespace Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID
{
    /// <summary>
    /// A VPAID ad player that can show static image ads
    /// </summary>
    public class ImageAdPlayerXbox : ImageAdPlayer
    {
        internal ImageAdPlayerXbox(ICreativeSource Source, IAdTarget Target, IDvrPlayer Host)
            : base(Source, Target, Host.ActiveMediaPlugin)
        {
            AdHost = Host;
        }

        private IDvrPlayer AdHost;
        private ImageMediaElementAdapter AdMediaElementAdapter;
        
        public override void StartAd()
        {
            base.StartAd();
            if (AdMediaElementAdapter != null)
            {
                AdMediaElementAdapter.Play += AdMediaElementAdapter_Play;
                AdMediaElementAdapter.Pause += AdMediaElementAdapter_Pause;
                AdMediaElementAdapter.StartAd(Duration.GetValueOrDefault(TimeSpan.Zero));
            }
        }

        protected override void LoadLinear()
        {
            base.LoadLinear();

            AdMediaElementAdapter = new ImageMediaElementAdapter(this, AdHost.MediaTransport, Image);
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
        }

        void AdMediaElementAdapter_Pause(object sender, EventArgs e)
        {
            PauseAd();
        }
    }
}

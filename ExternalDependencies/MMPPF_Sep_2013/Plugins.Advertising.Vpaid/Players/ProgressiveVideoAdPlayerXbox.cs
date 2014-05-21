using System;
using System.Windows.Controls;
using Microsoft.SilverlightMediaFramework.Utilities.Extensions;

namespace Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID
{
    /// <summary>
    /// A VPAID ad player that can play progressive video ads and control Dvr on Xbox
    /// </summary>
    public class ProgressiveVideoAdPlayerXbox : ProgressiveVideoAdPlayer
    {
        internal ProgressiveVideoAdPlayerXbox(ICreativeSource AdSource, IAdTarget AdTarget, IDvrPlayer AdHost)
            : base(AdSource, AdTarget, AdHost.ActiveMediaPlugin)
        {
            this.AdHost = AdHost;
            ((Microsoft.SilverlightMediaFramework.Core.SMFPlayer)AdHost).PreBack += new EventHandler(OnAdUserClose);
        }

        private IDvrPlayer AdHost;
        private ProgressiveMediaElementAdapter AdMediaElementAdapter = null;

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

            AdMediaElementAdapter = new ProgressiveMediaElementAdapter(MediaElement, AdHost.MediaTransport);
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

        public override void Dispose()
        {
            ((Microsoft.SilverlightMediaFramework.Core.SMFPlayer)AdHost).PreBack -= new EventHandler(OnAdUserClose);
            base.Dispose();
        }
    }
}

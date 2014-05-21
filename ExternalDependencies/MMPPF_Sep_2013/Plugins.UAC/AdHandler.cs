using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.Advertising;
using Microsoft.SilverlightMediaFramework.Core;
using Microsoft.SilverlightMediaFramework.Plugins.Advertising.Resources;
using Microsoft.SilverlightMediaFramework.Plugins.Advertising.VAST;
using Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID;
using Microsoft.SilverlightMediaFramework.Plugins.Metadata;
using Microsoft.SilverlightMediaFramework.Plugins.Primitives.Advertising;
using Microsoft.SilverlightMediaFramework.Utilities.Extensions;

namespace Microsoft.SilverlightMediaFramework.Plugins.UAC
{
    /// <summary>
    /// Plugin that handles UAC ads
    /// </summary>
    [ExportAdPayloadHandlerPlugin(PluginName = PluginName, PluginDescription = PluginDescription, PluginVersion = PluginVersion, SupportedFormat = PayloadFormat)]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public partial class AdHandler : AdHandlerBase
    {
        #region IPlugin Members
        private const string PluginName = "UACAdHandler";
        private const string PluginDescription = "A handler for ads in Microsoft UAC format";
        private const string PluginVersion = "2.2012.1005.0";
        #endregion

        public const string PayloadFormat = "UAC";

        private IAdPlaybackToken m_adPlaybackToken;

        #region Config

        private const bool DefaultIsSmoothEnabled = true;
        private bool? isSmoothEnabled;
        public const string Key_IsSmoothEnabled = "Microsoft.Advertising.IsSmoothEnabled";

        /// <summary>
        /// Indicates whether or not smooth streaming ads are enabled.
        /// </summary>
        public bool IsSmoothEnabled
        {
            get
            {
                if (!isSmoothEnabled.HasValue)
                {
                    isSmoothEnabled = GetIsSmoothEnabled();
                }
                return isSmoothEnabled.Value;
            }
            set
            {
                isSmoothEnabled = value;
            }
        }

        private bool GetIsSmoothEnabled()
        {
            if (AdHost is IPlayer)
            {
                var player = (IPlayer)AdHost;
                if (player.GlobalConfigMetadata != null && player.GlobalConfigMetadata.ContainsKey(Key_IsSmoothEnabled))
                {
                    var isSmoothEnabledObject = player.GlobalConfigMetadata[Key_IsSmoothEnabled];
                    if (isSmoothEnabledObject is bool)
                    {
                        return (bool)isSmoothEnabled;
                    }
                    else if (isSmoothEnabledObject is string)
                    {
                        bool isSmoothEnabledResult;
                        if (bool.TryParse(player.GlobalConfigMetadata[Key_IsSmoothEnabled] as string, out isSmoothEnabledResult))
                        {
                            return isSmoothEnabledResult;
                        }
                    }
                }
            }
            return DefaultIsSmoothEnabled;
        }


#if LIMITBITRATE
        private const int DefaultMaxBitrateKbps = 1500;
#else
        private const int DefaultMaxBitrateKbps = int.MaxValue;
#endif

        public const string Key_MaxBitrateKbps = "Microsoft.Advertising.MaxBitrateKbps";
        private int? maxBitrateKbps;
        /// <summary>
        /// Indicates whtat the max bitrate for an ad can be. If there are no ads with bitrates below this theshold, the setting will be ignored.
        /// </summary>
        public int MaxBitrateKbps
        {
            get
            {
                if (!maxBitrateKbps.HasValue)
                {
                    maxBitrateKbps = GetMaxBitrateKbps();
                }
                return maxBitrateKbps.Value;
            }
            set
            {
                maxBitrateKbps = value;
            }
        }

        private int GetMaxBitrateKbps()
        {
            if (AdHost is IPlayer)
            {
                var player = (IPlayer)AdHost;
                if (player.GlobalConfigMetadata != null && player.GlobalConfigMetadata.ContainsKey(Key_MaxBitrateKbps))
                {
                    var maxBitrateKbpsObject = player.GlobalConfigMetadata[Key_MaxBitrateKbps];
                    if (maxBitrateKbpsObject is int)
                    {
                        return (int)maxBitrateKbpsObject;
                    }
                    else if (maxBitrateKbpsObject is string)
                    {
                        int maxBitrateKbpsResult;
                        if (int.TryParse(maxBitrateKbpsObject as string, out maxBitrateKbpsResult))
                        {
                            return maxBitrateKbpsResult;
                        }
                    }
                }
            }
            return DefaultMaxBitrateKbps;
        }

        #endregion

        private UACCreativeFactory adModelFactory;
        private UACAdPod activeAdPod;
        private readonly List<UACCreativeSet> activeCreativeSets = new List<UACCreativeSet>();

        public AdHandler()
            : base()
        {
            PluginLogName = PluginName;
        }

        /// <summary>
        /// Raised when an individual ad fails. The criteria are based on the FailureStrategy property.
        /// </summary>
        public event EventHandler<AdCompletedEventArgs> AdCompleted;

        protected override bool CanHandleAd(IAdSource source)
        {
            if (activeAdPod != null)
            {
                if (activeAdPod.CanShutdown)
                {
                    // shut down the current trigger and let the new one in.
                    activeAdPod.Cancel();
                }
                else
                {
                    // there is already a loading ad. ignore the new trigger.
                    return false;
                }
            }
            return true;
        }

        protected override IAsyncAdPayload CreatePayload(IAdSource source)
        {
            // create the payload result. It will only contain UAC based ads
            var result = new UACAdUnit(source);

            // create a model to hold the ad and tell it to load itself
            activeAdPod = new UACAdPod(result, adModelFactory);
            activeAdPod.FailurePolicy = FailurePolicy;
            activeAdPod.CloseCompanionsOnComplete = CloseCompanionsOnComplete;
            result.AdPod = activeAdPod; // remember the active adspot

            return result;
        }

        private readonly object loadBlocker = new object();
        protected override bool ExecutePayload(IAsyncAdPayload Payload, IAdSource Source)
        {
            var result = Payload as UACAdUnit;
            var adPod = result.AdPod;

            bool needsRelease;
            Player.ActiveMediaPlugin.VisualElement.IfNotNull(v => v.Visibility = System.Windows.Visibility.Collapsed);
            if (((Microsoft.SilverlightMediaFramework.Core.SMFPlayer)adHost).IsPlayBlocked)
            {
                // if we're already blocked, stick with it.
                AdHost.AddPlayBlock(loadBlocker);
                needsRelease = true;
            }
            else
            {
                Player.ActiveMediaPlugin.Pause();
                needsRelease = false;
            }

            adPod.LoadAsync(success =>
            {
                Player.ActiveMediaPlugin.VisualElement.IfNotNull(v => v.Visibility = System.Windows.Visibility.Visible);
                if (success)
                {
                    // now that it is loaded, watch for each Ad and CreativeSet to begin and complete running
                    foreach (var ad in adPod.Ads)
                    {
                        ad.RunCompleted += ad_RunCompleted;
                        foreach (var creativeSet in ad.CreativeSets)
                        {
                            creativeSet.RunStarting += creativeSet_RunStarting;
                            creativeSet.RunStarted += creativeSet_RunStarted;
                            creativeSet.RunCompleted += creativeSet_RunCompleted;
                        }
                    }
                    // pass on that we are now running this ad. Note: It still could fail to run.
                    result.OnStart();
                    // actually run the ad
                    adPod.RunCompleted += new Action<Microsoft.SilverlightMediaFramework.Plugins.Advertising.VAST.AdPod, bool>(adPod_RunCompleted);
                    adPod.ReleasePlayer += new Action<Microsoft.SilverlightMediaFramework.Plugins.Advertising.VAST.AdPod>(adPod_ReleasePlayer);
                    adPod.RunAsync();
                }
                else
                {
                    // clear out the current running AdSpot. This permits other ads to be handled.
                    activeCreativeSets.Clear();
                    activeAdPod = null;

                    // notify upstream
                    result.OnFail();
                    result.Deactivate();

                    base.ExecuteAdFailed(Source);

                    if (!needsRelease)
                    {
                        Player.ActiveMediaPlugin.Play();
                        Player.ActiveMediaPlugin.AutoPlay = true;
                    }
                }

                if (needsRelease)
                {
                    adHost.ReleasePlayBlock(loadBlocker);
                }

            });
            return true;
        }
        
        void adPod_ReleasePlayer(Microsoft.SilverlightMediaFramework.Plugins.Advertising.VAST.AdPod adPod)
        {
            adPod.ReleasePlayer -= adPod_ReleasePlayer;
            // release the play block (this will start the player again if a play operation was pending)
            ReleasePlayer();
        }

        void adPod_RunCompleted(Microsoft.SilverlightMediaFramework.Plugins.Advertising.VAST.AdPod adPod, bool success)
        {
            adPod.RunCompleted -= adPod_RunCompleted;

            var uacAdPod = adPod as UACAdPod;
            // unhook all the event handlers we added for the individual parts of the ad operation
            foreach (var ad in uacAdPod.Ads)
            {
                ad.RunCompleted -= ad_RunCompleted;
                foreach (var creativeSet in ad.CreativeSets)
                {
                    creativeSet.RunStarting -= creativeSet_RunStarting;
                    creativeSet.RunStarted -= creativeSet_RunStarted;
                    creativeSet.RunCompleted -= creativeSet_RunCompleted;
                }
            }

            // clear out the current running AdSpot. This permits other ads to be handled.
            activeAdPod = null;

            // notify upstream
            if (!success) uacAdPod.AdUnit.OnFail();
            uacAdPod.AdUnit.Deactivate();

            OnHandleCompleted(new HandleCompletedEventArgs(uacAdPod.AdUnit.Source, success));
        }

        #region vPaidController EventHandlers

        protected override void VpaidController_AdStarted(object sender, ActiveCreativeEventArgs e)
        {
            base.VpaidController_AdStarted(sender, e);
            UACCreative creative = e.UserState as UACCreative;
            var sourceAd = creative.ParentCreativeSet.ParentAd.Ad;
            SMFPlayer smf = AdHost as SMFPlayer;
            m_adPlaybackToken = AdManager.StartAdPlayback(smf.MediaTransport, sourceAd);
            if (m_adPlaybackToken != null)
            {
                m_adPlaybackToken.EventBack += AdPlaybackTokenBackHandler;
            }
        }

        protected override void vPaidController_AdCompleted(object sender, ActiveCreativeEventArgs e)
        {
            // do nothing, we control our own blocking
            EndAdPlayback();
        }

        protected override void AdFailed(ActiveCreativeEventArgs e)
        {
            EndAdPlayback();
            base.AdFailed(e);

            var creative = e.UserState as UACCreative;
            creative.Failed();
        }

        protected override void AdStopped(ActiveCreativeEventArgs e)
        {
            EndAdPlayback();
            base.AdStopped(e);

            var creative = e.UserState as UACCreative;
            creative.Succeeded();
        }

        void VpaidController_AdStopFailed(object sender, ActiveCreativeEventArgs e)
        {
            EndAdPlayback();
        }
        #endregion

        #region AdModel EventHandlers
        private void creativeSet_RunStarting(CreativeSet sender)
        {
            var creativeSet = sender as UACCreativeSet;
            activeCreativeSets.Add(creativeSet);    // remember the active creative set.
            //RefreshAdMode();

            foreach (var creative in creativeSet.Creatives)
            {
                creative.RunCompleted += creative_RunCompleted;
            }
        }

        private void creativeSet_RunStarted(CreativeSet sender)
        {
            var creativeSet = sender as UACCreativeSet;
            // unhook all creatives
            foreach (var creative in creativeSet.Creatives)
            {
                creative.RunCompleted -= creative_RunCompleted;
            }
            // re-hook the ones that are actually running
            foreach (var creative in creativeSet.RunningCreatives)
            {
                creative.RunCompleted += creative_RunCompleted;
            }
        }

        private void creative_RunCompleted(Creative creative, bool success)
        {
            var adCreative = (UACCreative)creative;

            if (success)
                SendLogEntry(LogEntryTypes.CreativeSucceeded, Microsoft.SilverlightMediaFramework.Plugins.Primitives.LogLevel.Information, string.Format(VastAdHandlerResources.CreativeSucceeded, adCreative.Id));
            else
                SendLogEntry(LogEntryTypes.CreativeFailed, Microsoft.SilverlightMediaFramework.Plugins.Primitives.LogLevel.Error, string.Format(VastAdHandlerResources.CreativeFailed, adCreative.Id));
        }

        private void creativeSet_RunCompleted(CreativeSet sender, bool success)
        {
            var creativeSet = sender as UACCreativeSet;
            // cleanup
            foreach (var creative in creativeSet.Creatives)
            {
                creative.RunCompleted -= creative_RunCompleted;
            }
            activeCreativeSets.Remove(creativeSet);
        }

        private void ad_RunCompleted(Microsoft.SilverlightMediaFramework.Plugins.Advertising.VAST.Ad ad, bool success)
        {
            if (AdCompleted != null) AdCompleted(this, new AdCompletedEventArgs(ad as UACAd, success));
        }
        #endregion

        #region AdHost EventHandlers
        private void adHost_StateChanged(object sender, EventArgs e)
        {
            // do nothing
        }

#if !WINDOWS_PHONE && !FULLSCREEN
        private void adHost_FullScreenChanged(object sender, EventArgs e)
        {
            if (adHost.IsFullScreen)
            {
                VpaidController.OnFullscreen();
            }
        }
#endif

        private void adHost_VolumeChanged(object sender, EventArgs e)
        {
            VpaidController.SetVolume(adHost.Volume);
        }
        #endregion

        #region ad download/population operations
        /// <summary>
        /// Populates an AdUnit with the UAC info, downloads the UAC ad if necessary
        /// </summary>
        /// <param name="ad">The UACAdUnit to be populated</param>
        /// <param name="Completed">Fired when the operation is complete, includes a status param</param>
        internal void LoadAdUnitAsync(UACAdUnit ad, Action<bool> Completed)
        {
            Completed(true);
        }
        #endregion

        #region IGenericPlugin

        public override void Load()
        {
            adModelFactory = new UACCreativeFactory(this);
            base.Load();
            base.VpaidController.AdStopFailed += VpaidController_AdStopFailed;
        }

        public override void Unload()
        {
            adModelFactory = null;
            base.VpaidController.AdStopFailed -= VpaidController_AdStopFailed;
            base.Unload();
        }

        #endregion

        internal void PlayCreative(UACCreative creative)
        {
            PlayCreative(creative.ActiveCreative, creative);
        }

        internal void EndAdPlayback()
        {
            if (m_adPlaybackToken != null)
            {
                m_adPlaybackToken.EventBack -= AdPlaybackTokenBackHandler;
                AdManager.EndAdPlayback(m_adPlaybackToken);
            }
        }
        
        void AdPlaybackTokenBackHandler(object sender, EventArgs e)
        {
            EndAdPlayback();
            (AdHost as SMFPlayer).MediaTransport.ExecuteMediaCommand(System.Windows.Media.MediaCommand.Stop);
        }

        internal void CancelCreative(UACCreative creative)
        {
            EndAdPlayback();
            base.CancelCreative(creative.ActiveCreative);
        }

        protected override void AdApproachingEnd(ActiveCreativeEventArgs e)
        {
            // if the current creative is using a player that supports preloading
            // and if the next creative can be preloaded in that player
            // preload it

            if (!(e.ActiveCreative.Player is IVpaidSupportsPreloading))
                return;

            var currentCreative = e.UserState as UACCreative;

            // Find linear creative, either in this ad, or in the next

            var currentCreativeSet = currentCreative.ParentCreativeSet;

            // First look for the next CreativeSet in the current Ad
            var nextCreativeSet = currentCreativeSet.ParentAd.CreativeSets.SkipWhile(c => c != currentCreativeSet).Skip(1).FirstOrDefault() as UACCreativeSet;

            // If there are no further CreativeSets in the current Ad, look in the next Ad
            if (nextCreativeSet == null)
            {
                var currentAd = currentCreative.ParentCreativeSet.ParentAd;
                var nextAd = currentAd.ParentAdPod.Ads.SkipWhile(a => a != currentAd).Skip(1).FirstOrDefault() as UACAd;

                if (nextAd == null)
                    return;

                nextCreativeSet = nextAd.CreativeSets.FirstOrDefault() as UACCreativeSet;

                if (nextCreativeSet == null)
                    return;
            }

            if (nextCreativeSet.ContainsLinear)
            {
                var linearCreative = nextCreativeSet.Creatives.OfType<LinearUACCreative>().First();

                if (linearCreative.PreLoad())
                {
                    VpaidController.PreloadAd(linearCreative.ActiveCreative, (int)(adHost.PlaybackBitrate / 1024));
                }
            }
        }
    }
}

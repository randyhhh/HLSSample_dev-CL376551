using System;
using System.Windows;
using Microsoft.SilverlightMediaFramework.Utilities.Extensions;
using Microsoft.SilverlightMediaFramework.Plugins.Primitives;

namespace Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID
{
    /// <summary>
    /// A VPAID ad player that can play cips using an existing IMediaPlugin object retrieved from IPlayer.ActiveMediaPlugin using the ScheduleAd method
    /// </summary>
    public class AdClipLinearAdPlayer : AdPlayerBase, IVpaidLinearBehavior, IVpaidSupportsPreloading
    {
        public AdClipLinearAdPlayer(ICreativeSource AdSource, IAdTarget AdTarget, IPlayer AdHost)
            : base(AdSource.Dimensions, AdSource.IsScalable, true)
        {
            this.AdSource = AdSource;
            this.AdTarget = AdTarget;
            this.ActiveMediaPlugin = AdHost.ActiveMediaPlugin;
            this.AdHost = AdHost;
            if (AdSource.ClickUrl != null)
            {
                this.NavigateUri = new Uri(AdSource.ClickUrl, UriKind.RelativeOrAbsolute);
            }
        }

        public const string Key_EnforceAdDuration = "Microsoft.Advertising.Vpaid.EnforceAdDuration";

        readonly IMediaPlugin ActiveMediaPlugin;
        readonly IAdTarget AdTarget;
        readonly ICreativeSource AdSource;
        protected IPlayer AdHost;

        private IAdContext currentAdContext;
        private DateTime startTime;
        private bool isLoaded;
        private bool isStarted;
        private bool isPreloaded;

        object IVpaidSupportsPreloading.CurrentAdContext
        {
            get { return currentAdContext; }
        }

        protected override void OnClick()
        {
            OnAdClickThru(new AdClickThruEventArgs(AdSource.ClickUrl, AdSource.Id, false));
            base.OnClick();
        }

        public override TimeSpan AdRemainingTime
        {
            get
            {
                if (currentAdContext.NaturalDuration.HasValue)
                    return DateTime.Now.Subtract(startTime);
                else
                    return TimeSpan.Zero;
            }
        }

        protected override FrameworkElement CreateContentsElement()
        {
            return null;
        }

        public void PreloadAd(double width, double height, string viewMode, int desiredBitrate, string creativeData, string environmentVariables, object appendToAdContext)
        {
            if (appendToAdContext is IAdContext)
            {
                LoadClip(width, height, viewMode, desiredBitrate, creativeData, environmentVariables, appendToAdContext as IAdContext);
                isPreloaded = true;
            }
        }

        public override void InitAd(double width, double height, string viewMode, int desiredBitrate, string creativeData, string environmentVariables)
        {
            // wire up events
            ActiveMediaPlugin.AdProgressUpdated += new Action<IAdaptiveMediaPlugin, IAdContext, AdProgress>(ActiveMediaPlugin_AdProgressUpdated);
            //ActiveMediaPlugin.AdError += new Action<IAdaptiveMediaPlugin, IAdContext>(ActiveMediaPlugin_AdError);
            ((Microsoft.SilverlightMediaFramework.Core.SMFPlayer)AdHost).AdvertisementError += AdClipLinearAdPlayer_AdvertisementError;
            //ActiveMediaPlugin.AdStateChanged += new Action<IAdaptiveMediaPlugin, IAdContext>(ActiveMediaPlugin_AdStateChanged);
            ((Microsoft.SilverlightMediaFramework.Core.SMFPlayer)AdHost).AdvertisementStateChanged += AdClipLinearAdPlayer_AdvertisementStateChanged;

            //ActiveMediaPlugin.AdClickThrough += new Action<IAdaptiveMediaPlugin, IAdContext>(ActiveMediaPlugin_AdClickThrough);

            if (!isPreloaded)
            {
                LoadClip(width, height, viewMode, desiredBitrate, creativeData, environmentVariables, null);
            }
        }

        private void LoadClip(double width, double height, string viewMode, int desiredBitrate, string creativeData, string environmentVariables, IAdContext AppendToAdContext)
        {
            if (ActiveMediaPlugin.SupportsAdScheduling)
            {
                // warning: we need to dispatch or app will crash if we are running this synchronously after a previous clip failed.
                // looks like the SSME needs time to deal with the failure.
                // Update: add the error to the dispatcher instead
                var MediaSource = new Uri(creativeData);
                var mimeType = AdSource.MimeType.ToLower();
                var DeliveryMethod = mimeType == "video/x-ms-wmv" || mimeType == "video/mp4" ? DeliveryMethods.ProgressiveDownload : DeliveryMethods.AdaptiveStreaming;
                var isLive = (ActiveMediaPlugin is ILiveDvrMediaPlugin && ((ILiveDvrMediaPlugin)ActiveMediaPlugin).IsSourceLive);

                // If the user has included a true EnforceAdDuration flag, pad the specified duration to allow for a slight variation in
                // ad length and pass that value to the player - the player will end the ad at the duration (regardless of actual ad length).
                // If the flag is not included or it is false, pass a null duration to the player to let it play the ad in its entirety.

                bool enforceAdDuration = GetEnforceAdDuration(AdHost);

                TimeSpan? adDuration = null;

                double durationPadding = 1.0; // seconds

                if (enforceAdDuration && AdSource.Duration != null && AdSource.Duration.Value.TotalSeconds > durationPadding)
                {
                    adDuration = AdSource.Duration.Value.Add(TimeSpan.FromSeconds(durationPadding));
                }

                Uri clickUri = AdSource.ClickUrl == null ? null : new Uri(AdSource.ClickUrl, UriKind.RelativeOrAbsolute);

                currentAdContext = AdHost.PlayLinearAd(MediaSource, DeliveryMethod, null, null, clickUri, adDuration, !isLive, AppendToAdContext, this);

                base.Init(width, height, viewMode, desiredBitrate, creativeData, environmentVariables);
            }
            else
            {
                OnAdError(new AdMessageEventArgs("ActiveMediaPlugin does not support ad scheduling"));
            }
        }

        void AdClipLinearAdPlayer_AdvertisementStateChanged(object sender, Core.AdvertisementStateChangedInfo e)
        {
            ActiveMediaPlugin_AdStateChanged(ActiveMediaPlugin as IAdaptiveMediaPlugin, e.AdContext);
        } 
        

        void AdClipLinearAdPlayer_AdvertisementError(object sender, Core.CustomEventArgs<IAdContext> e)
        {
            ActiveMediaPlugin_AdError(ActiveMediaPlugin as IAdaptiveMediaPlugin, e.Value);
        }


        public static bool GetEnforceAdDuration(IPlayer player)
        {
            if (player.GlobalConfigMetadata != null && player.GlobalConfigMetadata.ContainsKeyIgnoreCase(Key_EnforceAdDuration))
            {
                var enforceAdDurationObject = player.GlobalConfigMetadata[Key_EnforceAdDuration];

                if (enforceAdDurationObject is bool)
                {
                    return (bool)enforceAdDurationObject;
                }
                else if (enforceAdDurationObject is string)
                {
                    var enforceAdDurationString = (string)enforceAdDurationObject;
                    bool enforceAdDurationResult;
                    if (bool.TryParse(enforceAdDurationString, out enforceAdDurationResult))
                    {
                        return enforceAdDurationResult;
                    }
                }
            }
            return false;
        }

        // dispatching errors so the ssme can catch up. see note in InitAd
        protected override void OnAdError(AdMessageEventArgs e)
        {
            Dispatcher.BeginInvoke(() => base.OnAdError(e));
        }

        void ActiveMediaPlugin_AdClickThrough(IAdaptiveMediaPlugin mediaPlugin, IAdContext adContext)
        {
            if (adContext != null && adContext.Data != this)
                return;

            OnAdClickThru(new AdClickThruEventArgs(adContext.ClickThrough.OriginalString, AdSource.Id, true));
        }

        void ActiveMediaPlugin_AdProgressUpdated(IAdaptiveMediaPlugin mp, IAdContext adContext, AdProgress progress)
        {
            if (adContext != null && adContext.Data != this)
                return;

            switch (progress)
            {
                case AdProgress.Start:
                    startTime = DateTime.Now;
                    OnAdVideoStart();
                    break;
                case AdProgress.FirstQuartile:
                    OnAdVideoFirstQuartile();
                    break;
                case AdProgress.Midpoint:
                    OnAdVideoMidpoint();
                    break;
                case AdProgress.ThirdQuartile:
                    OnAdVideoThirdQuartile();
                    break;
                case AdProgress.Complete:
                    OnAdVideoComplete();
                    break;
            }
        }

        void ActiveMediaPlugin_AdError(IAdaptiveMediaPlugin mp, IAdContext adContext)
        {
            if (adContext != null && adContext.Data != this)
                return;

            OnAdError(new AdMessageEventArgs("An unknown error occured while playing the ad clip."));
        }

        void ActiveMediaPlugin_AdStateChanged(IAdaptiveMediaPlugin mp, IAdContext adContext)
        {           
            if (adContext != null && adContext.Data != this)
                return;

            switch (currentAdContext.CurrentAdState)
            {
                case MediaPluginState.Paused:
                case MediaPluginState.Buffering:
                    if (!isLoaded)
                    {
                        OnAdLoaded();
                        isLoaded = true;
                    }
                    break;
                case MediaPluginState.Playing:
                    if (!isStarted)
                    {
                        OnAdImpression();
                        OnAdStarted();
                        isStarted = true;
                    }
                    break;
            }
        }

        public override double AdVolume
        {
            get
            {
                return ActiveMediaPlugin.Volume * 100;
            }
            set
            {
                ActiveMediaPlugin.Volume = value / 100.0;
            }
        }

        public override void StartAd()
        {
            base.Start();
        }

        public override void ResumeAd()
        {
            ActiveMediaPlugin.Play();
            base.Resume();
        }

        public override void PauseAd()
        {
            ActiveMediaPlugin.Pause();
            base.Pause();
        }

        public override void Dispose()
        {
            if (ActiveMediaPlugin != null)
            {
                //ActiveMediaPlugin.AdClickThrough -= new Action<IAdaptiveMediaPlugin, IAdContext>(ActiveMediaPlugin_AdClickThrough);
                ActiveMediaPlugin.AdProgressUpdated -= new Action<IAdaptiveMediaPlugin, IAdContext, AdProgress>(ActiveMediaPlugin_AdProgressUpdated);

                //ActiveMediaPlugin.AdError -= new Action<IAdaptiveMediaPlugin, IAdContext>(ActiveMediaPlugin_AdError);
                ((Microsoft.SilverlightMediaFramework.Core.SMFPlayer)AdHost).AdvertisementError -= AdClipLinearAdPlayer_AdvertisementError;
                //ActiveMediaPlugin.AdStateChanged -= new Action<IAdaptiveMediaPlugin, IAdContext>(ActiveMediaPlugin_AdStateChanged);
                ((Microsoft.SilverlightMediaFramework.Core.SMFPlayer)AdHost).AdvertisementStateChanged -= AdClipLinearAdPlayer_AdvertisementStateChanged;
            }
            currentAdContext = null;

            base.Dispose();
        }

        public bool Nonlinear
        {
            get { return true; }
        }
    }
}

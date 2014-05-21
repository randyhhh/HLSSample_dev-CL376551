using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Advertising;
using Microsoft.SilverlightMediaFramework.Core;
using Microsoft.SilverlightMediaFramework.Core.Media;
using Microsoft.SilverlightMediaFramework.Plugins.Metadata;
using Microsoft.SilverlightMediaFramework.Plugins.Primitives;
using Microsoft.SilverlightMediaFramework.Plugins.Primitives.Advertising;
using Microsoft.SilverlightMediaFramework.Utilities.Extensions;

namespace Microsoft.SilverlightMediaFramework.Plugins.UAC
{
    /// <summary>
    /// Provides the player framework with the ability to schedule ads using the Microsoft Universal Advertising Client manifest.
    /// </summary>
    [ExportGenericPlugin(PluginName = PluginName, PluginDescription = PluginDescription, PluginVersion = PluginVersion)]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class Scheduler : IGenericPlugin
    {
        #region IPlugin Members
        private const string PluginName = "Microsoft.Advertising.UAC.Scheduler";
        private const string PluginDescription = "Provides SMF with the ability to schedule ads based on the Microsoft UAC ad provider.";
        private const string PluginVersion = "2.2012.1005.0";
        #endregion

        [Obsolete("This key is deprecated; use Key_AdUrl")]
        public const string Key_FreeWheelUrl = "Microsoft.Advertising.FreeWheelUrl";
        [Obsolete("This key is deprecated; use Key_AdUrl")]
        public const string Key_MicrosoftUrl = "Microsoft.Advertising.MicrosoftUrl";
        [Obsolete]
        public const string Key_MediaId = "Microsoft.Advertising.MediaId";
        [Obsolete]
        public const string Key_PlayerName = "Microsoft.Advertising.PlayerName";
        [Obsolete]
        public const string Key_SubPlayerName = "Microsoft.Advertising.SubPlayerName";
        [Obsolete]
        public const string Key_PublisherName = "Microsoft.Advertising.PublisherName";


        public const string Key_AdUrl = "Microsoft.Advertising.AdUrl";
        public const string Key_StartPosition = "Microsoft.Advertising.StartPosition";

        private IPlayer player;
        private bool PlayWhenReady;
        private SchedulingStatus status;
        private List<ScheduledAd> ScheduledAds;
        private PackageAdTrigger PostRoll;
        private Uri pendingMediaSource;

        private AdSchedule adSchedule = null;

        /// <summary>
        /// Ads scheduled before the StartPosition are always excluded because we assume the user has already seen them. 
        /// This property offers additional control which allows you to provide an offset to the start position when excluding ads.
        /// For example: if StartPosition was 30.0001 and the ad was scheduled for 30, a threshold of .0001 or higher would cause the ad to be played.
        /// </summary>
        public TimeSpan StartPositionExcludeTheshold { get; set; }

        public enum SchedulingStatus
        {
            Pending,
            Disabled,
            Downloading,
            ScheduleReady,
            Scheduled,
            Failed,
            Postponed
        }

        #region Public Helpers
        /// <summary>
        /// Helper function to add UAC ad schedule data to a playlist item
        /// </summary>
        /// <param name="item">The playlist item to modify</param>
        /// <param name="AdStream">A stream containing ad data the UAC can handle</param>
        public static void AddAdScheduleToPlaylistItem(PlaylistItem item, string AdUrl)
        {
            item.CustomMetadata.Add(new Utilities.Metadata.MetadataItem() { Key = Key_AdUrl, Value = AdUrl });
        }

        /// <summary>
        /// Helper function to add UAC ad schedule data to a playlist item
        /// </summary>
        /// <param name="item">The playlist item to modify</param>
        /// <param name="AdStream">A stream containing ad data the UAC can handle</param>
        public static void AddAdScheduleToPlaylistItem(PlaylistItem item, Stream AdStream)
        {
            item.CustomMetadata.Add(new Utilities.Metadata.MetadataItem() { Key = Key_AdUrl, Value = AdStream });
        }

        /// <summary>
        /// Helper function to set the maximum ad bitrate in kbps
        /// </summary>
        /// <param name="player">The player to set the max bitrate</param>
        /// <param name="bitrate">The max bitrate in kbps</param>
        public static void SetMaxBitrateinKbps(SMFPlayer player, int bitrate)
        {
            player.GlobalConfigMetadata.Add(new Utilities.Metadata.MetadataItem() { Key = AdHandler.Key_MaxBitrateKbps, Value = bitrate });
        }

        /// <summary>
        /// Helper function to set whether to prefer SmoothStreaming ads or not. 
        /// Set this to true to prefer smooth streaming ads when available, falling back on progressive when they are not.
        /// Set this to false to prefer progressive ads when available, falling back to smooth streaming when they are not.
        /// </summary>
        /// <param name="player">The player to set smooth streaming ad preference</param>
        /// <param name="enabled">The smooth streaming ad preference</param>
        public static void SetSmoothStreamingEnabled(SMFPlayer player, bool enabled)
        {
            player.GlobalConfigMetadata.Add(new Utilities.Metadata.MetadataItem() { Key = AdHandler.Key_IsSmoothEnabled, Value = enabled });
        }

        /// <summary>
        /// Add a piece of content with an ad url to the smf player's playlist
        /// </summary>
        /// <param name="player">The smf player to add the content to</param>
        /// <param name="contentUrl">The url to the content</param>
        /// <param name="method">The delivery method for the content (adaptive or progressive)</param>
        /// <param name="adUrl">The url to the ad schedule</param>
        public static void AddContentWithAdToPlaylist(SMFPlayer player, string contentUrl, DeliveryMethods method, string adUrl)
        {
            AddContentWithAdToPlaylistInternal(player, contentUrl, method, adUrl, TimeSpan.Zero, "assetID");
        }

        /// <summary>
        /// Add a piece of content with an ad stream to the smf player's playlist
        /// </summary>
        /// <param name="player">The smf player to add the content to</param>
        /// <param name="contentUrl">The url to the content</param>
        /// <param name="method">The delivery method for the content (adaptive or progressive)</param>
        /// <param name="adUrl">The stream to the ad schedule</param>
        public static void AddContentWithAdToPlaylist(SMFPlayer player, string contentUrl, DeliveryMethods method, Stream adStream)
        {
            AddContentWithAdToPlaylistInternal(player, contentUrl, method, adStream, TimeSpan.Zero, "assetID");
        }

        /// <summary>
        /// Add a piece of content with an ad url to the smf player's playlist
        /// </summary>
        /// <param name="player">The smf player to add the content to</param>
        /// <param name="contentUrl">The url to the content</param>
        /// <param name="method">The delivery method for the content (adaptive or progressive)</param>
        /// <param name="adUrl">The url to the ad schedule</param>
        /// <param name="startTime">The time in the content where playback will begin</param>
        /// <param name="assetID">The asset ID of the content that is being played</param>
        public static void AddContentWithAdToPlaylist(SMFPlayer player, string contentUrl, DeliveryMethods method, string adUrl, TimeSpan startTime, string assetID)
        {
            AddContentWithAdToPlaylistInternal(player, contentUrl, method, adUrl, startTime, assetID);
        }

        /// <summary>
        /// Add a piece of content with an ad stream or url to the smf player's playlist; internal.
        /// </summary>
        /// <param name="player">The smf player to add the content to</param>
        /// <param name="contentUrl">The url to the content</param>
        /// <param name="method">The delivery method for the content (adaptive or progressive)</param>
        /// <param name="adSchedule">The stream to the ad schedule</param>
        /// <param name="startTime">The time in the content where playback will begin</param>
        /// <param name="assetID">The asset ID of the content that is being played</param>
        internal static void AddContentWithAdToPlaylistInternal(SMFPlayer player, string contentUrl, DeliveryMethods method, object adSchedule, TimeSpan startTime, string assetID)
        {
            PlaylistItem item = new PlaylistItem()
            {
                StartPosition = startTime,
                MediaAssetId = assetID,
                DeliveryMethod = method,
                MediaSource = new Uri(contentUrl),
                CustomMetadata = new Utilities.Metadata.MetadataCollection() 
                {
                    new Utilities.Metadata.MetadataItem() { Key = Key_AdUrl, Value = adSchedule }
                }
            };

            player.Playlist.Add(item);
        }
        #endregion

        public Scheduler()
        {
            // Default the Handler ID to the UAC ad handler format. This could be changed to cause ads to be handled by a different plugin.
            HandlerId = AdHandler.PayloadFormat;
            AutoPlay = true;
            status = SchedulingStatus.Pending;
            StartPositionExcludeTheshold = TimeSpan.FromSeconds(.25);
        }

        /// <summary>
        /// The format ID that the handler must have in order to accept the ad. This allows control over which ad handler (IAdPayloadHandlerPlugin) will handle the ad when triggered.
        /// </summary>
        public string HandlerId { get; set; }

        /// <summary>
        /// Indicates that ads should be automatically loaded and ad playback should begin as soon as possible.
        /// </summary>
        public bool AutoPlay { get; set; }

        /// <summary>
        /// Indicates the status of the scheduler
        /// </summary>
        public SchedulingStatus Status
        {
            get { return status; }
            private set
            {
                if (status != value)
                {
                    status = value;
                    if (StatusChanged != null) StatusChanged(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Fired when ad scheduling status changes
        /// </summary>
        public event EventHandler StatusChanged;

        /// <summary>
        /// Fired when ad scheduling fails due to issues downloading the manifest.
        /// </summary>
        public event EventHandler<SchedulingFailedEventArgs> SchedulingFailed;

        #region IGenericPlugin
        private bool isLoaded;
        public bool IsLoaded
        {
            get { return isLoaded; }
        }

        public event Action<IPlugin, Microsoft.SilverlightMediaFramework.Plugins.Primitives.LogEntry> LogReady;

        public event Action<IPlugin> PluginLoaded;

        public event Action<IPlugin> PluginUnloaded;

        public event Action<IPlugin, Exception> PluginLoadFailed;

        public event Action<IPlugin, Exception> PluginUnloadFailed;

        void IPlugin.Load()
        {
            try
            {
                ScheduledAds = new List<ScheduledAd>();
                if (player != null)
                {
                    player.ContentChanged += new EventHandler(player_ContentChanged);
                }
                isLoaded = true;
                if (PluginLoaded != null) PluginLoaded(this);
            }
            catch (Exception ex)
            {
                if (PluginLoadFailed != null) PluginLoadFailed(this, ex);
            }
        }

        void IPlugin.Unload()
        {
            try
            {
                // clean up all scheduled ads
                CleanupScheduledAds();
                PostRoll = null;
                ScheduledAds = null;
                // disconnect the player
                if (player != null)
                {
                    player.ContentChanged -= new EventHandler(player_ContentChanged);
                    player = null;
                }
                isLoaded = false;
                if (PluginUnloaded != null) PluginUnloaded(this);
            }
            catch (Exception ex)
            {
                if (PluginUnloadFailed != null) PluginUnloadFailed(this, ex);
            }
        }

        void IPlayerConsumer.SetPlayer(FrameworkElement Player)
        {
            player = Player as IPlayer;
        }
        #endregion

        private void CleanupScheduledAds()
        {
            foreach (var scheduledAd in ScheduledAds.ToList())
            {
                CleanupScheduledAd(scheduledAd);
            }
        }

        void player_ContentChanged(object sender, EventArgs e)
        {
            pendingMediaSource = null;
            PlayWhenReady = false;
            PostRoll = null;
            adSchedule = null;
            CleanupScheduledAds();
            if (player.ActiveMediaPlugin != null)
            {
                Status = SchedulingStatus.Downloading;
                // block the player from starting. We need to get a video manifest first in case there is a preroll.
                BlockPlayback();

                System.Windows.Deployment.Current.Dispatcher.BeginInvoke(() =>
                {
                    object adData = null;
// Disable warning on using freewheel and microsoft keys
#pragma warning disable 618
                    if (player.ContentMetadata.ContainsKey(Key_FreeWheelUrl))
                    {
                        adData = player.ContentMetadata[Key_FreeWheelUrl];
                    }
                    if (player.ContentMetadata.ContainsKey(Key_MicrosoftUrl))
                    {
                        adData = player.ContentMetadata[Key_MicrosoftUrl];
                    }
#pragma warning restore 618

                    if (player.ContentMetadata.ContainsKey(Key_AdUrl))
                    {
                        adData = player.ContentMetadata[Key_AdUrl];
                    }
                    if (adData != null)
                    {
                        if (adData is String || adData is Stream)
                        {
                            try
                            {
                                if (adData is String)
                                {
                                    string uri = adData as String;
                                    if (Uri.IsWellFormedUriString(uri, UriKind.RelativeOrAbsolute) == false)
                                    {
                                        uri = Uri.EscapeUriString(uri);
                                    }
                                    AdManager.GetSchedule(uri, ScheduleDownloadedHandler);
                                }
                                else if (adData is Stream)
                                {
                                    AdManager.GetSchedule(adData as Stream, ScheduleDownloadedHandler);
                                }
                            }
                            catch (Exception ex)
                            {
                                HandleManifestDownloadFailed(ex);
                            }
                        }
                        else
                        {
                            SendLogEntry("UAC Metadata type is not supported", Microsoft.SilverlightMediaFramework.Plugins.Primitives.LogLevel.Information, "Skipping UAC ad scheduling");
                        }
                    }
                    else
                    {

                        if (AutoPlay || PlayWhenReady)
                            ReleasePlayback();
                        Status = SchedulingStatus.Failed;

                        SendLogEntry("UAC Metadata not found", Microsoft.SilverlightMediaFramework.Plugins.Primitives.LogLevel.Information, "Skipping UAC ad scheduling");
                        return;
                    }
                });
            }
            else
            {
                Status = SchedulingStatus.Disabled;
            }
        }

        private void ScheduleDownloadedHandler(Exception error, AdSchedule schedule)
        {
            System.Windows.Deployment.Current.Dispatcher.BeginInvoke(() =>
            {
                if (error != null)
                {
                    HandleManifestDownloadFailed(error);
                    return;
                }

                if (IsLoaded)
                {
                    this.adSchedule = schedule;
                    if (AutoPlay || PlayWhenReady)
                    {
                        try
                        {
                            Status = SchedulingStatus.ScheduleReady;
                        }
                        catch (Exception ex)
                        {
                            Status = SchedulingStatus.Failed;
                            if (SchedulingFailed != null) SchedulingFailed(this, new SchedulingFailedEventArgs(ex));
                        }
                        finally
                        {
                            // release the player block
                            ReleasePlayback();
                        }
                    }
                    else
                    {
                        Status = SchedulingStatus.Postponed;
                    }
                }
                else
                {
                    Status = SchedulingStatus.Disabled;
                }
            });
        }

        private void HandleManifestDownloadFailed(Exception ex)
        {
            adSchedule = null;
            if (AutoPlay || PlayWhenReady)
                ReleasePlayback();

            Status = SchedulingStatus.Failed;

            if (SchedulingFailed != null)
                SchedulingFailed(this, new SchedulingFailedEventArgs(ex));

            SendLogEntry("UAC VideoManifest.DownloadAsync Failed", Microsoft.SilverlightMediaFramework.Plugins.Primitives.LogLevel.Error, ex.Message);
        }

        private void PreparePostRolls()
        {
            PostRoll = null;
            if (player.ActiveMediaPlugin != null && player.ActiveMediaPlugin.Duration != TimeSpan.Zero)
            {
                var duration = player.ActiveMediaPlugin.Duration;
                // save this for the MediaEnded event
                var scheduledPostRoll = ScheduledAds.FirstOrDefault(a => ((PackageAdTrigger)a.Trigger).StartTime == duration);
                if (scheduledPostRoll != null)
                {
                    CleanupScheduledAd(scheduledPostRoll);
                    PostRoll = scheduledPostRoll.Trigger as PackageAdTrigger;
                    player.ActiveMediaPlugin.MediaEnded += ActiveMediaPlugin_MediaEnded;
                }
            }
        }

        void ActiveMediaPlugin_MediaEnded(IMediaPlugin obj)
        {
            obj.MediaEnded -= ActiveMediaPlugin_MediaEnded;
            PlayPostRoll();
        }

        void PlayPostRoll()
        {
            if (PostRoll != null)
            {
                // play postroll
                var scheduledAd = player.ScheduleAdTrigger(PostRoll, null);
                ScheduledAds.Add(scheduledAd);
                scheduledAd.Deactivated += scheduledAd_Deactivated;
                PostRoll = null;
            }
        }

        private void ReleasePlayback()
        {
            player.ReleasePlayBlock(this);

            var smf = (SMFPlayer)player;
            player.ActiveMediaPlugin.AutoPlay = !ContainsPreroll();
            smf.CurrentPlaylistItem.MediaSource = pendingMediaSource;
            player.ActiveMediaPlugin.VisualElement.IfNotNull(v => v.Visibility = Visibility.Collapsed);
            if (player.ActiveMediaPlugin is IAdaptiveMediaPlugin)
            {
                var adaptiveMediaPlugin = (IAdaptiveMediaPlugin)player.ActiveMediaPlugin;
                adaptiveMediaPlugin.ManifestReady += adaptiveMediaPlugin_ManifestReady;
                adaptiveMediaPlugin.AdaptiveSource = pendingMediaSource;
            }
            else
            {
                player.ActiveMediaPlugin.Source = pendingMediaSource;
            }
            player.ActiveMediaPlugin.MediaOpened += mediaPlugin_MediaOpened;
        }

        void mediaPlugin_MediaOpened(IMediaPlugin obj)
        {
            obj.MediaOpened -= mediaPlugin_MediaOpened;
            player.ActiveMediaPlugin.VisualElement.IfNotNull(v => v.Visibility = Visibility.Visible);
            ScheduleAds();
        }

        void adaptiveMediaPlugin_ManifestReady(IAdaptiveMediaPlugin obj)
        {
            obj.ManifestReady -= adaptiveMediaPlugin_ManifestReady;
            ScheduleAds();
        }

        private void ScheduleAds()
        {
            if (Status == SchedulingStatus.ScheduleReady)
            {
                // schedule ads
                if (ReadManifest())
                {
                    Status = SchedulingStatus.Scheduled;
                    PreparePostRolls();
                }
                else
                {
                    HandleManifestDownloadFailed(new System.Exception("No valid ads in schedule"));
                }
            }
        }

        private void BlockPlayback()
        {
            player.AddPlayBlock(this);
            var smf = (SMFPlayer)player;
            pendingMediaSource = smf.CurrentPlaylistItem.MediaSource;
            smf.CurrentPlaylistItem.MediaSource = null;
        }

        /// <summary>
        /// Manually force ads to get scheduled and start playing. Only use this if AutoPlay = false.
        /// </summary>
        public void Play()
        {
            try
            {
                switch (Status)
                {
                    case SchedulingStatus.Postponed:
                        // Schedule ads found in the video manifest
                        Status = SchedulingStatus.ScheduleReady;
                        ReleasePlayback();
                        break;
                    case SchedulingStatus.Downloading:
                        // we're still loading the manifest, play as soon as possible
                        PlayWhenReady = true;
                        break;
                    case SchedulingStatus.Failed:
                        // we failed, but at least we can release the player block
                        ReleasePlayback();
                        break;
                    case SchedulingStatus.Disabled:
                        throw new Exception("Invalid request, Scheduler is disabled");
                    case SchedulingStatus.Pending:
                        throw new Exception("Invalid request, Scheduler is pending and cannot be played until PlaylistItemChanged event finishes");
                    case SchedulingStatus.Scheduled:
                    case SchedulingStatus.ScheduleReady:
                        throw new Exception("Invalid request, Scheduler has already been scheduled");
                }
            }
            catch
            {
                // release the player block
                ReleasePlayback();
                throw;
            }
        }

        /// <summary>
        /// Checks a pod to see if there is any ad with no error and at least one video asset.
        /// </summary>
        /// <param name="pod">The pod to check</param>
        /// <returns>True if the pod has a valid ad</returns>
        private bool PodHasValidAd(AdPod pod)
        {
            bool validPod = false;
            foreach (var adPackage in pod.AdPackages)
            {
                if (adPackage.Error == null && adPackage.VideoResources.Count > 0)
                {
                    validPod = true;
                    break;
                }
            }
            return validPod;
        }

        /// <summary>
        /// Schedule ads found in the video manifest
        /// </summary>
        private bool ReadManifest()
        {
            bool scheduled = false;
            TimeSpan StartPosition = player.ContentStartPosition.GetValueOrDefault(TimeSpan.Zero);
            foreach (var pod in adSchedule.AdPods)
            {
                if (PodHasValidAd(pod))
                {
                    var startTime = pod.ScheduledTime;

                    var scheduledAd = player.ScheduleAdTrigger(
                        new PackageAdTrigger(pod, HandlerId, startTime), startTime.Subtract(StartPosition).Duration() <= StartPositionExcludeTheshold ? (TimeSpan?)null : startTime);  // force starttime == null to play NOW and not wait until marker manager is evaluated on timer
                    if (scheduledAd != null)
                    {
                        // Make sure there's at least one valid pod in this
                        scheduled = true;
                        scheduledAd.Deactivated += scheduledAd_Deactivated;
                        ScheduledAds.Add(scheduledAd);
                    }
                }
            }

            return scheduled;
        }

        private bool ContainsPreroll()
        {
            if (adSchedule != null)
            {
                TimeSpan StartPosition = player.ContentStartPosition.GetValueOrDefault(TimeSpan.Zero);
                foreach (var pod in adSchedule.AdPods)
                {
                    if (pod.ScheduledTime.Subtract(StartPosition).Duration() <= StartPositionExcludeTheshold && PodHasValidAd(pod))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void scheduledAd_Deactivated(object sender, EventArgs e)
        {
            CleanupScheduledAd(sender as ScheduledAd);
        }

        private void CleanupScheduledAd(ScheduledAd scheduledAd)
        {
            scheduledAd.Deactivated -= scheduledAd_Deactivated;
            ScheduledAds.Remove(scheduledAd);
            player.IfNotNull(p => p.RemoveScheduledAd(scheduledAd));
        }

        private IEnumerable<AdPod> GetActiveElements(AdSchedule schedule)
        {
            TimeSpan? startPosition = null;

            if (player.ContentMetadata.ContainsKey(Key_StartPosition))
            {
                object startPositionObject = player.ContentMetadata[Key_StartPosition];
                if (startPositionObject is TimeSpan)
                {
                    startPosition = (TimeSpan)startPositionObject;
                }
                else if (startPositionObject is string)
                {
                    var startPositionString = (string)startPositionObject;
                    TimeSpan startPositionResult;
                    if (TimeSpan.TryParse(startPositionString, out startPositionResult))
                    {
                        startPosition = startPositionResult;
                    }
                }
            }
            if (!startPosition.HasValue)
            {
                startPosition = player.ContentStartPosition;
            }

            IEnumerable<AdPod> pods = schedule.AdPods;
            if (startPosition.HasValue)
            {
                pods = pods.Where(pod => pod.ScheduledTime >= startPosition.Value.Subtract(StartPositionExcludeTheshold));
            }

            return pods;
        }

        protected void SendLogEntry(string type,
                                  Microsoft.SilverlightMediaFramework.Plugins.Primitives.LogLevel severity = Microsoft.SilverlightMediaFramework.Plugins.Primitives.LogLevel.Information,
                                  string message = null,
                                  DateTime? timeStamp = null,
                                  IEnumerable<KeyValuePair<string, object>> extendedProperties = null)
        {
            if (LogReady != null)
            {
                var logEntry = new LogEntry
                {
                    Severity = severity,
                    Message = message,
                    SenderName = PluginName,
                    Timestamp = timeStamp.HasValue ? timeStamp.Value : DateTime.Now
                };

                extendedProperties.ForEach(logEntry.ExtendedProperties.Add);
                LogReady(this, logEntry);
            }

        }
    }

    public class SchedulingFailedEventArgs : EventArgs
    {
        public SchedulingFailedEventArgs(Exception ex)
        {
            this.Error = ex;
        }

        public Exception Error { get; private set; }
    }
}

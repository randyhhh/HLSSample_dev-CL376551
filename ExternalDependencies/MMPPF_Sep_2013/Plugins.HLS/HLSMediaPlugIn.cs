using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.SilverlightMediaFramework.Plugins.HLS.Resources;
using Microsoft.SilverlightMediaFramework.Plugins.Metadata;
using Microsoft.SilverlightMediaFramework.Plugins.Primitives;
using Microsoft.SilverlightMediaFramework.Utilities.Extensions;
using Silverlight.Samples.HttpLiveStreaming;
using System.Windows.Threading;
using org.OpenVideoPlayer;
//using Microsoft.Xbox;
//using Microsoft.Xbox.Core;
//using Microsoft.Xbox.Controls;

namespace Microsoft.SilverlightMediaFramework.Plugins.HLS
{
    /// <summary>
    /// Represents a media plug-in that can play Http Live Streaming (HLS) download media.
    /// </summary>
    [ExportMediaPlugin(PluginName = PluginName,
        PluginDescription = PluginDescription,
        PluginVersion = PluginVersion,
        SupportedDeliveryMethods = SupportedDeliveryMethodsInternal,
        SupportsLiveDvr = true,
        SupportedMediaTypes = new string[] { "video/MP2T", "application/x-mpegURL", "application/vnd.apple.mpegURL" })]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class HLSMediaPlugin : IVariableBitrateMediaPlugin,ILiveDvrMediaPlugin
    {
        private const string PluginName = "HLSMediaPlugin";

        private const string PluginDescription =
            "Provides Http Live Streaming capabilities for the Silverlight Media Framework by wrapping the MediaElement.";

        private const string PluginVersion = "2.2011.1113.0";

        private const DeliveryMethods SupportedDeliveryMethodsInternal = DeliveryMethods.NotSpecified | DeliveryMethods.AdaptiveStreaming;

        private const double SupportedPlaybackRate = 1;

        protected MediaElement MediaElement { get; set; }
        private TimeSpan _liveDvrMinDuration = TimeSpan.FromSeconds(120);

        /// <summary>
        /// Use 30 second buffer
        /// </summary>
        protected readonly TimeSpan BufferLength = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Media stream source
        /// </summary>
        protected HLSMediaStreamSource _mss;
        
        #region Events

        /// <summary>
        /// Occurs when the plug-in is loaded.
        /// </summary>
        public event Action<IPlugin> PluginLoaded;

        /// <summary>
        /// Occurs when the plug-in is unloaded.
        /// </summary>
        public event Action<IPlugin> PluginUnloaded;

        /// <summary>
        /// Occurs when an exception occurs when the plug-in is loaded.
        /// </summary>
        public event Action<IPlugin, Exception> PluginLoadFailed;

        /// <summary>
        /// Occurs when an exception occurs when the plug-in is unloaded.
        /// </summary>
        public event Action<IPlugin, Exception> PluginUnloadFailed;

        /// <summary>
        /// Occurs when the log is ready.
        /// </summary>
        public event Action<IPlugin, LogEntry> LogReady;

        //IMediaPlugin Events

        /// <summary>
        /// Occurs when a seek operation has completed.
        /// </summary>
        public event Action<IMediaPlugin> SeekCompleted;

        /// <summary>
        /// Occurs when the percent of the media being buffered changes.
        /// </summary>
        public event Action<IMediaPlugin, double> BufferingProgressChanged;

        /// <summary>
        /// Occurs when the percent of the media downloaded changes.
        /// </summary>
        public event Action<IMediaPlugin, double> DownloadProgressChanged;

        /// <summary>
        /// Occurs when a marker defined for the media file has been reached.
        /// </summary>
        public event Action<IMediaPlugin, MediaMarker> MarkerReached;

        /// <summary>
        /// Occurs when the media reaches the end.
        /// </summary>
        public event Action<IMediaPlugin> MediaEnded;

        /// <summary>
        /// Occurs when the media does not open successfully.
        /// </summary>
        public event Action<IMediaPlugin, Exception> MediaFailed;

        /// <summary>
        /// Occurs when the media successfully opens.
        /// </summary>
        public event Action<IMediaPlugin> MediaOpened;

        /// <summary>
        /// Occurs when the state of playback for the media changes.
        /// </summary>
        public event Action<IMediaPlugin, MediaPluginState> CurrentStateChanged;

#pragma warning disable 67
        /// <summary>
        /// Occurs when the user clicks on an ad.
        /// </summary>
        public event Action<IAdaptiveMediaPlugin, IAdContext> AdClickThrough;

        /// <summary>
        /// Occurs when there is an error playing an ad.
        /// </summary>
        public event Action<IAdaptiveMediaPlugin, IAdContext> AdError;

        /// <summary>
        /// Occurs when the progress of the currently playing ad has been updated.
        /// </summary>
        public event Action<IAdaptiveMediaPlugin, IAdContext, AdProgress> AdProgressUpdated;

        /// <summary>
        /// Occurs when the state of the currently playing ad has changed.
        /// </summary>
        public event Action<IAdaptiveMediaPlugin, IAdContext> AdStateChanged;

        /// <summary>
        /// Occurs when the media's playback rate changes.
        /// </summary>
        public event Action<IMediaPlugin> PlaybackRateChanged;
#pragma warning restore 67

        #endregion

        #region Properties       
        public CacheMode CacheMode
        {
            get { return MediaElement != null ? MediaElement.CacheMode : null; }
            set { MediaElement.IfNotNull(i => i.CacheMode = value); }
        }

        /// <summary>
        /// Gets a value indicating whether a plug-in is currently loaded.
        /// </summary>
        public bool IsLoaded { get; private set; }

        /// <summary>
        /// Gets or sets a value that indicates whether the media file starts to play immediately after it is opened.
        /// </summary>
        public bool AutoPlay
        {
            get { return MediaElement != null && MediaElement.AutoPlay; }
            set { MediaElement.IfNotNull(i => i.AutoPlay = value); }
        }

        /// <summary>
        /// Gets or sets the ratio of the volume level across stereo speakers.
        /// </summary>
        /// <remarks>
        /// The value is in the range between -1 and 1. The default value of 0 signifies an equal volume between left and right stereo speakers.
        /// A value of -1 represents 100 percent volume in the speakers on the left, and a value of 1 represents 100 percent volume in the speakers on the right. 
        /// </remarks>
        public double Balance
        {
            get { return MediaElement != null ? MediaElement.Balance : default(double); }
            set { MediaElement.IfNotNull(i => i.Balance = value); }
        }
        public bool? CanPauseOverride { get; set; }
        /// <summary>
        /// Gets a value indicating if the current media item can be paused.
        /// </summary>
        public bool CanPause
        {
            get { return CanPauseOverride != null ? (bool)CanPauseOverride : MediaElement != null && MediaElement.CanPause;  }           
        }
        public bool? CanSeekOverride { get; set; }
        /// <summary>
        /// Gets a value indicating if the current media item allows seeking to a play position.
        /// </summary>
        public bool CanSeek
        {
            get { return CanSeekOverride != null ? (bool)CanSeekOverride : MediaElement != null && MediaElement.CanSeek; }
        }
        public TimeSpan? DurationOverride { get; set; }
        /// <summary>
        /// Gets the total time of the current media item.
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                return DurationOverride != null && IsDvrLive ? (TimeSpan)DurationOverride : MediaElement != null && MediaElement.NaturalDuration.HasTimeSpan ? MediaElement.NaturalDuration.TimeSpan : TimeSpan.Zero;
            }
        }
        public TimeSpan? EndPositionOverride { get; set; }
        /// <summary>
        /// Gets the end time of the current media item.
        /// </summary>
        public TimeSpan EndPosition
        {
            get { return EndPositionOverride != null && IsDvrLive ? (TimeSpan)EndPositionOverride : Duration; }
        }

        /// <summary>
        /// Gets or sets a value indicating if the current media item is muted so that no audio is playing.
        /// </summary>
        public bool IsMuted
        {
            get { return MediaElement != null && MediaElement.IsMuted; }
            set { MediaElement.IfNotNull(i => i.IsMuted = value); }
        }

        /// <summary>
        /// Gets or sets the LicenseAcquirer associated with the IMediaPlugin. 
        /// The LicenseAcquirer handles acquiring licenses for DRM encrypted content.
        /// </summary>
        public LicenseAcquirer LicenseAcquirer
        {
            get
            {
                return MediaElement != null
                           ? MediaElement.LicenseAcquirer
                           : null;
            }
            set { MediaElement.IfNotNull(i => i.LicenseAcquirer = value); }
        }

        /// <summary>
        /// Gets the size value (unscaled width and height) of the current media item.
        /// </summary>
        public Size NaturalVideoSize
        {
            get
            {
                return MediaElement != null
                           ? new Size(MediaElement.NaturalVideoWidth, MediaElement.NaturalVideoHeight)
                           : Size.Empty;
            }
        }

        /// <summary>
        /// Gets the play speed of the current media item.
        /// </summary>
        /// <remarks>
        /// A rate of 1.0 is normal speed.
        /// </remarks>
        public double PlaybackRate
        {
            get { return SupportedPlaybackRate; }
            set
            {
                if (value != SupportedPlaybackRate)
                {
                    throw new InvalidPlaybackRateException(value);
                }
            }
        }
        public MediaPluginState? CurrentStateOverride { get; set; }
        /// <summary>
        /// Gets the current state of the media item.
        /// </summary>
        public MediaPluginState CurrentState
        {
            get
            {
                if (CurrentStateOverride != null)
                    return (MediaPluginState)CurrentStateOverride;
                return MediaElement != null
                           ? ConvertToPlayState(MediaElement.CurrentState)
                           : MediaPluginState.Stopped;
            }
        }

        /// <summary>
        /// Gets the current position of the clip.
        /// </summary>
        public TimeSpan ClipPosition
        {
            get
            {
                return TimeSpan.Zero;
            }
        }

        public TimeSpan? PositionOverride
        {
            get;
            set;
        }
        /// <summary>
        /// Gets the current position of the media item.
        /// </summary>
        public TimeSpan Position
        {
            get
            {                
                if (IsDvrLive && PositionOverride != null)
                    return (TimeSpan)PositionOverride;
                else
                {
                    return MediaElement != null
                               ? MediaElement.Position
                               : TimeSpan.Zero;
                }
            }
            set
            {
                if (MediaElement != null)
                {
                    PositionOverride = value;
                    MediaElement.Position = value;
                    SeekCompleted.IfNotNull(i => i(this));
                }
            }
        }

        /// <summary>
        /// Gets whether this plugin supports ad scheduling.
        /// </summary>
        public bool SupportsAdScheduling
        {
            get { return true; }
        }
        public TimeSpan? StartPositionOverride { get; set; }
        /// <summary>
        /// Gets the start position of the current media item (0).
        /// </summary>
        public TimeSpan StartPosition
        {
            get { return StartPositionOverride != null && IsDvrLive ? (TimeSpan)StartPositionOverride : TimeSpan.Zero; }
        }

        /// <summary>
        /// Gets the stretch setting for the current media item.
        /// </summary>
        public Stretch Stretch
        {
            get
            {
                return MediaElement != null
                           ? MediaElement.Stretch
                           : default(Stretch);
            }
            set { MediaElement.IfNotNull(i => i.Stretch = value); }
        }

        /// <summary>
        /// Gets or sets a boolean value indicating whether to 
        /// enable GPU acceleration.  In the case of the 
        /// MediaElement, the CacheMode being set to BitmapCache
        /// is the equivalent of setting EnableGPUAcceleration = true
        /// </summary>
        public bool EnableGPUAcceleration
        {
            get
            {
                return MediaElement != null
                           ? MediaElement.CacheMode is BitmapCache
                           : false;
            }
            set
            {
                if (value)
                    MediaElement.IfNotNull(i => i.CacheMode = new BitmapCache());
                else
                    MediaElement.IfNotNull(i => i.CacheMode = null);
            }
        }

        /// <summary>
        /// Gets the delivery methods supported by this plugin.
        /// </summary>
        public DeliveryMethods SupportedDeliveryMethods
        {
            get { return SupportedDeliveryMethodsInternal; }
        }

        /// <summary>
        /// Gets a collection of the playback rates for the current media item.
        /// </summary>
        public IEnumerable<double> SupportedPlaybackRates
        {
            get { return new[] { SupportedPlaybackRate }; }
        }

        /// <summary>
        /// Gets a reference to the media player control.
        /// </summary>
        public FrameworkElement VisualElement
        {
            get { return MediaElement; }
        }

        /// <summary>
        /// Gets or sets the initial volume setting as a value between 0 and 1.
        /// </summary>
        public double Volume
        {
            get
            {
                return MediaElement != null
                           ? MediaElement.Volume
                           : 0;
            }
            set { MediaElement.IfNotNull(i => i.Volume = value); }
        }

        /// <summary>
        /// Gets the dropped frames per second.
        /// </summary>
        public double DroppedFramesPerSecond
        {
            get
            {
                return MediaElement != null
                           ? MediaElement.DroppedFramesPerSecond
                           : 0;
            }
        }

        /// <summary>
        /// Gets the rendered frames per second.
        /// </summary>
        public double RenderedFramesPerSecond
        {
            get
            {
                return MediaElement != null
                           ? MediaElement.RenderedFramesPerSecond
                           : 0;
            }
        }
        public double? BufferingProgressOverride { get; set; }
        /// <summary>
        /// Gets the percentage of the current buffering that is completed.
        /// </summary>
        public double BufferingProgress
        {
            get
            {
                return BufferingProgressOverride != null ? (double)BufferingProgressOverride : (MediaElement != null ? MediaElement.BufferingProgress : default(double));
            }
        }
       
        /// <summary>
        /// Gets or sets the amount of time for the current buffering action.
        /// </summary>
        public TimeSpan BufferingTime
        {
            get
            {
                return MediaElement != null
                           ? MediaElement.BufferingTime
                           : TimeSpan.Zero;
            }
            set { MediaElement.IfNotNull(i => i.BufferingTime = value); }
        }
        public double? DownloadProgressOverride { get; set; }
        /// <summary>
        /// Gets the percentage of the current buffering that is completed
        /// </summary>
        public double DownloadProgress
        {
            get
            {
                return DownloadProgressOverride != null ? (double)DownloadProgressOverride : MediaElement != null ? MediaElement.DownloadProgress : 0;
            }
        }
        public double? DownloadProgressOffsetOverride { get; set; }
        /// <summary>
        /// Gets the download progress offset
        /// </summary>
        public double DownloadProgressOffset
        {
            get
            {
                return DownloadProgressOffsetOverride != null ? (double)DownloadProgressOffsetOverride : MediaElement != null ? MediaElement.DownloadProgressOffset : 0;
            }
        }

        /// <summary>
        /// Gets or sets the location of the media file.
        /// </summary>
        public Uri Source
        {
            get
            {
                return MediaElement != null
                           ? MediaElement.Source
                           : null;
            }
            set
            {
                if (MediaElement != null)
                {
                    if (value == null) return;
                    // Create media stream source
                    _mss = new HLSMediaStreamSource(new HLSMediaStreamSourceOpenParam() { uri = value });   
                 

                    //Load the plugin and configuration
                    MediaAnalyticsHelper.LoadMediaAnalytics(
                        null,
                        new Uri("http://79423.analytics.edgesuite.net/csma/configuration/CSMASampleConfiguration.xml")//CONFIG_PATH
                    );
                    //MediaAnalyticsHelper.AssignHLSPlayback(MediaElement, _mss.Playback);

                    // Optional initialization
                    _mss.BufferLength = TimeSpan.FromSeconds(30);
                    _mss.Playback.PlaybackBitrateChanged += Async_PlaybackBitrateChanged;
                    _mss.Playback.VariantSelector = new VariantSelector(BitrateCommand.Auto);                    
                    // Start playback
                    MediaElement.SetSource(_mss);                    
                }
            }
        }

        #region IVariableBitrateMediaPlugin
        public long MaximumPossibleBitrate { get { return _mss != null ? _mss.MaxBitrate : 0; } }

        public event Action<IVariableBitrateMediaPlugin, long> VideoPlaybackBitrateChanged;
        #endregion

        /// <summary>
        /// Updates bitrate display
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="newBandwidth"></param>
        private void Async_PlaybackBitrateChanged(object sender, uint newBandwidth)
        {
            double signalLevel = 0.00;

            if (_mss.Playback.VariantSelector != null && _mss.Playback.VariantSelector.Variants != null
                && _mss.Playback.VariantSelector.Variants.Count > 0)
            {
                foreach (var bitrate in _mss.Playback.VariantSelector.Variants)
                {
                    signalLevel += 1.00;
                    if (bitrate.Bitrate == newBandwidth)
                        break;
                }

                signalLevel = signalLevel / (double)_mss.Playback.VariantSelector.Variants.Count;
            }

            uint displayLevel = 0;

            if (signalLevel >= 0.00 && signalLevel < .33)
                displayLevel = 1;
            else if (signalLevel >= 0.33 && signalLevel < .66)
                displayLevel = 2;
            else if (signalLevel >= 0.66 && signalLevel < 1.00)
                displayLevel = 3;
            else if (signalLevel == 1.00)
                displayLevel = 4;
            else
                displayLevel = 0;

            //MediaElement.Dispatcher.BeginInvoke(new Action<uint>(MSS_OnPlaybackBitrateChanged), newBandwidth);
            MediaElement.Dispatcher.BeginInvoke(new Action<uint>(MSS_OnPlaybackBitrateChanged), displayLevel);
        }

        /// <summary>
        /// Updates bitrate display
        /// </summary>
        /// <param name="newBandwidth"></param>
        private void MSS_OnPlaybackBitrateChanged(uint newBandwidth)
        {
            VideoPlaybackBitrateChanged.IfNotNull(i => i(this, newBandwidth));
        }


        private Stream _streamSource;
        public Stream StreamSource
        {
            get { return _streamSource; }

            set
            {
                if (MediaElement != null)
                {
                    MediaElement.SetSource(value);
                    _streamSource = value;
                }
            }
        }

        #endregion

        private DispatcherTimer _pauseTimer = null;
        /// <summary>
        /// Starts playing the current media file from its current position.
        /// </summary>
        public void Play()
        {
            
            _pauseTimer.Stop();
            //Position = (TimeSpan)PositionOverride;
            MediaElement.IfNotNull(i => i.Play());
        }

        /// <summary>
        /// Pauses the currently playing media.
        /// </summary>
        public void Pause()
        {
            MediaElement.IfNotNull(i => i.Pause());
            _pauseTimer.Start();
        }

        /// <summary>
        /// Stops playing the current media.
        /// </summary>
        public void Stop()
        {
            MediaElement.IfNotNull(i => i.Stop());
#if HACK_1023
            // HACK to fix memory leak due to MediaElement being removed from the visual tree before Unload is called.
            // Stop is the next best place to do this cleanup because it is fired while the MediaElement is still in the VisualTree.
            MediaElement.Source = null;
#endif
        }

        /// <summary>
        /// Loads a plug-in for playing HLS media.
        /// </summary>
        public void Load()
        {
            try
            {
                if (_pauseTimer == null)
                {
                    _pauseTimer = new DispatcherTimer();
                    _pauseTimer.Interval = TimeSpan.FromMilliseconds(1000);
                    _pauseTimer.Tick += new EventHandler(_pauseTimer_Tick);

                }

                InitializeMediaElement();
                IsLoaded = true;
                SeekCompleted.IfNotNull(i => i(this));
                PluginLoaded.IfNotNull(i => i(this));
                SendLogEntry(KnownLogEntryTypes.MediaPluginLoaded, message: HLSMediaPluginResources.MediaPluginLoadedLogMessage);
            }
            catch (Exception ex)
            {
                PluginLoadFailed.IfNotNull(i => i(this, ex));
            }
        }

        void _pauseTimer_Tick(object sender, EventArgs e)
        {
            if (PositionOverride > TimeSpan.Zero)
                PositionOverride = PositionOverride - TimeSpan.FromSeconds(1);
            if (PositionOverride < TimeSpan.Zero)
                PositionOverride = TimeSpan.Zero;
        }

        /// <summary>
        /// Unloads a plug-in for HLS media.
        /// </summary>
        public void Unload()
        {
            try
            {
                DestroyMediaElement();
                IsLoaded = false;
                PluginUnloaded.IfNotNull(i => i(this));
                SendLogEntry(KnownLogEntryTypes.MediaPluginUnloaded, message: HLSMediaPluginResources.MediaPluginUnloadedLogMessage);
            }
            catch (Exception ex)
            {
                PluginUnloadFailed.IfNotNull(i => i(this, ex));
            }
        }

        /// <summary>
        /// Requests that this plugin generate a LogEntry via the LogReady event
        /// </summary>
        public void RequestLog()
        {
            MediaElement.IfNotNull(i => i.RequestLog());
        }

        /// <summary>
        /// Schedules an ad to be played by this plugin.
        /// </summary>
        /// <param name="adSource">The source of the ad content.</param>
        /// <param name="deliveryMethod">The delivery method of the ad content.</param>
        /// <param name="duration">The duration of the ad content that should be played.  If ommitted the plugin will play the full duration of the ad content.</param>
        /// <param name="startTime">The position within the media where this ad should be played.  If ommited ad will begin playing immediately.</param>
        /// <param name="clickThrough">The URL where the user should be directed when they click the ad.</param>
        /// <param name="pauseTimeline">Indicates if the timeline of the currently playing media should be paused while the ad is playing.</param>
        /// <param name="appendToAd">Another scheduled ad that this ad should be appended to.  If ommitted this ad will be scheduled independently.</param>
        /// <param name="data">User data.</param>
        /// <returns>A reference to the IAdContext that contains information about the scheduled ad.</returns>
        public IAdContext ScheduleAd(Uri adSource, DeliveryMethods deliveryMethod, TimeSpan? duration = null,
                                     TimeSpan? startTime = null, TimeSpan? startOffset = null, Uri clickThrough = null, bool pauseTimeline = true,
                                     IAdContext appendToAd = null, object data = null)
        {
            throw new NotImplementedException();
        }


    

 




        private void InitializeMediaElement()
        {
            if (MediaElement == null)
            {
                MediaElement = new MediaElement();
                MediaElement.MediaOpened += MediaElement_MediaOpened;
                MediaElement.MediaFailed += MediaElement_MediaFailed;
                MediaElement.MediaEnded += MediaElement_MediaEnded;
                
                MediaElement.CurrentStateChanged += MediaElement_CurrentStateChanged;
#if !WINDOWS_PHONE
                MediaElement.MarkerReached += MediaElement_MarkerReached;
#endif
                MediaElement.BufferingProgressChanged += MediaElement_BufferingProgressChanged;
                MediaElement.DownloadProgressChanged += MediaElement_DownloadProgressChanged;
                MediaElement.LogReady += MediaElement_LogReady;                
                MediaElement.BufferingTime = TimeSpan.FromSeconds(30);

                
            }           
            
        }
       

        private void DestroyMediaElement()
        {
            if (_mss != null)
            {
                if (_mss.Playback != null)
                {
                    _mss.Playback.PlaybackBitrateChanged -= Async_PlaybackBitrateChanged;
                }
                _mss = null;
            }

            if (MediaElement != null)
            {
                MediaElement.MediaOpened -= MediaElement_MediaOpened;
                MediaElement.MediaFailed -= MediaElement_MediaFailed;
                MediaElement.MediaEnded -= MediaElement_MediaEnded;
                MediaElement.CurrentStateChanged -= MediaElement_CurrentStateChanged;
#if !WINDOWS_PHONE
                MediaElement.MarkerReached -= MediaElement_MarkerReached;
#endif
                MediaElement.BufferingProgressChanged -= MediaElement_BufferingProgressChanged;
                MediaElement.DownloadProgressChanged -= MediaElement_DownloadProgressChanged;
                MediaElement.LogReady -= MediaElement_LogReady;

                if (_mss != null && _mss.Playback != null)
                {
                    _mss.Playback.PlaybackBitrateChanged -= Async_PlaybackBitrateChanged;
                    _mss.Playback.MediaFileChanged -= Playback_MediaFileChanged_Async;
                    _mss.Playback.VariantSelector = null;

                    _mss = null;
                }

                MediaElement.Source = null;
                MediaElement = null;
            }
        }

        private void Playback_MediaFileChanged_Async(object sender, Uri uri)
        {
            MediaElement.Dispatcher.BeginInvoke(new Action<object, Uri>(Playback_MediaFileChanged), sender, uri);
        }

        /// <summary>
        /// Updates chunk URL display
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="uri"></param>
        private void Playback_MediaFileChanged(object sender, Uri uri)
        { }

        private void MediaElement_DownloadProgressChanged(object sender, RoutedEventArgs e)
        {
            DownloadProgressChanged.IfNotNull(i => i(this, MediaElement.DownloadProgress));
        }

        private void MediaElement_BufferingProgressChanged(object sender, RoutedEventArgs e)
        {
            BufferingProgressChanged.IfNotNull(i => i(this, MediaElement.BufferingProgress));
        }

        private void MediaElement_MarkerReached(object sender, TimelineMarkerRoutedEventArgs e)
        {
            string logMessage = string.Format(HLSMediaPluginResources.TimelineMarkerReached, e.Marker.Time,
                                              e.Marker.Type, e.Marker.Text);
            SendLogEntry(KnownLogEntryTypes.MediaElementMarkerReached, message: logMessage);

            var mediaMarker = new MediaMarker
            {
                Type = e.Marker.Type,
                Begin = e.Marker.Time,
                End = e.Marker.Time,
                Content = e.Marker.Text
            };

            NotifyMarkerReached(mediaMarker);
        }

        private void MediaElement_CurrentStateChanged(object sender, RoutedEventArgs e)
        {
            MediaPluginState playState = ConvertToPlayState(MediaElement.CurrentState);
            CurrentStateChanged.IfNotNull(i => i(this, playState));
        }

        private void MediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            MediaEnded.IfNotNull(i => i(this));
        }

        private void MediaElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            MediaFailed.IfNotNull(i => i(this, e.ErrorException));
        }

        private void MediaElement_MediaOpened(object sender, RoutedEventArgs e)
        {
            MediaOpened.IfNotNull(i => i(this));
            if (IsSourceLive)
            {
                StartPositionOverride = TimeSpan.FromSeconds(0);
                CanSeekOverride = true;
                PositionOverride = EndPositionOverride = DurationOverride = LivePositionOverride = LiveDvrDuration - TimeSpan.FromSeconds(30);                
                
            }         
        }
              
        private void MediaElement_LogReady(object sender, LogReadyRoutedEventArgs e)
        {
            string message = string.Format(HLSMediaPluginResources.MediaElementGeneratedLogMessageFormat,
                                           e.LogSource);
            var extendedProperties = new Dictionary<string, object> { { "Log", e.Log } };
            SendLogEntry(KnownLogEntryTypes.MediaElementLogReady, LogLevel.Statistics, message, extendedProperties: extendedProperties);
        }

        private void NotifyMarkerReached(MediaMarker mediaMarker)
        {
            MarkerReached.IfNotNull(i => i(this, mediaMarker));
        }

        private void SendLogEntry(string type, LogLevel severity = LogLevel.Information,
                                  string message = null,
                                  DateTime? timeStamp = null,
                                  IEnumerable<KeyValuePair<string, object>> extendedProperties = null)
        {
            if (LogReady != null)
            {
                var logEntry = new LogEntry
                {
                    Type = type,
                    Severity = severity,
                    Message = message,
                    SenderName = PluginName,
                    Timestamp = timeStamp.HasValue ? timeStamp.Value : DateTime.Now
                };

                extendedProperties.ForEach(logEntry.ExtendedProperties.Add);
                LogReady(this, logEntry);
            }
        }

        private static MediaPluginState ConvertToPlayState(MediaElementState mediaElementState)
        {
            return (MediaPluginState)Enum.Parse(typeof(MediaPluginState), mediaElementState.ToString(), true);
        }

        #region Live Dvr

        //lrj add
        public bool IsLive
        {
            get
            {
                if (IsDvrLive)
                    return false;
                else
                {
                    if (_mss == null || _mss.Playback == null)
                        return false;

                    return !_mss.Playback.IsEndList;
                }
            }
        }   
        public bool IsDvrLive
        {
            get
            {
                bool _islive = false;
                if (_mss == null || _mss.Playback == null)
                    return false;
                else
                    _islive = !_mss.Playback.IsEndList;

                HLSPlaylistImpl playlist = _mss.Playback.Metadata as HLSPlaylistImpl;
                if (_islive && playlist.PlaylistDuration > _liveDvrMinDuration)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
        //lrj add
        public TimeSpan LiveDvrDuration
        {
            get
            {
                HLSPlaylistImpl playlist = _mss.Playback.Metadata as HLSPlaylistImpl;                
                return playlist.PlaylistDuration;
            }
        }

        public bool IsLivePosition
        {
            get { return IsLive; }
        }

        public bool IsSourceLive
        {
            get { return IsDvrLive; }
        }

        public event Action<ILiveDvrMediaPlugin> LiveEventCompleted;
        private void MediaElement_LiveEventCompleted(object sender, EventArgs e)
        {
            LiveEventCompleted.IfNotNull(i => i(this));
        }
        private LivePlaybackStartPosition _liveplaybackstartposition = LivePlaybackStartPosition.PausedPosition;
        public LivePlaybackStartPosition LivePlaybackStartPosition
        {
            get
            {
                return _liveplaybackstartposition;
            }
            set
            {
                _liveplaybackstartposition = value;
            }
        }
        public TimeSpan? LivePositionOverride { get; set; }       
        public TimeSpan LivePosition
        {
            get {
                if (LivePositionOverride != null)
                    return (TimeSpan)LivePositionOverride;
                else
                {                   
                    return TimeSpan.FromSeconds(0);
                }
            }
            set {
                LivePositionOverride = value;
            }
        }
        
        
        #endregion

        
    }
}
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Xbox.Controls;
using Microsoft.Web.Media.SmoothStreaming;

namespace Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID
{
    public class AdaptiveMediaElementAdapter : MediaElementAdapterBase
    {
        SmoothStreamingMediaElement MediaPlugin;

        public AdaptiveMediaElementAdapter(SmoothStreamingMediaElement MediaPlugin, MediaTransport MediaTransport)
            : base(MediaTransport)
        {
            this.MediaPlugin = MediaPlugin;
        }

        public override void LoadAd()
        {
            base.LoadAd();
            MediaTransport.SignalStrengthMode = SignalStrengthMode.None;
        }

        public override void StartAd(TimeSpan Duration) 
        {
            RegisterMediaEvents();
            base.StartAd(Duration);
        }

        private void RegisterMediaEvents()
        {
            MediaPlugin.VideoHighestPlayableTrackChanged += MediaPlugin_VideoHighestPlayableTrackChanged;
            MediaPlugin.PlaybackTrackChanged += MediaPlugin_PlaybackTrackChanged;
            MediaPlugin.MediaEnded += OnMediaEnded;
            MediaPlugin.MediaFailed += OnMediaFailed;
            MediaPlugin.MediaOpened += OnMediaOpened;
            MediaPlugin.CurrentStateChanged += MediaPlugin_CurrentStateChanged;
            MediaPlugin.BufferingProgressChanged += MediaPlugin_BufferingProgressChanged;
            MediaPlugin.DownloadProgressChanged += MediaPlugin_DownloadProgressChanged;
            MediaPlugin.SeekCompleted += MediaPlugin_SeekCompleted;
        }

        public override void UnloadAd()
        {
            UnregisterMediaEvents();
            base.UnloadAd();
        }

        private void UnregisterMediaEvents()
        {
            MediaPlugin.VideoHighestPlayableTrackChanged -= MediaPlugin_VideoHighestPlayableTrackChanged;
            MediaPlugin.PlaybackTrackChanged -= MediaPlugin_PlaybackTrackChanged;
            MediaPlugin.MediaEnded -= OnMediaEnded;
            MediaPlugin.MediaFailed -= OnMediaFailed;
            MediaPlugin.MediaOpened -= OnMediaOpened;
            MediaPlugin.CurrentStateChanged -= MediaPlugin_CurrentStateChanged;
            MediaPlugin.BufferingProgressChanged -= MediaPlugin_BufferingProgressChanged;
            MediaPlugin.DownloadProgressChanged -= MediaPlugin_DownloadProgressChanged;
            MediaPlugin.SeekCompleted -= MediaPlugin_SeekCompleted;
        }

        void MediaPlugin_PlaybackTrackChanged(object sender, TrackChangedEventArgs e)
        {
            UpdateBitrateGraph();
        }

        void MediaPlugin_VideoHighestPlayableTrackChanged(object sender, TrackChangedEventArgs e)
        {
            UpdateBitrateGraph();
        }

        void UpdateBitrateGraph()
        {
            MediaTransport.SignalStrengthMode = GetSignalStrength();
        }

        protected virtual SignalStrengthMode GetSignalStrength()
        {
            if (MediaPlugin.VideoPlaybackTrack != null && MediaPlugin.VideoHighestPlayableTrack != null)
            {
                var PlaybackBitrate = MediaPlugin.VideoPlaybackTrack.Bitrate;
                var MaximumPlaybackBitrate = MediaPlugin.VideoHighestPlayableTrack.Bitrate;

                double percentage = MaximumPlaybackBitrate != 0
                                        ? (double)PlaybackBitrate / MaximumPlaybackBitrate
                                        : 0;

                SignalStrengthMode state =
                        percentage < .2
                              ? SignalStrengthMode.None
                              : percentage < .3
                                    ? SignalStrengthMode.Low
                                    : percentage < .5
                                          ? SignalStrengthMode.Medium
                                          : percentage < .75
                                                ? SignalStrengthMode.High
                                                : SignalStrengthMode.Full;

                return state;
            }
            else
            {
                return SignalStrengthMode.None;
            }
        }

        private void MediaPlugin_SeekCompleted(object sender, SeekCompletedEventArgs e)
        {
            base.IsSeeking = false;
        }

        void MediaPlugin_DownloadProgressChanged(object sender, RoutedEventArgs eventArgs)
        {
            OnDownloadProgressChanged();
        }

        void MediaPlugin_BufferingProgressChanged(object sender, RoutedEventArgs eventArgs)
        {
            OnBufferingProgressChanged();
        }

        void MediaPlugin_CurrentStateChanged(object sender, RoutedEventArgs eventArgs)
        {
            OnCurrentStateChanged();
        }

        protected override TimeSpan PositionCore
        {
            get { return MediaPlugin.Position; }
        }

        protected override void SeekCore(TimeSpan position)
        {
            MediaPlugin.Position = position;
        }

        protected override void PlayCore()
        {
            MediaPlugin.Play();
        }

        protected override void PauseCore()
        {
            MediaPlugin.Pause();
        }

        protected override void StopCore()
        {
            MediaPlugin.Stop();
        }

        protected override MediaElementAdapterState CurrentStateCore
        {
            get { return ConvertToState(MediaPlugin.CurrentState); }
        }

        protected override void DisposeCore()
        {
            UnregisterMediaEvents();
            MediaPlugin = null;
        }

        protected override double BufferingProgressCore
        {
            get { return MediaPlugin.BufferingProgress; }
        }

        protected override double DownloadOffsetCore
        {
            get { return MediaPlugin.DownloadProgressOffset; }
        }

        protected override double DownloadProgressCore
        {
            get { return MediaPlugin.DownloadProgress; }
        }

        protected override void SetStretchCore(Stretch stretch)
        {
            MediaPlugin.Stretch = stretch;
        }

        protected override bool CanPauseCore
        {
            get { return MediaPlugin.CanPause; }
        }

        protected override bool CanSeekCore
        {
            get { return MediaPlugin.CanSeek; }
        }

        MediaElementAdapterState ConvertToState(SmoothStreamingMediaElementState state)
        {
            switch (state)
            {
                case SmoothStreamingMediaElementState.AcquiringLicense:
                    return MediaElementAdapterState.Busy;
                case SmoothStreamingMediaElementState.Buffering:
                    return MediaElementAdapterState.Busy;
                case SmoothStreamingMediaElementState.Closed:
                    return MediaElementAdapterState.Closed;
                case SmoothStreamingMediaElementState.Individualizing:
                    return MediaElementAdapterState.Busy;
                case SmoothStreamingMediaElementState.Opening:
                    return MediaElementAdapterState.Busy;
                case SmoothStreamingMediaElementState.Paused:
                    return MediaElementAdapterState.Paused;
                case SmoothStreamingMediaElementState.Playing:
                    return MediaElementAdapterState.Playing;
                case SmoothStreamingMediaElementState.Stopped:
                    return MediaElementAdapterState.Stopped;
                case SmoothStreamingMediaElementState.ClipPlaying:
                    return MediaElementAdapterState.Playing;
                default:
                    throw new NotImplementedException();
            }
        }
    }
}

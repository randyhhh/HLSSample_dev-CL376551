using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Xbox.Controls;

namespace Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID
{
    public class ProgressiveMediaElementAdapter : MediaElementAdapterBase
    {
        MediaElement MediaPlugin;

        public ProgressiveMediaElementAdapter(MediaElement MediaPlugin, MediaTransport MediaTransport)
            : base(MediaTransport)
        {
            this.MediaPlugin = MediaPlugin;
        }

        public override void StartAd(TimeSpan Duration)
        {
            RegisterMediaEvents();
            base.StartAd(Duration);
        }

        public override void LoadAd()
        {
            base.LoadAd();
            MediaTransport.SignalStrengthMode = SignalStrengthMode.None;
        }

        private void RegisterMediaEvents()
        {
            MediaPlugin.MediaEnded += OnMediaEnded;
            MediaPlugin.MediaFailed += OnMediaFailed;
            MediaPlugin.MediaOpened += OnMediaOpened;
            MediaPlugin.CurrentStateChanged += MediaPlugin_CurrentStateChanged;
            MediaPlugin.BufferingProgressChanged += MediaPlugin_BufferingProgressChanged;
            MediaPlugin.DownloadProgressChanged += MediaPlugin_DownloadProgressChanged;
        }

        public override void UnloadAd()
        {
            UnregisterMediaEvents();
            base.UnloadAd();
        }

        private void UnregisterMediaEvents()
        {
            MediaPlugin.MediaEnded -= OnMediaEnded;
            MediaPlugin.MediaFailed -= OnMediaFailed;
            MediaPlugin.MediaOpened -= OnMediaOpened;
            MediaPlugin.CurrentStateChanged -= MediaPlugin_CurrentStateChanged;
            MediaPlugin.BufferingProgressChanged -= MediaPlugin_BufferingProgressChanged;
            MediaPlugin.DownloadProgressChanged -= MediaPlugin_DownloadProgressChanged;
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

        MediaElementAdapterState ConvertToState(MediaElementState state)
        {
            switch (state)
            {
                case MediaElementState.AcquiringLicense:
                    return MediaElementAdapterState.Busy;
                case MediaElementState.Buffering:
                    return MediaElementAdapterState.Busy;
                case MediaElementState.Closed:
                    return MediaElementAdapterState.Closed;
                case MediaElementState.Individualizing:
                    return MediaElementAdapterState.Busy;
                case MediaElementState.Opening:
                    return MediaElementAdapterState.Busy;
                case MediaElementState.Paused:
                    return MediaElementAdapterState.Paused;
                case MediaElementState.Playing:
                    return MediaElementAdapterState.Playing;
                case MediaElementState.Stopped:
                    return MediaElementAdapterState.Stopped;
                default:
                    throw new NotImplementedException();
            }
        }
    }
}

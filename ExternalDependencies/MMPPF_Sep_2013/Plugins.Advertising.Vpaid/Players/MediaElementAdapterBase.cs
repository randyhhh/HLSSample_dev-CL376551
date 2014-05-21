using System;
using System.Windows;
using Microsoft.Xbox.Controls;
using System.Windows.Media;

namespace Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID
{
    public abstract class MediaElementAdapterBase : IMediaElementAdapter
    {
        bool disposed;
        protected MediaTransport MediaTransport { get; private set; }

        public MediaElementAdapterBase(MediaTransport MediaTransport)
        {
            this.MediaTransport = MediaTransport;
        }

        object formerMediaElement;
        bool wasDisplayModeEnabled;
        bool wasThumbnailEnabled;
        bool isLoaded = false;
        SignalStrengthMode formerSignalStrength;

        public virtual void LoadAd()
        {
            wasDisplayModeEnabled = MediaTransport.IsDisplayModeEnabled;
            wasThumbnailEnabled = MediaTransport.IsThumbnailEnabled;
            formerMediaElement = MediaTransport.MediaElement;
            formerSignalStrength = MediaTransport.SignalStrengthMode;

            MediaTransport.IsFastForwardEnabled = false;
            MediaTransport.IsSkipForwardEnabled = false;
            MediaTransport.IsDisplayModeEnabled = false;
            MediaTransport.IsThumbnailEnabled = false;
            MediaTransport.MediaElement = this;

            isLoaded = true;
        }

        public virtual void StartAd(TimeSpan Duration)
        {
            this.Duration = Duration;
        }

        public virtual void UnloadAd()
        {
            if (isLoaded)
            {
                MediaTransport.SignalStrengthMode = formerSignalStrength;
                MediaTransport.IsFastForwardEnabled = true;
                MediaTransport.IsSkipForwardEnabled = true;
                MediaTransport.IsDisplayModeEnabled = wasDisplayModeEnabled;
                MediaTransport.IsThumbnailEnabled = wasThumbnailEnabled;
                MediaTransport.MediaElement = formerMediaElement;
                isLoaded = false;
            }
        }

        protected void OnDownloadProgressChanged()
        {
            if (DownloadProgressChanged != null)
                DownloadProgressChanged(this, EventArgs.Empty);
        }

        protected void OnBufferingProgressChanged()
        {
            if (BufferingProgressChanged != null)
                BufferingProgressChanged(this, EventArgs.Empty);
        }

        protected void OnCurrentStateChanged()
        {
            if (!isSeeking)
            {
                InvokeCurrentStateChanged();
            }
        }

        private void InvokeCurrentStateChanged()
        {
            if (CurrentStateChanged != null)
                CurrentStateChanged(this, EventArgs.Empty);
        }

        public event EventHandler Pause;
        void IMediaElementAdapter.Pause()
        {
            if (!isSeeking && !isSeekPending)
            {
                if (Pause != null)
                    Pause(this, EventArgs.Empty);
                else
                    PauseCore();
            }
        }

        protected abstract void PauseCore();

        public event EventHandler Play;
        void IMediaElementAdapter.Play()
        {
            if (isSeekPending)
            {
                ExecutePendingSeek();
            }
            if (Play != null)
                Play(this, EventArgs.Empty);
            else
                PlayCore();
        }

        protected abstract void PlayCore();

        TimeSpan IMediaElementAdapter.Position
        {
            get
            {
                CheckForSeekCompleted(PositionCore);
                if (!isSeekPending && !isSeeking)
                {
                    return PositionCore;
                }
                return seekingPosition;
            }
        }

        protected abstract TimeSpan PositionCore { get; }

        void IMediaElementAdapter.Seek(TimeSpan position)
        {
            SeekCore(position);

            if (CurrentStateCore == MediaElementAdapterState.Playing)
            {
                BeginSeek(position);
            }
            else
            {
                SeekPending(position);
            }
        }

        protected abstract MediaElementAdapterState CurrentStateCore { get; }

        protected abstract void SeekCore(TimeSpan position);

        void IMediaElementAdapter.Stop()
        {
            CompleteSeek();
            StopCore();
        }

        protected abstract void StopCore();

        void IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    MediaTransport = null;
                    DisposeCore();
                }
                disposed = true;
            }
        }

        protected abstract void DisposeCore();

        double IMediaElementAdapter.BufferingProgress
        {
            get { return BufferingProgressCore; }
        }

        protected abstract double BufferingProgressCore { get; }

        void IMediaElementAdapter.SeekToLive()
        {
            throw new NotImplementedException();
        }

        MediaElementAdapterState IMediaElementAdapter.CurrentState
        {
            get
            {
                if (isSeeking)
                {
                    return MediaElementAdapterState.Busy;
                }
                return CurrentStateCore;
            }
        }

        double IMediaElementAdapter.DownloadOffset
        {
            get { return DownloadOffsetCore; }
        }

        protected abstract double DownloadOffsetCore { get; }

        double IMediaElementAdapter.DownloadProgress
        {
            get { return DownloadProgressCore; }
        }

        protected abstract double DownloadProgressCore { get; }

        public TimeSpan Duration { get; private set; }

        public event EventHandler BufferingProgressChanged;

        public event EventHandler CurrentStateChanged;

        public event EventHandler DownloadProgressChanged;

        TimeSpan IMediaElementAdapter.MaxPosition
        {
            get
            {
                return ((IMediaElementAdapter)this).Position;
            }
        }

        void IMediaElementAdapter.SetStretch(Stretch stretch)
        {
            SetStretchCore(stretch);
        }

        protected abstract void SetStretchCore(Stretch stretch);

        TimeSpan IMediaElementAdapter.StartPosition { get { return TimeSpan.Zero; } }

        bool IMediaElementAdapter.CanPause { get { return CanPauseCore; } }

        protected abstract bool CanPauseCore { get; }

        bool IMediaElementAdapter.CanSeek { get { return CanSeekCore; } }

        protected abstract bool CanSeekCore { get; }

        bool IMediaElementAdapter.IsLive { get { return false; } }

        TimeSpan IMediaElementAdapter.MinPosition { get { return TimeSpan.Zero; } }

        private bool isSeeking;
        private bool isSeekPending;
        private TimeSpan seekingPosition;

        private void SeekPending(TimeSpan position)
        {
            seekingPosition = position;
            isSeekPending = true;
        }

        private void BeginSeek(TimeSpan position)
        {
            seekingPosition = position;
            isSeekPending = false;
            IsSeeking = true;
        }

        private void CheckForSeekCompleted(TimeSpan position)
        {
            if (isSeeking && (position > seekingPosition))
            {
                CompleteSeek();
            }
        }

        private void CompleteSeek()
        {
            isSeekPending = false;
            IsSeeking = false;
        }

        private void ExecutePendingSeek()
        {
            BeginSeek(seekingPosition);
        }

        protected void OnMediaEnded(object sender, RoutedEventArgs e)
        {
            IsSeeking = false;
        }

        protected void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            IsSeeking = false;
        }

        protected void OnMediaOpened(object sender, RoutedEventArgs e)
        {
            IsSeeking = false;
        }

        protected bool IsSeeking
        {
            get
            {
                return isSeeking;
            }
            set
            {
                if (isSeeking != value)
                {
                    isSeeking = value;
                    InvokeCurrentStateChanged();
                }
            }
        }
    }
}

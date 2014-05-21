using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Xbox.Controls;
using System.Diagnostics;
using Silverlight.Samples.HttpLiveStreaming;
using System.Windows.Media;
using Microsoft.SilverlightMediaFramework.Utilities.Extensions;
namespace Silverlight.Samples.HLS
{
    public partial class HLSMediaElementControl : UserControl, IMediaElementAdapter
    {
        HLSMediaStreamSource _mss;
        public HLSMediaStreamSource MediaStreamSource { get { return _mss; } }
        public HLSMediaStreamSourceOpenParam OpenParam
        {
            get { return _mss == null ? null : _mss.OpenParam; }
            set
            {
                if (value != null)
                {
                    _mss = new HLSMediaStreamSource(value);
                    _mss.BufferLength = TimeSpan.FromSeconds(30);
                    _mss.Playback.VariantSelector = new VariantSelector(BitrateCommand.Auto);  
                    MediaElement.SetSource(_mss);
                }
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
                    // Create media stream source
                    _mss = new HLSMediaStreamSource(new HLSMediaStreamSourceOpenParam() { uri = value });
                    // Optional initialization
                    _mss.BufferLength = TimeSpan.FromSeconds(30);
                    _mss.Playback.VariantSelector = new VariantSelector(BitrateCommand.Auto);
                    // Start playback
                    MediaElement.SetSource(_mss);
                }
            }
        }
        public HLSMediaElementControl()
        {
           
            InitializeComponent();
            RegisterMediaElementEvents();
        }
        public MediaElement MediaElement { get { return _mediaElement; } }
        [Conditional("DEBUG")]
        protected void DebugTrace(string text, params object[] args)
        {
            HLSTrace.WriteLine(text, args);
        }
        ~HLSMediaElementControl()
        {
            Dispose(false);
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called.
            if (!_disposed)
            {
                DebugTrace("[Dispose]");

                // If disposing equals true, dispose all managed
                // and unmanaged resources.
                if (disposing)
                {
                    UnregisterMediaElementEvents();
                    DisposeCore();
                }

                // Note disposing has been done.
                _disposed = true;
            }
        }

        protected  void DisposeCore() {
            if (_mss != null)
            {
                _mss.Dispose();
            }
        }

        void RegisterMediaElementEvents()
        {
            DebugTrace("[RegisterMediaElementEvents]");
            _mediaElement.CurrentStateChanged += OnCurrentStateChanged;
            _mediaElement.BufferingProgressChanged += OnBufferingProgressChanged;
            _mediaElement.DownloadProgressChanged += OnDownloadProgressChanged;
            _mediaElement.MediaEnded += OnMediaEnded;
            _mediaElement.MediaFailed += OnMediaFailed;
            _mediaElement.MediaOpened += OnMediaOpened;
        }
        void UnregisterMediaElementEvents()
        {
            DebugTrace("[UnregisterMediaElementEvents]");
            _mediaElement.CurrentStateChanged -= OnCurrentStateChanged;
            _mediaElement.BufferingProgressChanged -= OnBufferingProgressChanged;
            _mediaElement.DownloadProgressChanged -= OnDownloadProgressChanged;
            _mediaElement.MediaEnded -= OnMediaEnded;
            _mediaElement.MediaFailed -= OnMediaFailed;
            _mediaElement.MediaOpened -= OnMediaOpened;
        }
        void VerifyNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
        /// <summary>
        /// Instructs the media to pause.
        /// </summary>
        public void Pause()
        {
            DebugTrace("[Pause] - Position = {0}", Position);
            VerifyNotDisposed();
            PauseCore();
        }

        public bool? CanPauseOverride { get; set; }
        public virtual bool CanPause { get { return CanPauseOverride != null ? (bool)CanPauseOverride : _mediaElement.CanPause; } }
        protected virtual void PauseCore() { _mediaElement.Pause(); }

        /// <summary>
        /// Instructs the media to play.
        /// </summary>
        public void Play()
        {
            DebugTrace("[Play] - Position = {0}", Position);
            VerifyNotDisposed();

            // simulate a seek because a previous seek may not be finished
            if (_isSeekPending)
            {
                ExecutePendingSeek();
            }

            PlayCore();
        }

        protected virtual void PlayCore() { _mediaElement.Play(); }

        /// <summary>
        /// Instructs the media to stop.
        /// </summary>
        public void Stop()
        {
            DebugTrace("[Stop] - Position = {0}", Position);
            VerifyNotDisposed();
            CompleteSeek();
            _forceStop = true;
            StopCore();
        }

        protected void StopCore() { _mediaElement.Stop(); }

        /// <summary>
        /// Instructs the media to seek.
        /// </summary>
        public void Seek(TimeSpan position)
        {
            DebugTrace("[Seek] - Position = {0}", position);
            VerifyNotDisposed();

            var oldPosition = PositionCore; // get the raw ME position before the seek.

            SeekCore(position);

            // Only seek now if you're in the playing state.
            // While in Paused (or similar) the seek won't happen until we play again.
            if (CurrentStateCore == MediaElementAdapterState.Playing)
            {
                BeginSeek(oldPosition, position);
            }
            else
            {
                SeekPending(oldPosition, position);
            }
        }

        public bool? CanSeekOverride { get; set; }
        public virtual bool CanSeek { get { return CanSeekOverride != null ? (bool)CanSeekOverride : _mediaElement.CanSeek; } }
        protected virtual void SeekCore(TimeSpan position) { _mediaElement.Position = position; }

        public void CompleteSeek()
        {
            DebugTrace("[CompleteSeek]");
            _isSeekPending = false;
            IsSeeking = false;

            SeekCompleted.IfNotNull(i => i(null, new RoutedEventArgs()));
        }

        void BeginSeek(TimeSpan from, TimeSpan to)
        {
            _seekingFromPosition = from;
            _seekingToPosition = to;
            DebugTrace("[BeginSeek] - From={0} To = {1} ({2})", from, to, CurrentStateCore);
            _isSeekPending = false;
            IsSeeking = true;
        }

        void SeekPending(TimeSpan from, TimeSpan to)
        {
            DebugTrace("[SeekPending] - From={0} To = {1} ({2})", from, to, CurrentStateCore);
            _seekingFromPosition = from;
            _seekingToPosition = to;
            _isSeekPending = true;
        }

        void ExecutePendingSeek()
        {
            // Start seeking from the saved locations.
            // _seekingFromPosition & _seekingToPosition were stashed away in SeekPending
            // because ME behaves differently when you try to seek while Paused.
            BeginSeek(_seekingFromPosition, _seekingToPosition);
        }

        /// <summary>
        /// Instructs the media to seek to live.
        /// </summary>
        public void SeekToLive()
        {
            DebugTrace("[SeekToLive]");
            VerifyNotDisposed();

            var oldPosition = PositionCore; // get the raw ME position before the seek.

            var position = SeekToLiveCore();

            BeginSeek(oldPosition, position);
        }

        public bool? IsLiveOverride { get; set; }

        /// <summary>
        /// Returns true if the the media is playing a live stream.
        /// </summary>
        public bool IsLive { get { return IsLiveOverride != null ? (bool)IsLiveOverride : IsLiveCore; } }
        protected  bool IsLiveCore
        {
            get
            {
                if (_mss == null || _mss.Playback == null)
                    return false;

                return !_mss.Playback.IsEndList;
            }
        }

        public TimeSpan? LivePositionOverride { get; set; }
        protected virtual TimeSpan SeekToLiveCore()
        {
            // Don't do anything because ME doesn't know about live streams.
            return LivePositionOverride != null ? (TimeSpan)LivePositionOverride : TimeSpan.Zero;
        }

        /// <summary>
        /// Instructs the media to stretch using the given Stretch mode.
        /// </summary>
        public void SetStretch(Stretch stretch)
        {
            DebugTrace("[SetStretch] {0}", stretch);
            VerifyNotDisposed();

            SetStretchCore(stretch);
        }

        protected virtual void SetStretchCore(Stretch stretch) { _mediaElement.Stretch = stretch; }


        public MediaElementAdapterState? CurrentStateOverride { get; set; }

        /// <summary>
        /// Gets the current state of the media.
        /// </summary>
        public MediaElementAdapterState CurrentState
        {
            get
            {
                DebugTrace("[CurrentState]");
                VerifyNotDisposed();

                if (CurrentStateOverride != null)
                    return (MediaElementAdapterState)CurrentStateOverride;

                if (_isSeeking)
                {
                    DebugTrace("[Forcing CurrentState Busy]");
                    return MediaElementAdapterState.Busy;
                }

                return CurrentStateCore;
            }
        }

        protected virtual MediaElementAdapterState CurrentStateCore { get { return ConvertToMediaElementAdapterState(_mediaElement.CurrentState); } }

        protected MediaElementAdapterState ConvertToMediaElementAdapterState(MediaElementState currentState)
        {
            DebugTrace("[ConvertToMediaElementAdapterState] CurrentState={0}", currentState);
            switch (currentState)
            {
                case MediaElementState.Closed:
                    return MediaElementAdapterState.Closed;
                case MediaElementState.Opening:
                    return MediaElementAdapterState.Closed; // need to go closed while opening so we cancel scrubbing
                case MediaElementState.Buffering:
                    return MediaElementAdapterState.Busy;
                case MediaElementState.Playing:
                    return MediaElementAdapterState.Playing;
                case MediaElementState.Paused:
                    return MediaElementAdapterState.Paused;
                case MediaElementState.Stopped:
                    return MediaElementAdapterState.Stopped;
                case MediaElementState.Individualizing:
                    return MediaElementAdapterState.Busy;
                case MediaElementState.AcquiringLicense:
                    return MediaElementAdapterState.Busy;
                default:
                    throw new ArgumentOutOfRangeException("currentState");
            }
        }

        public TimeSpan? StartPositionOverride { get; set; }
        /// <summary>
        /// Returns the start position.  This is the time that corresponds to the left side of the timeline.
        /// </summary>
        public TimeSpan StartPosition
        {
            get
            {
                DebugTrace("[StartPosition]");
                VerifyNotDisposed();

                return StartPositionOverride != null ? (TimeSpan)StartPositionOverride : StartPositionCore;
            }
        }

        protected virtual TimeSpan StartPositionCore { get { return TimeSpan.Zero; } }

        public TimeSpan? DurationOverride { get; set; }

        /// <summary>
        /// Returns the duration of the media.
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                DebugTrace("[Duration]");
                VerifyNotDisposed();

                return DurationOverride != null ? (TimeSpan)DurationOverride : DurationCore;
            }
        }

        protected virtual TimeSpan DurationCore
        {
            get
            {
                if (IsLive)
                {
                    // Live streams don't typically have a duration, so we'll just call it max-min.
                    return MaxPositionCore - MinPositionCore;
                }

                var duration = _mediaElement.NaturalDuration;
                return duration.HasTimeSpan ? duration.TimeSpan : TimeSpan.Zero;
            }
        }


        public TimeSpan? PositionOverride { get; set; }

        /// <summary>
        /// Returns the current position of the media.
        /// </summary>
        public TimeSpan Position
        {
            get
            {
                VerifyNotDisposed();

                if (PositionOverride != null)
                    return (TimeSpan)PositionOverride;

                var currentState = CurrentStateCore;
                if (_forceStop)// yet another hack to work around ME bugs.  ME sometimes returns Position = zero BEFORE it goes into the stopped state.
                {
                    _forceStop = false;
                    DebugTrace("[Position] faking a stop for one frame because ME is broken {0}", _lastPosition);
                }
                else if (currentState == MediaElementAdapterState.Stopped || currentState == MediaElementAdapterState.Closed)
                {
                    // We don't update the last position if stopped/closed because it will report Zero
                    DebugTrace("[Position] using last position {0}", _lastPosition);
                }
                else
                {
                    // Ok actually ask the ME what the position is.
                    var position = PositionCore;
                    if (position != _lastPosition)
                    {
                        _lastPosition = position;
                        DebugTrace("[Position] {0}", position);
                    }
                }

                CheckForSeekCompleted(currentState, _lastPosition);

                if (_isSeekPending || IsSeeking)
                {
                    return ClampPosition(_seekingToPosition);
                }
                else
                {
                    return ClampPosition(_lastPosition);
                }
            }
        }

        TimeSpan ClampPosition(TimeSpan position)
        {
            var min = MinPositionCore;
            if (position < min)
            {
                return min;
            }

            var max = MaxPositionCore;
            if (position > max)
            {
                return max;
            }

            return position;
        }

        protected virtual TimeSpan PositionCore { get { return _mediaElement.Position; } }
        private void CheckForSeekCompleted(MediaElementAdapterState currentState, TimeSpan position)
        {
            DebugTrace("[CheckForSeekCompleted] From={0} To={1} Position={2} IsSeeking={3} CurrentState={4}",
                                                         _seekingFromPosition, _seekingToPosition, position, IsSeeking, currentState);

            // Early-out if we're not seeking.
            if (!IsSeeking)
                return;

            // Early-out if we're not playing because ME does whacky things with the position while in the paused/buffering state.
            // For example, if you are paused at 10s and you seek to 30s, ME will immediately set the Position to 30s.  Then when you play again
            // the Position will jump back to 10s then forward to 30s.  This will obviously break our seek completed heuristics below, so we must
            // avoid the situation or else the timeline will jump around.
            if (currentState != MediaElementAdapterState.Playing)
                return;

            var seekingForward = _seekingToPosition >= _seekingFromPosition;
            var midPoint = TimeSpan.FromTicks((_seekingToPosition.Ticks + _seekingFromPosition.Ticks) / 2);

            DebugTrace("[CheckForSeekCompleted] Mid={0} SeekingForward={1}", midPoint, seekingForward);

            // To complete a seek the position must be in the range of the seek.
            //  This prevents the case where ME is way behind, say position = 1sec, and due to previous seeks we consider the position 50sec, and we're seeking back to 30sec.
            //  Unfortunately, if you're seeking forward the Position may move beyond the seek range before we ever see it, so we can only check the lower half of the seek range.
            var rangeStart = seekingForward ? _seekingFromPosition : _seekingToPosition;
            var rangeEnd = seekingForward ? _seekingToPosition : _seekingFromPosition;
            var isSmallSeek = (rangeEnd - rangeStart).TotalSeconds < c_inaccurateSeekThreshold; // due to inaccurate seeks we don't trust anything less the two seconds.

            if ((position >= rangeStart && isSmallSeek) ||  // If it's a small seek then we don't check that the position moved in the right direction because we may miss the movement based on our polling frequency.
                 (seekingForward && position >= midPoint) ||
                 (!seekingForward && position <= midPoint)) // Did position move toward the seek destination?  This is to counter ME's async lag on seeking.
            {
                CompleteSeek();
            }
        }
        public TimeSpan? MinPositionOverride { get; set; }
        /// <summary>
        /// Returns the minimum position that the media can be seeked to.
        /// </summary>
        public TimeSpan MinPosition
        {
            get
            {
                DebugTrace("[MinPosition]");
                VerifyNotDisposed();

                return MinPositionOverride != null ? (TimeSpan)MinPositionOverride : MinPositionCore;
            }
        }

        protected virtual TimeSpan MinPositionCore { get { return StartPosition; } }

        public TimeSpan? MaxPositionOverride { get; set; }
        /// <summary>
        /// Returns the maximum position that the media can be seeked to.  In a live stream, this would be the live position.
        /// </summary>
        public TimeSpan MaxPosition
        {
            get
            {
                DebugTrace("[MaxPosition]");
                VerifyNotDisposed();

                return MaxPositionOverride != null ? (TimeSpan)MaxPositionOverride : MaxPositionCore;
            }
        }

        protected virtual TimeSpan MaxPositionCore
        {
            get
            {
                if (IsLive && LivePositionOverride != null)
                {
                    return (TimeSpan)LivePositionOverride;
                }

                var duration = _mediaElement.NaturalDuration;
                return duration.HasTimeSpan ? duration.TimeSpan : TimeSpan.Zero; ;
            }
        }

        public double? BufferingProgressOverride { get; set; }

        /// <summary>
        /// Gets the buffering progress.
        /// </summary>
        public double BufferingProgress
        {
            get
            {
                DebugTrace("[BufferingProgress]");
                VerifyNotDisposed();

                return BufferingProgressOverride != null ? (double)BufferingProgressOverride : BufferingProgressCore;
            }
        }

        protected virtual double BufferingProgressCore { get { return _mediaElement.BufferingProgress; } }

        public double? DownloadProgressOverride { get; set; }
        /// <summary>
        /// Gets the download progress.
        /// </summary>
        public double DownloadProgress
        {
            get
            {
                DebugTrace("[DownloadProgress]");
                VerifyNotDisposed();

                return DownloadProgressOverride != null ? (double)DownloadProgressOverride : DownloadProgressCore;
            }
        }

        protected virtual double DownloadProgressCore { get { return _mediaElement.DownloadProgress; } }

        public double? DownloadOffsetOverride { get; set; }

        /// <summary>
        /// Gets the offset where downloading began.
        /// </summary>
        public double DownloadOffset
        {
            get
            {
                DebugTrace("[DownloadOffset]");
                VerifyNotDisposed();

                return DownloadOffsetOverride != null ? (double)DownloadOffsetOverride : DownloadOffsetCore;
            }
        }

        protected virtual double DownloadOffsetCore { get { return _mediaElement.DownloadProgressOffset; } }

        protected void OnMediaEnded(object sender, RoutedEventArgs e)
        {
            DebugTrace("[OnMediaEnded]");
            ResetAdapterState();
            
                MediaEnded.IfNotNull(i=>i(sender, e));
        }

        protected void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            DebugTrace("[OnMediaFailed]");
            ResetAdapterState();
            MediaFailed.IfNotNull(i => i(sender, e));
        }

        protected void OnMediaOpened(object sender, RoutedEventArgs e)
        {
            DebugTrace("[OnMediaOpened]");
            ResetAdapterState();
            MediaOpened.IfNotNull(i => i(sender,e));
        }

        private void ResetAdapterState()
        {
            _isSeeking = false;
            _isSeekPending = false;
            _forceStop = false;
            _lastPosition = TimeSpan.Zero;
        }

        /// <summary>
        /// Raised when the media has changed state.
        /// </summary>
        public event EventHandler CurrentStateChanged;

        void InvokeCurrentStateChanged(EventArgs e)
        {
            DebugTrace("[InvokeCurrentStateChanged] IsSeeking={0} CurrentStateCore={1}", IsSeeking, CurrentStateCore);
            var handler = CurrentStateChanged;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected void OnCurrentStateChanged(object sender, RoutedEventArgs e)
        {
            DebugTrace("[OnCurrentStateChanged] IsSeeking={0} CurrentStateCore={1}", IsSeeking, CurrentStateCore);
            if (!IsSeeking)
            {
                InvokeCurrentStateChanged(EventArgs.Empty);
            }
        }

        /// <summary>
        /// Raised when the media has changed buffering progress.
        /// </summary>
        public event EventHandler BufferingProgressChanged;

        void InvokeBufferingProgressChanged(EventArgs e)
        {
            DebugTrace("[InvokeBufferingProgressChanged] {0}", BufferingProgress);
            EventHandler handler = BufferingProgressChanged;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected void OnBufferingProgressChanged(object sender, RoutedEventArgs e)
        {
            DebugTrace("[OnBufferingProgressChanged]");
            InvokeBufferingProgressChanged(EventArgs.Empty);
        }

        /// <summary>
        /// Raised when the media has changed download progress.
        /// </summary>
        public event EventHandler DownloadProgressChanged;

        void InvokeDownloadProgressChanged(EventArgs e)
        {
            DebugTrace("[InvokeDownloadProgressChanged] {0}", DownloadProgress);
            EventHandler handler = DownloadProgressChanged;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected void OnDownloadProgressChanged(object sender, RoutedEventArgs e)
        {
            DebugTrace("[OnDownloadProgressChanged]");
            InvokeDownloadProgressChanged(EventArgs.Empty);
        }

        protected bool IsSeeking
        {
            get { return _isSeeking; }
            set
            {
                DebugTrace("[IsSeeking] {0}", value);
                if (_isSeeking != value)
                {
                    _isSeeking = value;
                    InvokeCurrentStateChanged(EventArgs.Empty);
                }
            }
        }

        bool _isSeeking;
        bool _isSeekPending; // this is true when we've seeked while paused.  The seek won't fully go though until we start playing again.
        bool _disposed;
        bool _forceStop;
        TimeSpan _seekingToPosition;
        TimeSpan _seekingFromPosition;
        TimeSpan _lastPosition;
        const double c_inaccurateSeekThreshold = 2.0;


        #region Ad event
        public event RoutedEventHandler MediaEnded;
        public event EventHandler<ExceptionRoutedEventArgs> MediaFailed;
        public event RoutedEventHandler MediaOpened;
        public event RoutedEventHandler SeekCompleted;
        #endregion
        
        
    }
}

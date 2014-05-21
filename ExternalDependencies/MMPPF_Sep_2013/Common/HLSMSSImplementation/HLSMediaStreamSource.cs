using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Media;

namespace Silverlight.Samples.HttpLiveStreaming
{

    public class BandwidthHistory
    {
        /// <summary>
        /// Avg bandwidth in last playback session
        /// </summary>
        private static double _avgBandwidth = UnknownBandwidth;

        private double _initialBandwidth = UnknownBandwidth;

        /// <summary>
        /// Maximum number of past BWMeasurementItems this class will keep.
        /// </summary>
        private int _maxBWHistoryCount = 5;

        /// <summary>
        /// A list of past and current BWMeasurementItems. The maximum number of items 
        /// we keep track of is _maxBWHistoryCount. 
        /// </summary>
        private List<BWMeasurementItem> _BWMeasurmentList = new List<BWMeasurementItem>();

        /// <summary>
        /// Maximum time duration that may be spent for calculating each BWMeasurment item. 
        /// </summary>
        private static readonly TimeSpan _maximumMeasurementDuration = TimeSpan.FromSeconds(1);
        
        /// <summary>
        /// A constant returned by GetAverageBandwidth and GetBandwidth methods in cases 
        /// that no actual bandwidth measurement is available. 
        /// </summary>
        public static double UnknownBandwidth = -1.00;

        /// <summary>
        /// A hardcoded maximum bandwidth limit applied to our measurements, which is set to the 
        /// XBOX network card limit. In some special cases our BW measurements may be larger than 
        /// the actual physical limits of XBOX hardware, which we would limit them by this number. 
        /// </summary>
        private const double _maxBandwidth = 100000000.00;

        /// <summary>
        /// The accumulated number of bytes received so far that corresponds to the current measurement item. 
        /// </summary>
        private long _totalBytesReceived = 0;

        /// <summary>
        /// The accumulated duration that corresponds to the current measurement item. 
        /// </summary>
        private TimeSpan _totalDuration = TimeSpan.FromTicks(0);

        /// <summary>
        /// A bandwidth measurement item, which represents the bandwidth over a given period of time. 
        /// Each item corresponds to a maximum of maximumMeasurementDuration duration. 
        /// </summary>
        private class BWMeasurementItem
        {
            /// <summary>
            /// Public constructor
            /// </summary>
            public BWMeasurementItem(int bytes, TimeSpan duration)
            {
                _bytes = bytes;
                _duration = duration;
            }
            
            /// <summary>
            /// Returns true if current measurement is completed. 
            /// </summary>
            public bool IsCompleted()
            {
                if (_duration > _maximumMeasurementDuration)
                {
                    return true;
                }
                else if (_bytes > (_maxBandwidth * _maximumMeasurementDuration.TotalSeconds) / 8.00)
                {
                    return true;
                }
                return false;
            }

            /// <summary>
            /// Gets the bandwidth represented by this item in bps. 
            /// </summary>
            public double GetBandwidth()
            {
                if (_duration.TotalMilliseconds == 0)
                {
                    return UnknownBandwidth;
                }
                else
                {
                    return Math.Min(_maxBandwidth, (_bytes * 8.00) / Duration.TotalSeconds);
                };
            }

            /// <summary>
            /// Public accessor for the number of bytes for this measurement.
            /// </summary>
            public double NumberOfBytes
            {
                get
                {
                    return _bytes;
                }
                set
                {
                    _bytes = value;
                }
            }
        
            /// <summary>
            /// Public accessor for the duration of this measurement.
            /// </summary>
            public TimeSpan Duration
            {
                get
                {
                    return _duration;
                }
                set
                {
                    _duration = value;
                }
            }

            /// <summary>
            /// The number of bytes for this measurement.
            /// </summary>
            private double _bytes = 0;

            /// <summary>
            /// The duration of this measurement 
            /// </summary>
            private TimeSpan _duration = TimeSpan.FromTicks(0);

        }

        public BandwidthHistory(double initialBandwidth)
        {
            _initialBandwidth = initialBandwidth;
        }

        /// <summary>
        /// Incorporates a new pair of byte and duration readings to the bandwidth measurment
        /// entries.
        /// </summary>
        /// <param name="bytes">how many bytes has been downloaded</param>
        /// <param name="duration">the duration for downloading the data in MSec</param>
        /// <param name="isEndOfSegment">flag indicating that this data is from last portion of a .TS segmnet download</param
        public void AddMeasurement(int bytes, TimeSpan duration)
        {
            lock (_BWMeasurmentList)
            {
                _totalBytesReceived += (long)bytes;
                _totalDuration += duration;

                if (_BWMeasurmentList.Count == 0)
                {
                    _BWMeasurmentList.Add(new BWMeasurementItem(0, TimeSpan.Zero));
                }

                BWMeasurementItem lastItem = _BWMeasurmentList[_BWMeasurmentList.Count - 1];

                if (lastItem.IsCompleted())
                {
                    if (_BWMeasurmentList.Count >= _maxBWHistoryCount)
                    {
                        _BWMeasurmentList.RemoveAt(0);
                    }

                    // add a new entry
                    _BWMeasurmentList.Add(new BWMeasurementItem(0, TimeSpan.Zero));
                    lastItem = _BWMeasurmentList[_BWMeasurmentList.Count - 1];
                }

                lastItem.Duration += duration;
                lastItem.NumberOfBytes += bytes;
           }
        }

        /// <summary>
        /// Public accessor for the duration of this measurement.
        /// </summary>
        public int MaxHistoryCount
        {
            get
            {
                lock (_BWMeasurmentList)
                {
                    return _maxBWHistoryCount;
                }
            }
            set
            {
                lock (_BWMeasurmentList)
                {
                    _maxBWHistoryCount = value;
                }
            }
        }

        /// <summary>
        /// Returns the average bandwidth in the history list
        /// </summary>
        /// <returns>Average Bandwidth in bits/sec</returns>
        public double GetAverageBandwidth()
        {
            lock (_BWMeasurmentList)
            {
                if (_BWMeasurmentList.Count == 0)
                {
                    if (_initialBandwidth != UnknownBandwidth)
                    {
                        return _initialBandwidth;
                    }
                    else
                    {
                        return _avgBandwidth;
                    }
                }
                else
                {
                    double totalBytes = 0.00;
                    TimeSpan totalDuration = TimeSpan.FromTicks(0);
                    for (int i = 0; i < _BWMeasurmentList.Count; i++)
                    {
                        totalBytes += _BWMeasurmentList[i].NumberOfBytes;
                        totalDuration += _BWMeasurmentList[i].Duration;
                    }

                    if (totalDuration.TotalMilliseconds == 0)
                    {
                        return UnknownBandwidth;
                    }
                    else
                    {
                        return Math.Min(_maxBandwidth, (totalBytes * 8.00) / totalDuration.TotalSeconds);
                    }
                }
            }
        }

        /// <summary>
        /// Retuns the latest bandwidth in the history list
        /// </summary>
        /// <returns>Latest bandwidth in bits/sec</returns>
        public double GetLatestBandwidth()
        {
            lock (_BWMeasurmentList)
            {
                if (_BWMeasurmentList.Count == 0)
                {
                    if (_initialBandwidth != UnknownBandwidth)
                    {
                        return _initialBandwidth;
                    }
                    else
                    {
                        return _avgBandwidth;
                    }
                }
                else
                {
                    BWMeasurementItem lastItem = _BWMeasurmentList[_BWMeasurmentList.Count - 1];
                    if (lastItem.IsCompleted())
                    {
                        return lastItem.GetBandwidth();
                    }
                    else if (_BWMeasurmentList.Count == 1)
                    {
                        return UnknownBandwidth;
                    }
                    else
                    {
                        lastItem = _BWMeasurmentList[_BWMeasurmentList.Count - 2];
                        return lastItem.GetBandwidth();
                    }
                }
            }
        }

        /// <summary>
        /// Clears the history list
        /// </summary>
        private void Clear()
        {
            lock (_BWMeasurmentList)
            {
                _BWMeasurmentList.Clear();
            }
        }

        /// <summary>
        /// Close bandwidth history
        /// </summary>
        public void Close()
        {
            _avgBandwidth = GetAverageBandwidth();
            Clear();
        }
    }

    public class HLSMediaStreamSourceOpenParam
    {
        public Uri uri;
        public bool isDownloadAllSubPlayList = false;
        public uint minimalBitrate = 0;
        public uint maxBitrate = 0;
        public uint maxPicWidth = 0;
        public uint maxPicHeight = 0;
        public bool ifAlignBufferAfterSeek = false;
        public double initialBandwidth = BandwidthHistory.UnknownBandwidth;
        public TimeSpan startupBuffer = TimeSpan.FromSeconds(3.00);
        public class OptionalHeader
        {
            public string header;
            public string value;
            public OptionalHeader(string header, string value)
            {
                this.header = header;
                this.value = value;
            }
        }
        public List<OptionalHeader> optionalHeaderList;

        public HLSMediaStreamSourceOpenParam() { }

        public HLSMediaStreamSourceOpenParam(HLSMediaStreamSourceOpenParam openParam)
        {
            this.uri = openParam.uri;
            this.isDownloadAllSubPlayList = openParam.isDownloadAllSubPlayList;
            this.minimalBitrate = openParam.minimalBitrate;
            this.maxBitrate = openParam.maxBitrate;
            this.maxPicWidth = openParam.maxPicWidth;
            this.maxPicHeight = openParam.maxPicHeight;
            this.ifAlignBufferAfterSeek = openParam.ifAlignBufferAfterSeek;
            this.initialBandwidth = openParam.initialBandwidth;
            this.startupBuffer = openParam.startupBuffer;
            if (null != openParam.optionalHeaderList)
            {
                this.optionalHeaderList = new List<OptionalHeader>(openParam.optionalHeaderList);
            }
        }
    }

    public class HLSMediaStreamSource : MediaStreamSource, IVariantSelector, IDisposable
    {
        /// <summary>
        /// Playlist being played
        /// </summary>
        private HLSPlaylist _playlist;

        /// <summary>
        /// A program in playlist that's being played
        /// </summary>
        private HLSProgram _program;

        /// <summary>
        /// current program date time
        /// </summary>
        private ProgramDateTime _programDateTime = null;


        /// <summary>
        /// Playback context
        /// </summary>
        private HLSPlayback _playback;

        /// <summary>
        /// Buffer for storing audio samples
        /// </summary>
        private SampleBuffer _audioBuffer;

        /// <summary>
        /// Buffer for storing video samples
        /// </summary>
        private SampleBuffer _videoBuffer;

        /// <summary>
        /// Flag that indicates we are in buffering state, which means we are not 
        /// returning any samples until all buffers are reconstructed.
        /// </summary>
        private volatile bool _isBuffering;

        /// <summary>
        /// Contains last progress number that we reported in buffering mode.
        /// </summary>
        private double _lastBufferingProgressReported; 


        /// <summary>
        /// Target buffer length
        /// </summary>
        private TimeSpan _bufferLength;

        /// <summary>
        /// MPEG-TS demuxer
        /// </summary>
        private volatile TSDemux _demux;

        /// <summary>
        /// This flag keeps track of whether or not we have started the
        /// work queue thread.
        /// </summary>
        private bool _isWorkQueueThreadStarted;

        /// <summary>
        /// The current state of our stream
        /// </summary>
        private volatile State _state = State.None;

        /// <summary>
        /// The seek position requested by MediaElement via SeekAsync
        /// </summary> 
        private long _requestedSeekPosition;

        /// <summary>
        /// This is the queue of pending commands. These commands are run on a background
        /// thread so that we do not block the UI.
        /// </summary>
        private WorkQueue _workQueue;      
        /// <summary>
        /// This is the background thread that handles all of the commands in the work queue
        /// </summary>
        private Thread _workQueueThread;

        /// <summary>
        /// Provide an object with strong identity to lock the work queue thread call on
        /// </summary>
        private object _workQueueThreadLock = new object();

        /// <summary>
        /// lock to make  ReportPendingSamples threadsafe
        /// </summary>
        private object _reportPendingSamplesLock = new object();

        /// <summary>
        /// Flag used to show the target duration warning message only once
        /// </summary>
        private bool _targetDurationWarningShown = false; 

        /// <summary>
        /// Default target duration used by heuristics algorithm in cases that target duration tag
        /// is missing in the playlist. 
        /// </summary>
        private readonly TimeSpan _defaultTargetDuration = TimeSpan.FromSeconds(10);

        /// <summary>
        /// record bandwidth history 
        /// </summary>
        public BandwidthHistory BandwidthHistory
        {
            get
            {
                return _bwHistory;
            }
        }

        private BandwidthHistory _bwHistory = null;

        /// <summary>
        /// Track whether Dispose has been called
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// The total continuious count of TS Demux ReadChunk errors 
        /// </summary>
        private int _TSReadErrorCount = 0;


        /// <summary>
        /// The maximum number of continuious TS Demux ReadChunk errors before exiting the playback
        /// </summary>
        private const int _maxTSReadErrorCount = 10;

        /// <summary>
        /// Stream bitrate of the last sample reported to MediaElement. This is NOT the size of last 
        /// sample reported divided by its duration. This is actually the bitrate of HLS stream variant 
        /// that this sample has originated from, and is used to trigger the playback bitrate changed 
        /// event once we start generating video samples from a new bitrate variant. 
        /// </summary>
        /// <param name="diagnosticKind"></param>
        private long _lastSampleReportedBitrate = 0;

        private bool _playlistOverrided = false;

        /// <summary>
        /// This enumeration is used for keeping track of the state of our
        /// stream.
        /// </summary>
        private enum State
        {
            /// <summary>
            /// Stream is neither open nor closed
            /// </summary>
            None,

            /// <summary>
            /// Stream is in process of opening
            /// </summary>
            Opening,

            /// <summary>
            /// Stream is opened
            /// </summary>
            Opened,

            /// <summary>
            /// Seek is in progress 
            /// </summary>
            Seeking,

            /// <summary>
            /// Stream is closed
            /// </summary>
            Closed
        }
        private TimeSpan _liveDvrMinDuration = TimeSpan.FromSeconds(120);
        //public TimeSpan LiveDvrMinDuration {
        //    get { return _liveDvrMinDuration; }
        //    set { _liveDvrMinDuration = value; Playback.LiveDvrMinDuration = value; }
        //}
        private HLSMediaStreamSourceOpenParam _openParam;

        public HLSMediaStreamSourceOpenParam OpenParam
        {
            get
            {
                return _openParam;
            }
        }

        // Summary:
        //     Initializes a new instance of the System.Windows.Media.MediaStreamSource
        //     class.
        public HLSMediaStreamSource(HLSMediaStreamSourceOpenParam openParam)
        {
            _openParam = new HLSMediaStreamSourceOpenParam(openParam);
            _bwHistory = new HttpLiveStreaming.BandwidthHistory(openParam.initialBandwidth);
            _playlist = new HLSPlaylist(openParam.uri);
            _playlist.MSS = this;
            Construct();
        }

        /// <summary>
        /// Another form of constructor, accepts potentially preloaded playlist and program.
        /// </summary>
        /// <param name="playlist">Playlist to play, required.</param>
        /// <param name="program">Program to play, can be null.</param>
        /*public HLSMediaStreamSource(HLSPlaylist playlist, HLSProgram program)
        {
            if (playlist == null)
                throw new ArgumentNullException("playlist");

            _playlist = playlist;
            _program = program;
            Construct();
        }*/

        /// <summary>
        /// Internal common constructor
        /// </summary>
        private void Construct()
        {
            _workQueue = new WorkQueue();
            _workQueueThread = new Thread(WorkerThread);
            _workQueueThread.Name = "HLS MSS Worker Thread";
        }

        /// <summary>
        /// Accessor for playback object for user customization
        /// </summary>
        public HLSPlayback Playback
        {
            get
            {
                if (_playback == null)
                    _playback = new HLSPlayback(_program, (IVariantSelector)this);
                return _playback;
            }
        }

        /// <summary>
        /// Buffer length in milliseconds
        /// </summary>
        public TimeSpan BufferLength
        {
            get
            {
                return _bufferLength;
            }
            set
            {
                if (value.TotalMilliseconds < 500 || value.TotalMilliseconds > 60000)
                    throw new ArgumentOutOfRangeException("value");

                _bufferLength = value;
            }
        }

        /// <summary>
        /// Closes down the open media streams and otherwise cleans up the System.Windows.Media.MediaStreamSource.
        //  The System.Windows.Controls.MediaElement can call this method when going
        //  through normal shutdown or as a result of an error.
        /// </summary>
        protected override void CloseMedia()
        {
            try
            {
                HLSTrace.WriteLine(" CloseMedia is called");  

                // Mark our state as closed
                _state = State.Closed;

                // No need to respond to anything because we are shutting down
                if (_workQueue != null)
                {
                    _workQueue.ClearAndEnqueue(new WorkQueueElement(WorkQueueElement.Command.Close, null));
                }
            }
            catch (Exception e)
            {
                RaiseError(e);
                throw;
            }
        }

        /// <summary>
        /// Gathers the diagnostic information requested.
        /// </summary>
        /// <param name="diagnosticKind"></param>
        protected override void GetDiagnosticAsync(MediaStreamSourceDiagnosticKind diagnosticKind)
        {
            try
            {
                _workQueue.Enqueue(new WorkQueueElement(WorkQueueElement.Command.Diagnostics, diagnosticKind));
            }
            catch (Exception e)
            {
                RaiseError(e);
                throw;
            }
        }

        /// <summary>
        /// Causes the System.Windows.Media.MediaStreamSource to prepare a System.Windows.Media.MediaStreamSample
        /// describing the next media sample to be rendered by the media pipeline. This
        /// method can be responded to by both System.Windows.Media.MediaStreamSource.DeliverSample(System.Windows.Media.MediaStreamSample)
        /// and System.Windows.Media.MediaStreamSource.ReportGetSampleProgress(System.Double).
        /// </summary>
        /// <param name="mediaStreamType"></param>
        protected override void GetSampleAsync(MediaStreamType mediaStreamType)
        {
            _noSampleRequestYet = false;

            try
            {
                GetBuffer(mediaStreamType).OnSampleRequested();

                // deliver all pending sample requests
                ReportPendingSamples();

                // check if we need to enter buffering mode
                if (!_isBuffering)
                {
                    if (_audioBuffer.NeedSamples > 0 && !_audioBuffer.EndOfPlayback && !_audioBuffer.HasSamples )
                    {
                        _isBuffering = true;
                    }

                    if (_videoBuffer.NeedSamples > 0 && !_videoBuffer.EndOfPlayback && !_videoBuffer.HasSamples)
                    {
                        _isBuffering = true;
                    }

                    if (_isBuffering)
                    {
                        _lastBufferingProgressReported = 0;
                        HLSTrace.WriteLine("Entering buffering mode: BufferLevel = {0} MSec, _bufferLength = {1} MSec, Buffer fullness = %{2}", BufferLevel.TotalMilliseconds, _bufferLength.TotalMilliseconds, (100.00 * BufferLevel.TotalMilliseconds / _bufferLength.TotalMilliseconds).ToString("F"));
                        ReportGetSampleProgress(_lastBufferingProgressReported);
                    }
                }
            }
            catch (Exception e)
            {
                HLSTrace.PrintException(e);
                RaiseError(e);
                throw;
            }

        }

        /// <summary>
        /// Collects the metadata required to instantiate a collection of System.Windows.Media.MediaStreamDescription
        /// objects and then instantiates it.
        /// </summary>
        protected override void OpenMediaAsync()
        {
            try
            {
                // If called from multiple threads, make sure that we only do this one at a time
                lock (_workQueueThreadLock)
                {
                    // If we have not started the work queue thread, then start it now
                    if (!_isWorkQueueThreadStarted)
                    {
                        // Start our work queue thread to handle commands
                        _workQueueThread.Start();

                        // make sure we never start it twice
                        _isWorkQueueThreadStarted = true;
                    }
                }
                _workQueue.Enqueue(new WorkQueueElement(WorkQueueElement.Command.Open, null));
            }
            catch (Exception e)
            {
                RaiseError(e);
                throw;
            }
        }
        
        /// <summary>
        /// Takes the given sampleOffset and ensures that future calls to System.Windows.Media.MediaStreamSource.GetSampleAsync(System.Windows.Media.MediaStreamType)
        /// will be returned samples starting at that point.
        /// </summary>
        /// <param name="seekToTime"></param>
        protected override void SeekAsync(long seekToTime)
        {
            try
            {                
                HLSTrace.WriteLine("SeekAsync: seekToTime = {0}", seekToTime);
                _workQueue.Enqueue(new WorkQueueElement(WorkQueueElement.Command.Seek, seekToTime));
                
            }
            catch (Exception e)
            {
                RaiseError(e);
                throw;
            }

        }
        
        /// <summary>
        /// lrj add
        /// </summary>
        /// <param name="seekToTime"></param>
        //public void Seek(long seekToTime)
        //{
        //    try
        //    {
        //        HLSTrace.WriteLine("CusSeek: seekToTime = {0}", seekToTime);
        //        _workQueue.Enqueue(new WorkQueueElement(WorkQueueElement.Command.Seek, seekToTime));

        //    }
        //    catch (Exception e)
        //    {
        //        RaiseError(e);
        //        throw;
        //    }
        //}
        /// <summary>
        /// Called when a stream switch is requested on the System.Windows.Controls.MediaElement.
        /// </summary>
        /// <param name="mediaStreamDescription"></param>
        protected override void SwitchMediaStreamAsync(MediaStreamDescription mediaStreamDescription)
        {
            try
            {
                _workQueue.Enqueue(new WorkQueueElement(WorkQueueElement.Command.SwitchMedia, mediaStreamDescription));
            }
            catch (Exception e)
            {
                RaiseError(e);
                throw;
            }
        }

        /// <summary>
        /// returns amount of sample data in buffer. 
        /// </summary>
        public TimeSpan BufferLevel
        {
            get
            {
                if (_audioBuffer != null)
                {
                    // only audio buffer duration is thrustable, since audio/video is in sync,
                    // only return audio buffer is good enough.
                    return _audioBuffer.BufferLevel;
                }

                if (null != _videoBuffer)
                {
                    // in rare case, ts only has video but no audio, use video buffer duration ( guessed ) in this case.
                    return _videoBuffer.BufferLevel;
                }
                
                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// returns true if the playlist has specificed program date time 
        /// </summary>
        public bool HasProgramDateTime
        {
            get
            {
                return _programDateTime != null;
            }
        }

        /// <summary>
        /// returns program time of current position
        /// if the playlist did not specific program datetime, return DateTime.MinValue
        /// </summary>
        /// <param name="currentPosition"> the current playback position get from media element</param>
        public DateTime GetProgramTime( TimeSpan currentPosition )
        {
            if (HasProgramDateTime)
            {
                return _programDateTime.TsStartTime + currentPosition;
            }
            else
            {
                return DateTime.MinValue;
            }
        }

        public long BufferLevelInBytes
        {
            get
            {
                long totalSize = 0;
                if (_audioBuffer != null)
                    totalSize += _audioBuffer.BufferLevelInBytes;
                if (_videoBuffer != null)
                    totalSize += _videoBuffer.BufferLevelInBytes;
                return totalSize;
            }
        }

        //
        //
        // Private implementation
        //
        //

        /// <summary>
        /// This is our worker thread which dispatches all our commands and does buffering
        /// </summary>
        private void WorkerThread()
        {
            try
            {

                TimeSpan waitForCommandTimeout = TimeSpan.FromMilliseconds(0);

                while (true)
                {
                    if (_workQueue.WaitForWorkItem(waitForCommandTimeout))
                    {
                        WorkQueueElement elem = _workQueue.Dequeue();

                        if (elem == null)
                        {
                            // Clear was called on queue before elem was retrieved: it's either Close or Error
                            continue;
                        }

                        switch (elem.CommandToPerform)
                        {
                            case WorkQueueElement.Command.Open:
                                DoOpenMedia();
                                break;

                            case WorkQueueElement.Command.Close:
                                // Abort/close means that we should exit, this object should be discarded and never used again
                                // I.e. Terminate worker thread
                                DoCloseMedia();
                                return;

                            case WorkQueueElement.Command.Diagnostics:
                                DoGetDiagnostic((MediaStreamSourceDiagnosticKind)elem.CommandParameter);
                                break;

                            case WorkQueueElement.Command.Seek:

                                if (_state != State.Opened)
                                {
                                    return;
                                    //throw new InvalidOperationException("Seek operation can only be started while being in 'Opened' state");
                                }

                                _state = State.Seeking;

                                _requestedSeekPosition = (long)elem.CommandParameter;
                                DoSeek(_requestedSeekPosition);

                                break;

                            case WorkQueueElement.Command.SwitchMedia:
                                DoSwitchMediaStream(elem.CommandParameter as MediaStreamDescription);
                                break;

                            case WorkQueueElement.Command.NextStream:
                                DoNextStream((EncryptedStream)elem.CommandParameter);
                                break;
                        }
                    }
                    else
                    {
                        waitForCommandTimeout = TimeSpan.FromMilliseconds(1);

                        if (_demux == null || _playback.IsWebRequestPending())
                        {
                            // Do not do a tight loop if nothing do in the loop 
                            continue;
                        }

                        if (!_demux.IsEndOfStream)
                        {
                            try
                            {
                                _demux.ReadChunk();
                                ReportPendingSamples();

                                if (_demux.IsEndOfStream)
                                {
                                    // chunk read completed, clear _TSReadErrorCount
                                    _TSReadErrorCount = 0;
                                }
                            }
                            catch( Exception e )
                            {
                                _demux.Flush(true);
                                HLSTrace.WriteLine("Flush demux as exception is raised in _demux.ReadChunk");
                                HLSTrace.PrintException(e);
                                _TSReadErrorCount++;
                                if (_TSReadErrorCount < _maxTSReadErrorCount)
                                {
                                    // We handle a few TS demux read chunk errors before we throw and exception.
                                    _demux.IsEndOfStream = true;
                                    HLSTrace.WriteLine(" Ignored this TS Demux ReadChunk error and continue. error count = {0} ", _TSReadErrorCount );
                                }
                                else
                                {
                                    HLSTrace.WriteLine(" Too many TS Demux ReadChunk errors, exiting the playback ");
                                    throw (e);
                                }
                            }
                        }

                        if (_demux.IsEndOfStream
                            && _audioBuffer.BufferLevel <= _bufferLength 
                            && _videoBuffer.BufferLevel <= _bufferLength )
                        {
                            // finished download a segement and buffer is not full, try to download next segment.
                            lock (_playback)
                            {
                                if (_playback.BeginGetNextStream(new AsyncCallback(AsyncStreamCallback), _playback) == null)
                                {
                                    _demux.Flush(!_playback.IsEndList);
                                    _audioBuffer.EndOfPlayback = true;
                                    _videoBuffer.EndOfPlayback = true;
                                    ReportPendingSamples();
                                }
                            }
                        }

                        HandlePendingSeek();
                    }
                }
            }
            catch (Exception e)
            {
                RaiseError(e);
            }
        }

        /// <summary>
        /// Try to align audio and video buffer to start with similar timestamp within a threshold 
        /// </summary>
        /// <returns>If buffers are aligned after the call</returns>
        private bool AlignBuffersAfterSeek()
        {
            while (true)
            {
                // Remove any non-key video frame before first key frame, use key frame as align baseline for video
                while (_videoBuffer.HasSamples)
                {
                    Sample sample = _videoBuffer.PeekSample();
                    if (sample.KeyFrame)
                    {
                        break;
                    }
                    else
                    {
                        HLSTrace.WriteLineLow("Remove non-key frame video sample at {0}", sample.AdjustedTimeStamp);
                        _videoBuffer.RemoveHead();
                    }
                }

                if (_videoBuffer.HasSamples && _audioBuffer.HasSamples)
                {
                    long videoTimestamp = _videoBuffer.CurrentTimestamp;

                    if (_audioBuffer.CurrentTimestamp < videoTimestamp)
                    {
                        //
                        // If audio buffer starts at a samller timestamp than video buffer, remove audio
                        // samples with smaller timestamp to align to video
                        //
                        while (_audioBuffer.HasSamples && _audioBuffer.CurrentTimestamp < videoTimestamp)
                        {
                            HLSTrace.WriteLineLow("Remove audio sample at {0}", _audioBuffer.CurrentTimestamp);
                            _audioBuffer.RemoveHead();
                        }

                        // If auido buffer is not empty, alignment succeeded, otherwise failed
                        if (_audioBuffer.HasSamples)
                        {
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        //
                        // If audio buffer starts at a bigger timestamp than video buffer
                        //
                        long delta = _audioBuffer.CurrentTimestamp - videoTimestamp;
                        if (delta > ConvertHelper.SecondInHNS)
                        {
                            //
                            // The difference is bigger than threshold, remove one frame from video buffer,
                            // then continue the whole alignment process again
                            //
                            HLSTrace.WriteLineLow("Remove key frame video sample at {0}", videoTimestamp);
                            _videoBuffer.RemoveHead();
                            continue;
                        }
                        else
                        {
                            // The difference is within threshold, consider buffers are aligned
                            return true;
                        }
                    }
                }
                else
                {
                    // Either video or audio doesn't have sample, alignment failed
                    return false;
                }
            }
        }

        private void HandlePendingSeek()
        {
            if (_state == State.Seeking)
            {
                bool isAligned = false;
                if (_openParam.ifAlignBufferAfterSeek)
                {
                    isAligned = AlignBuffersAfterSeek();
                }

                if ((_openParam.ifAlignBufferAfterSeek && isAligned) || !_openParam.ifAlignBufferAfterSeek)
                {
                    if (_playlistOverrided)
                    {
                        if (!_openParam.ifAlignBufferAfterSeek && !_videoBuffer.HasSamples)
                        {
                            //
                            // If no alignment required, and vidio buffer doesn't have any sample yet, simply
                            // return so that next time we can report seek complete only whne video buffer has sample
                            //
                            return;
                        }

                        //
                        // If playlist is overrided, we should just from the beginning of the playlist
                        // no further seeking is needed
                        //
                        _state = State.Opened;
                        HLSTrace.WriteLine("Calling ReportSeekCompleted for overrided playlist");
                        ReportSeekCompleted(Int64.MinValue);
                    }
                    else
                    {
                        //
                        // While in seeking state, we parse the TS packets via _demux.ReadChunk(), and then 
                        // look if the A/V samples matching the requested seek point exist in audio and 
                        // video buffers
                        //
                        if (_openParam.ifAlignBufferAfterSeek)
                        {
                            _state = State.Opened;
                            HLSTrace.WriteLine("Calling ReportSeekCompleted with seek time = {0}", _audioBuffer.CurrentTimestamp);
                            ReportSeekCompleted(_audioBuffer.CurrentTimestamp);
                        }
                        else
                        {
                            if (_videoBuffer.HasSamples)
                            {
                                _state = State.Opened;
                                HLSTrace.WriteLine("Calling ReportSeekCompleted with seek time = {0}", _videoBuffer.CurrentTimestamp);
                                ReportSeekCompleted(_videoBuffer.CurrentTimestamp);
                            }
                            else if (_audioBuffer.HasSamples)
                            {
                                _state = State.Opened;
                                HLSTrace.WriteLine("Calling ReportSeekCompleted with seek time = {0}", _audioBuffer.CurrentTimestamp);
                                ReportSeekCompleted(_audioBuffer.CurrentTimestamp);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Get sample buffer based on media type
        /// </summary>
        private SampleBuffer GetBuffer(MediaStreamType mediaType)
        {
            if( mediaType == MediaStreamType.Audio )
                return _audioBuffer;
            else if ( mediaType == MediaStreamType.Video )
                return _videoBuffer;
            else
                throw new InvalidOperationException("invalid media type");
        }

        /// <summary>
        /// Report any samples that were previously requested but not served due to low buffer.
        /// </summary>
        private void ReportPendingSamples()
        {
            Sample sample;

            // if exit buffering is EOS or buffering reached high buffer threshold
            if (_isBuffering)
            {
                if ((BufferLevel < _openParam.startupBuffer) && (!_audioBuffer.EndOfPlayback || !_videoBuffer.EndOfPlayback))
                {
                    double newProgress = BufferLevel.TotalMilliseconds / _openParam.startupBuffer.TotalMilliseconds;
                    if (newProgress >= _lastBufferingProgressReported + 0.05)
                    {
                        if (_audioBuffer.NeedSamples > 0 || _videoBuffer.NeedSamples > 0)
                        {
                            // only report sample progress if there is pending sample requests.
                            _lastBufferingProgressReported = newProgress;
                            ReportGetSampleProgress(newProgress);
                        }
                    }
                    HLSTrace.WriteLineLow("Staying in buffering mode: BufferLevel = {0} MSec, _bufferLength = {1} MSec, Buffer fullness = %{2}, Progress reported = {3}", BufferLevel.TotalMilliseconds, _bufferLength.TotalMilliseconds, (100.00 * BufferLevel.TotalMilliseconds / _bufferLength.TotalMilliseconds).ToString("F"), _lastBufferingProgressReported);
                    return;
                }
                else
                {
                    HLSTrace.WriteLine("Exiting buffering mode: BufferLevel = {0} MSec, _bufferLength = {1} MSec, Buffer fullness = %{2}", BufferLevel.TotalMilliseconds, _bufferLength.TotalMilliseconds, (100.00 * BufferLevel.TotalMilliseconds / _bufferLength.TotalMilliseconds).ToString("F"));
                    _isBuffering = false;
                }
            }

            if (_audioBuffer.NeedSamples == 0 && _videoBuffer.NeedSamples == 0)
            {
                // if no sample is requested, nothing to report, exit. 
                return;
            }

            bool lockAcquired = false;
            try
            {
                lockAcquired = Monitor.TryEnter(_reportPendingSamplesLock);
                if (lockAcquired)
                {                    // samplesToReport is to keep a list to sample to be reported later on. 
                    while ( ( sample =_audioBuffer.ProcessPendingSampleRequest() ) != null )
                    {
                        ReportGetSampleCompleted(sample.ToMediaStreamSample());
                        if (null != sample.HLSStream && sample.HLSStream.SegmentProgramTime != null)
                        {
                            _programDateTime = sample.HLSStream.SegmentProgramTime.programTime;
                        }
                        _audioBuffer.RecycleSample(sample);
                    }
            
                    while ((sample = _videoBuffer.ProcessPendingSampleRequest()) != null)
                    {
                        ReportGetSampleCompleted(sample.ToMediaStreamSample());

                        if (null != sample.HLSStream && sample.HLSStream.SegmentProgramTime != null)
                        {
                            _programDateTime = sample.HLSStream.SegmentProgramTime.programTime;
                        }

                        // report bitrate changed after delivered to pipeline 
                        _videoBuffer.RecycleSample(sample);
                        uint curBitrate = _videoBuffer.CurrentBitrate;
                        if (_videoBuffer.HasSamples && Playback.PlaybackBitrateChanged != null && curBitrate != _lastSampleReportedBitrate)
                        {
                            Playback.PlaybackBitrateChanged(this, curBitrate);
                            _lastSampleReportedBitrate = curBitrate;
                        }
                    }
                }
            }
            finally
            {
                if( lockAcquired )
                {
                    Monitor.Exit( _reportPendingSamplesLock );
                }
            }
        }

        /// <summary>
        /// First stage of media open sequence:
        ///  allocate buffers, start loading playlist
        /// </summary>
        private void DoOpenMedia()
        {
            if (_state != State.None)
                RaiseError("calling OpenMedia on an open media stream");

            _state = State.Opening;

            if (_playlist.IsLoaded)
            {
                Playlist_PlaylistReady(null);
            }
            else
            {
                _playlist.PlaylistReady += Playlist_PlaylistReady;
                _playlist.PlaylistError += Playlist_PlaylistError;
                _playlist.Load();
            }
        }

        private void OnAllSubPlayListLoaded()
        {
            // DoCloseMedia may happend before the AllSubPlayListEvent event fired.
            if (_playlist == null || _program == null) return;

            uint maxBitrate = 0;
            _playback.AllSubPlayListEvent -= OnAllSubPlayListLoaded;
            foreach (HLSVariant hlsVariant in _program.Variants)
            {
                hlsVariant.Playback = null;
                if (hlsVariant.Bitrate > maxBitrate)
                {
                    maxBitrate = hlsVariant.Bitrate;
                }
            }

            //
            // Allocate video buffer based on max bitrate and buffer length plus 10 extra seconds of pending chunk progressive 
            // downloading / parsing.
            // Minimal initial buffer size is 5MB (corresponding 1Mb bitrate) in case playlist returns unreasonable low bitrate.
            // Max initial buffer size is 50MB (corrsponding 10Mb bitrate) in case playlist returns unsupported high bitrate.
            //
            int initialVideoBufferSize = Math.Max((int)((_bufferLength.TotalSeconds + 10) * maxBitrate / 8), 5 * 1024 * 1024);
            initialVideoBufferSize = Math.Min(initialVideoBufferSize, 50 * 1024 * 1024);
            _videoBuffer = new SampleBuffer(initialVideoBufferSize);
            // Allocate audio buffer based on 256Kb audio bitrate and buffer length plus 10 extra seconds of pending chunk progressive 
            // downloading / parsing.
            _audioBuffer = new SampleBuffer((int)((_bufferLength.TotalSeconds + 10) * 256 * 1024) / 8);

            PlayListOverrideInfo overrideInfo = new PlayListOverrideInfo();
            overrideInfo.reason = PlayListOverrideReason.ePlaylistLoaded;
            overrideInfo.position = TimeSpan.Zero;
            OverridePlaylistIfNecessary(overrideInfo);

            if (_playback.Program == null)
                _playback.Program = _program;

            if (_playback.BeginGetNextStream(new AsyncCallback(AsyncStreamCallback), _playback) == null)
            {
                RaiseError("unable to open stream");
            }
        }

        private bool OverridePlaylistIfNecessary(PlayListOverrideInfo overrideInfo)
        {
            bool isOverrided = false;
            if (null != _playlistOverrideEvent)
            {
                HLSExternalPlayListImpl externalPlayLsit = new HLSExternalPlayListImpl();
                if (overrideInfo.reason == PlayListOverrideReason.ePlaylistLoaded)
                {
                    externalPlayLsit.FromHLSPlaylist(_playlist);
                }
                if (_playlistOverrideEvent(externalPlayLsit, overrideInfo))
                {
                    isOverrided = true;
                    _playlist.FromHLSExternalPlaylist(externalPlayLsit);
                    lock (_playback)
                    {
                        _program = _playlist.Programs[0];
                        _playback.Program = _program;
                        _playback.ResetPlayback(_program, _playlist.PlaylistDuration);

                        List<SampleBuffer.TimeLineInfo> timelineInfoList = new List<SampleBuffer.TimeLineInfo>();
                        foreach (TimelineEstablishInfo establishInfo in externalPlayLsit.timelineEstablishInfoList)
                        {
                            timelineInfoList.Add(new SampleBuffer.TimeLineInfo(establishInfo.timelineStartOffset.Ticks, establishInfo.isMonoIncrease));
                        }
                        _videoBuffer.EstablishTimeline(timelineInfoList);
                        _audioBuffer.EstablishTimeline(timelineInfoList);
                    }
                }
            }

            if (!isOverrided && overrideInfo.reason == PlayListOverrideReason.ePlaylistLoaded)
            {
                List<SampleBuffer.TimeLineInfo> timelineInfoList = new List<SampleBuffer.TimeLineInfo>();
                timelineInfoList.Add(new SampleBuffer.TimeLineInfo(0, false));

                _videoBuffer.EstablishTimeline(timelineInfoList);
                _audioBuffer.EstablishTimeline(timelineInfoList);
            }

            _playlistOverrided = isOverrided;
            return isOverrided;
        }

        /// <summary>
        /// Called when playlist file has been loaded and parsed. This implementation selects a program,
        /// creates a playback wrapper and asynchronously requests first stream to play.
        /// </summary>
        private void Playlist_PlaylistReady(object sender)
        {
            // DoCloseMedia may happend before the PlaylistReady event fired.
            if (_playlist == null) return;

            if (_program == null)
                _program = _playlist.Programs[0];

            if (_playback == null)
                _playback = new HLSPlayback(_program, (IVariantSelector)this);

            lock (_playback)
            {
                if (_openParam.isDownloadAllSubPlayList || null != _playlistOverrideEvent)
                {
                    _playback.OnStartLoadingSubPlayList(_program.Variants.Count);
                    _playback.AllSubPlayListEvent += OnAllSubPlayListLoaded;
                    foreach (HLSVariant hlsVariant in _program.Variants)
                    {
                        hlsVariant.Playback = _playback;
                        hlsVariant.Load();
                    }
                }
                else
                {
                    OnAllSubPlayListLoaded();
                }
            }
        }

        /// <summary>
        /// Called when playlist has detected a fatal error.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="exception"></param>
        private void Playlist_PlaylistError(object sender, Exception exception)
        {
            RaiseError(exception);
        }

        /// <summary>
        /// Asynchronous callback for dealing with media file opening
        /// </summary>
        /// <param name="asyncResult"></param>
        private void AsyncStreamCallback(IAsyncResult asyncResult)
        {
            EncryptedStream stream = null;


            if (_playback == null)
                _playback = new HLSPlayback(_program, (IVariantSelector)this);

            lock (_playback)
            {
                if (_playback.Program == null)
                    _playback.Program = _program;

                stream = _playback.EndGetNextStream(asyncResult);
            }

            if (_state != State.Seeking)
            {
                _workQueue.Enqueue(new WorkQueueElement(WorkQueueElement.Command.NextStream, stream));
            }
            else
            {
                UpdateDemux(stream);

                _playback.EndPendingWebRequest();
            }

        }

        /// <summary>
        /// Create new demux from a stream
        /// </summary>
        /// <param name="stream"></param>        
        private void UpdateDemux(EncryptedStream stream)
        {
            TSDemux newDemux;
            if (_demux != null && _demux.IsMediaInfoReady )
            {
                newDemux = new TSDemux(stream, _demux, _playback.Metadata, _playback.CurrentDownloadBitrate, _bwHistory);
            }
            else
            {
                newDemux = new TSDemux(stream, _audioBuffer, _videoBuffer, _playback.Metadata, _playback.CurrentDownloadBitrate, _bwHistory);
            }

            if (_demux != null)
            {
                if (!_demux.IsMediaInfoReady)
                {
                    // If last demux has no media info ready, we are not carrying over demux parser buffers has been allocated,
                    // so make sure we discard those buffers by Flush(true)
                    _demux.Flush(true);
                    HLSTrace.WriteLine("Discard samples as last demux has no media info ready");
                }
                _demux.Dispose();
            }

            _demux = newDemux;

            while (!_demux.IsMediaInfoReady && !_demux.IsEndOfStream)
            {
                _demux.ReadChunk();
            }

            if (!_demux.IsMediaInfoReady)
                RaiseError("unable to parse stream");
        }

        /// <summary>
        /// In-thread handler for media file opening
        /// </summary>
        /// <param name="stream"></param>
        private void DoNextStream(EncryptedStream stream)
        {
            try
            {
                if (stream == null)
                {
                    if (_state == State.Opening)
                        RaiseError("unable to open stream");

                    if (_demux != null)
                        _demux.Flush(true);

                    _audioBuffer.EndOfPlayback = true;
                    _videoBuffer.EndOfPlayback = true;
                    ReportPendingSamples();

                    return;
                }

                UpdateDemux(stream);

                if (_state == State.Opening)
                {
                    _isBuffering = true;

                    Dictionary<MediaSourceAttributesKeys, string> sourceAttributes = new Dictionary<MediaSourceAttributesKeys, string>();
                    TimeSpan playlistDuration;

                    if (_playback.IsEndList)
                    {
                        playlistDuration = _playback.Duration;
                        sourceAttributes[MediaSourceAttributesKeys.CanSeek] = Boolean.TrueString;
                    }
                    else
                    {
                        if (_playback.Duration > _liveDvrMinDuration)
                        {
                            playlistDuration = _playback.Duration;
                            sourceAttributes[MediaSourceAttributesKeys.CanSeek] = Boolean.TrueString;
                        }
                        else
                        {
                            playlistDuration = new TimeSpan(0, 0, 0);
                            sourceAttributes[MediaSourceAttributesKeys.CanSeek] = Boolean.FalseString;
                        }
                        if (_playback.TargetDuration.TotalSeconds > 0 && _bwHistory.MaxHistoryCount < _playback.TargetDuration.TotalSeconds + 1)
                        {
                            _bwHistory.MaxHistoryCount = (int)_playback.TargetDuration.TotalSeconds + 1;
                        }

                    }

                    if (null != _playlistOverrideEvent)
                    {
                        // Playlist is overrided use max timeline plus duration as whole duration
                        sourceAttributes[MediaSourceAttributesKeys.Duration] = (playlistDuration + TimeSpan.FromTicks(_audioBuffer.MaxStartTimeInAllTimelines)).Ticks.ToString();
                    }
                    else
                    {
                        sourceAttributes[MediaSourceAttributesKeys.Duration] = playlistDuration.Ticks.ToString();
                    }
                    
                    List<MediaStreamDescription> availableMediaStreams = new List<MediaStreamDescription>();
                    availableMediaStreams.Add(_audioBuffer.Description);

                    // The video width and height that we pass to MediaElement should be set to the maximum resolution that 
                    // this HLS playlist could possibly use. This ensures that the codec can handle high resolution video 
                    // streams as well. The _videoBuffer.Description currently holds the resolution for the current playlist, 
                    // which is not necessairly the maximum resolution. If the resolution tag is missing for one of variants, 
                    // we will default to the maximim resolution of 1280x720. 
                    MediaStreamDescription videoMSD = _videoBuffer.Description;

                    uint maxPicWidth = uint.Parse(videoMSD.MediaAttributes[MediaStreamAttributeKeys.Width]);
                    uint maxPicHeight = uint.Parse(videoMSD.MediaAttributes[MediaStreamAttributeKeys.Height]);

                    foreach (HLSVariant subPlaylist in this.Playback.Program.Variants)
                    {
                        string s;
                        if (subPlaylist.MetaData != null &&
                            subPlaylist.MetaData.TryGetValue(HLSPlaylistMetaKeys.Resolution, out s))
                        {
                            string[] components = s.Split(new char[] { 'x' });
                            if (components != null && components.Length == 2)
                            {
                                uint subStreamWidth = uint.Parse(components[0]);
                                uint subStreamHeight = uint.Parse(components[1]);

                                if (maxPicHeight < subStreamHeight)
                                {
                                    maxPicHeight = subStreamHeight;
                                }

                                if (maxPicWidth < subStreamWidth)
                                {
                                    maxPicWidth = subStreamWidth;
                                }
                            }
                        }
                    }

                    if (_openParam.maxPicWidth > 0)
                    {
                        if (maxPicWidth < _openParam.maxPicWidth)
                        {
                            maxPicWidth = _openParam.maxPicWidth;
                        }
                    }

                    if (_openParam.maxPicHeight > 0)
                    {
                        if (maxPicHeight < _openParam.maxPicHeight)
                        {
                            maxPicHeight = _openParam.maxPicHeight;
                        }
                    }

                    videoMSD.MediaAttributes[MediaStreamAttributeKeys.Width] = maxPicWidth.ToString();
                    videoMSD.MediaAttributes[MediaStreamAttributeKeys.Height] = maxPicHeight.ToString();

                    availableMediaStreams.Add(videoMSD);
                    ReportOpenMediaCompleted(sourceAttributes, availableMediaStreams);

                    _state = State.Opened;
                }
            }
            catch (Exception exception)
            {
                RaiseError(exception);
            }
            finally
            {
                _playback.EndPendingWebRequest();
            }
        }

        /// <summary>
        /// Handler for Close command
        /// </summary>
        private void DoCloseMedia()
        {
            lock (_workQueueThreadLock)
            {
                HLSTrace.WriteLine("DoCloseMedia");
                _bwHistory.Close();
                _playback.AbortStreamDownloads();
                _isWorkQueueThreadStarted = false;
                _workQueueThread = null;
                _workQueue = null;
                _audioBuffer = null;
                _videoBuffer = null;
                _playlist = null;
                _playback = null;
                _program = null;
                if (_demux != null)
                {
                    _demux.Dispose();
                    _demux = null;
                }
            }
        }

        /// <summary>
        /// Handler for diagnostics command
        /// </summary>
        /// <param name="diagnosticKind"></param>
        private void DoGetDiagnostic(MediaStreamSourceDiagnosticKind diagnosticKind)
        {
            switch (diagnosticKind)
            {
                case MediaStreamSourceDiagnosticKind.BufferLevelInMilliseconds:
                    ReportGetDiagnosticCompleted(diagnosticKind, (long)BufferLevel.TotalMilliseconds);
                    break;

                case MediaStreamSourceDiagnosticKind.BufferLevelInBytes:
                    ReportGetDiagnosticCompleted(diagnosticKind, BufferLevelInBytes);
                    break;
            }
        }

        private bool _noSeekYet = true;

        private bool _noSampleRequestYet = true;

        /// <summary>
        /// Handler for Seek command
        /// </summary>
        /// <param name="seekToTime"></param>
        private void DoSeek(long seekToTime)
        {
            HLSTrace.WriteLine("HLS MSS DoSeek, seek time {0} Hns", seekToTime);

            if (_noSeekYet && _noSampleRequestYet && 0 == seekToTime)
            {
                // There is no need to cancel download and start download again for the very first seek
                _noSeekYet = false;
                return;
            }
            _noSeekYet = false;

            // If a stream download is in progress using HttpWebRequest, then this call will flag 
            // that request to discard its download stream. The playback impl will not 
            // start any new stream download until ResumeDownloads is called. After this call, 
            // no more data should be pushed into TSDemux or audio/video buffers. 
            _playback.AbortStreamDownloads();
            // Clear work queue as after _playback.AbortStreamDownloads() there shouldn't be any pending NextStream work queue items
            _workQueue.Clear(WorkQueueElement.Command.NextStream);
            _isBuffering = false;
            _audioBuffer.EndOfPlayback = false;
            _videoBuffer.EndOfPlayback = false;
            ReportPendingSamples();

            // Flush and discard all data that are in the TSDemux or audio/video buffers
            _demux.Flush(false);
            _demux.Dispose();
            _demux = null;
            _audioBuffer.Flush();
            _videoBuffer.Flush();

            PlayListOverrideInfo overrideInfo = new PlayListOverrideInfo();
            overrideInfo.reason = PlayListOverrideReason.eSeek;
            overrideInfo.position = new TimeSpan(seekToTime);
            if (!OverridePlaylistIfNecessary(overrideInfo))
            {
                // This will find the .ts stream that should contain the seek point, and would 
                // position the playback's stream index to point to that stream.  
                _playback.FindSeekStream(seekToTime);
            }
            if (Playback.CurrentStream != null)
            {
                _audioBuffer.ResetTSRollverOffset();
            }
            _lastSeekPlaylistPosition = _playback.CurrentPosition;

            _playback.ResumeDownloads();

            // enter buffering mode right after seek
            _isBuffering = true;

            TSDemux currentTSDemux = _demux;

            if (_playback.BeginGetNextStream(new AsyncCallback(AsyncStreamCallback), _playback) == null)
                RaiseError("unable to open stream");
        }


        /// <summary>
        /// Handler for SwitchMedia command
        /// </summary>
        /// <param name="mediaStreamDescription"></param>
        private void DoSwitchMediaStream(MediaStreamDescription mediaStreamDescription)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Raises an error to the media element. Shuts everything down and puts us in a closed state.
        /// </summary>
        /// <param name="message">message to raise</param>
        private void RaiseError(string message)
        {
            HLSTrace.WriteLine("HLS MSS Error: {0}", message);

            if (_workQueue != null)
                _workQueue.ClearAndEnqueue(new WorkQueueElement(WorkQueueElement.Command.Close, null));

            if (_state != State.Closed)
                ErrorOccurred(message);

            _state = State.Closed;
        }

        /// <summary>
        /// Raises an error to the media element. Shuts everything down and puts us in a closed state.
        /// </summary>
        /// <param name="e"></param>
        private void RaiseError(Exception e)
        {
            HLSTrace.PrintException(e);
            RaiseError(e.Message);
        }

        /// <summary>
        /// Application should register its delegate implementation on 
        /// event playlistOverrideEvent in order to override playlist. If not registered, HLSMediaStreamSource
        /// plays playlist as is.
        /// Timing and reason when this event is fired:
        /// When playlist is loaded this event is fired with reason ePlaylistLoaded.
        /// When playlist is refreshed this event is fired with reason ePlayListRefreshed.
        /// When seek operation is issued, this event is fired with reason eSeek.
        /// When SetRate operation is issued, this event is fired with reason eSetRate.
        /// 
        /// Application can leverage the mechanism to implement following scenario:
        /// For HULU, application can read ads information out of band. Then when event is fired
        /// with ePlaylistLoaded reason, application can go through the playlist in its delegate implementation,
        /// then use HLSSegment.startClockSignatureHNS as a signature to differentiate ads from content. After
        /// event is fired, HLSMediaStreamSource updates its internal data based on modified playlist. So when
        /// ads is playing, application can decode the signature from timestamp, know it is an ad to place a
        /// different UI and progress bar. When seek or setrate operation is issued, event is fired to ask application
        /// to override playlist so that application can implement its ads logic without letting HLSMediaStreamSource
        /// being aware of ads.
        /// </summary>
        public enum PlayListOverrideReason
        {
            ePlaylistLoaded = 0,
            eSeek,
        };

        public class PlayListOverrideInfo
        {
            public PlayListOverrideReason reason;
            public TimeSpan position;
        };

        /// <summary>
        /// Delegate implementation 
        /// </summary>
        /// <param name="mainPlayList"></param>
        /// <returns>If playlist has been changed</returns>
        public delegate bool PlayListOverrideEvent(HLSExternalPlayList mainPlayList, PlayListOverrideInfo overrideInfo);

        protected PlayListOverrideEvent _playlistOverrideEvent;

        public PlayListOverrideEvent PlayListOverride
        {
            get
            {
                return _playlistOverrideEvent;
            }
            set
            {
                _playlistOverrideEvent = value;
            }
        }

        public TimelineEventInfo GetTimelineEventInfo(TimeSpan timeStamp)
        {
            if (null != _audioBuffer)
            {
                return _audioBuffer.GetTimelineEventInfo(timeStamp);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// reserve the playlist stream index after previous seek. 
        /// </summary>
        private int _lastSeekPlaylistPosition;

        /// <summary>
        /// the radio of current bandwidth to be used when download the first segment after seek
        /// </summary>
        private float _bandwidthUsageRatioForFirstSegment = 0.5f;

        /// <summary>
        /// Accessor for bandwidthUSageRatioForFirstSegment
        /// </summary>
        internal float BandwidthUsageRatioForFirstSegment
        {
            get
            {
                return _bandwidthUsageRatioForFirstSegment;
            }
            set
            {
                _bandwidthUsageRatioForFirstSegment = value;
            }
        }

        /// <summary>
        /// The heuristics algorithm will use this threshold as one of its criterias for selecting a variant, 
        /// where the buffer size after _lookforwardDuration seconds should be larger than this threashold. 
        /// </summary>
        private TimeSpan _highHeuristicBufferThreshold = TimeSpan.FromSeconds(30.00);

        /// <summary>
        /// The heuristics algorithm will use this threshold as one of its criterias for selecting a variant, 
        /// where the buffer size during next lookforwardDuration seconds should never go below this threashold. 
        /// </summary>
        private TimeSpan _lowHeuristicBufferThreshold = TimeSpan.FromSeconds(5.00);
        
        /// <summary>
        /// The look forward duration used by heuristic algorithm. 
        /// </summary>
        private static TimeSpan _lookforwardDuration = TimeSpan.FromMinutes(2.00);


        /// <summary>
        /// Accessor for LookforwardDuration
        /// </summary>
        internal TimeSpan LookforwardDuration
        {
            get
            {
                return _lookforwardDuration;
            }
            set
            {
                _lookforwardDuration = value;
            }
        }

        /// <summary>
        /// Minimum bitrate to play, if multiple variants exist.
        /// </summary>
        private uint _minBitrate = 0;

        /// <summary>
        /// Maximum bitrate to play, if multiple variants exist.
        /// </summary>
        private uint _maxBitrate = uint.MaxValue;

        /// <summary>
        /// Minimum bitrate to play
        /// </summary>
        public uint MinBitrate
        {
            get
            {
                return _minBitrate;
            }
            set
            {
                _minBitrate = value;
            }
        }

        /// <summary>
        /// Maximum bitrate to play
        /// </summary>
        public uint MaxBitrate
        {
            get
            {
                return _maxBitrate;
            }
            set
            {
                _maxBitrate = value;
            }
        }
        /// <summary>
        /// calculate target buffer size for a given bandwidth and HLS variant.
        /// </summary>
        private void CalculateTargeBuffer(HLSVariant nextVariant, double bandwidth, out TimeSpan targetBuffer, out TimeSpan lowbuffer)
        {
            TimeSpan totalDuration = TimeSpan.FromTicks(0);
            int streamIndex = _playback.CurrentPosition;

            if (bandwidth == BandwidthHistory.UnknownBandwidth || bandwidth == 0.00)
            {
                targetBuffer = TimeSpan.FromTicks(0);
                lowbuffer = TimeSpan.FromTicks(0);
                return;
            }

            TimeSpan SegmentDefaultDuration = TimeSpan.FromTicks(0);

            if (nextVariant.IsLoaded)
            {
                // if we cannot get the segment duration from stream, therefore we will use the target 
                // duration tag instead. The target duration tag is mandatory according to HLS standard; 
                // however, it is yet missing from some HLS sources. If target duration tag is missing, 
                // we print out a warning message, and then we attempt to use the last segment duration, 
                // and if that fails, we use a default duration _defaultTargetDuration (10 seconds). If
                // the actual segments have a different duration, then this will result in wrong 
                // heuristics stream selection. 
                try
                {
                    SegmentDefaultDuration = nextVariant.TargetDuration;
                }
                catch (HLSPlaylistException e)
                {
                    HLSTrace.PrintException(e);

                    if (!_targetDurationWarningShown)
                    {
                        Debug.WriteLine("The mandatory EXT-X-TARGETDURATION is missing from the HLS playlist.  \r\n" +
                                        "The heuristics algorithm needs this tag for its buffer calculations.  \r\n" +
                                        "This may result in errors in heuristics calculations.                 \r\n");
                        _targetDurationWarningShown = true;
                    }
                    if (nextVariant.Streams != null && nextVariant.Streams.Count != 0)
                        SegmentDefaultDuration = nextVariant.Streams[nextVariant.Streams.Count - 1].Duration;
                    else
                        SegmentDefaultDuration = _defaultTargetDuration;
                }
            }

            targetBuffer = BufferLevel;
            lowbuffer = BufferLevel;

            while (totalDuration < _lookforwardDuration)
            {
                double segmentSize = 0.00;
                TimeSpan segmentPlaybackDuration = TimeSpan.FromTicks(0);

                if (nextVariant.Streams == null || streamIndex >= nextVariant.Streams.Count)
                {
                    segmentPlaybackDuration = SegmentDefaultDuration;
                }
                else
                {
                    segmentPlaybackDuration = nextVariant.Streams[streamIndex].Duration;
                    segmentSize = nextVariant.Streams[streamIndex].Size;
                    streamIndex++;
                }

                // if duration is missing, use the default 
                if (segmentPlaybackDuration.TotalMilliseconds == 0.00)
                    segmentPlaybackDuration = _defaultTargetDuration;

                // if segment size is not specified in playlist, use duration * bitrate
                if (segmentSize == 0)
                    segmentSize = ((double)nextVariant.Bitrate * segmentPlaybackDuration.TotalSeconds) / 8.00;

                TimeSpan segmentDownloadDuration = TimeSpan.FromSeconds((segmentSize * 8.00) / bandwidth);

                targetBuffer += ( segmentPlaybackDuration - segmentDownloadDuration );

                totalDuration += segmentPlaybackDuration;

                if (targetBuffer < lowbuffer)
                    lowbuffer = targetBuffer;
            }
        }

        /// <summary>
        /// heuristics implementation to select default variant 
        /// </summary>
        /// 
        void IVariantSelector.SelectVariant(HLSVariant previousVariant, HLSVariant heuristicSuggestedVariant, ref HLSVariant nextVariant, List<HLSVariant> availableVariants)
        {
            double avgBandwidth = _bwHistory.GetAverageBandwidth();
            double latestBandwidth = _bwHistory.GetLatestBandwidth();
            int i;

            if (_lastSeekPlaylistPosition == _playback.CurrentPosition)
            {
                // it is the first segment after seek. use a ratio of current bandwidth to speed up start time. 
                avgBandwidth = (int)( (float)(avgBandwidth) * _bandwidthUsageRatioForFirstSegment );
            }

            // intialize with lowest bitrate playlist
            nextVariant = availableVariants[0];
            for (i = 0; i < availableVariants.Count; i++)
            {
                if (availableVariants[i].Bitrate >= _minBitrate)
                {
                    nextVariant = availableVariants[i];
                    break;
                }
            }

            // This implements the logic for selecting next variant to download. 
            // We go over the variants, in the order of the highest bitrate to lowest bitrate, and calculate the targtet buffer 
            // after _lookforwardDuration from current time, and select the first variant that meet these two condition:
            // 1) the end buffer size after _lookforwardDuration is larger than _highHeuristicBufferThreshold
            // 2) the low buffer (the smallest buffer size during the looking forward scan) is higher than _lowHeuristicBufferThreshold or current buffer size 
            for (i = availableVariants.Count - 1; i >= 0; i--)
            {
                if (availableVariants[i].Bitrate >= _minBitrate  && availableVariants[i].Bitrate <= _maxBitrate)
                {
                    TimeSpan targetBufferSize;
                    TimeSpan lowBuffer;
                    CalculateTargeBuffer(availableVariants[i], avgBandwidth, out targetBufferSize, out lowBuffer);

                    if ((lowBuffer > _lowHeuristicBufferThreshold || lowBuffer >= BufferLevel) && targetBufferSize > _highHeuristicBufferThreshold)
                    {
                        nextVariant = availableVariants[i];
                        break;
                    }
                }
            }
        }

        public class HLSMediaSourceStatistics
        {
            public class MemoryPoolStatistics
            {
                public long allocCount;
                public long accumulatedAllocSize;
                public long poolSize;
                public long freeSize;
                public long growCount;
                public long shrinkCount;

                internal void Reset()
                {
                    allocCount = 0;
                    accumulatedAllocSize = 0;
                    poolSize = 0;
                    freeSize = 0;
                    growCount = 0;
                    shrinkCount = 0;
                }
            }

            public MemoryPoolStatistics _videoMemPoolStatistics = new MemoryPoolStatistics();

            public MemoryPoolStatistics _audioMemPoolStatistics = new MemoryPoolStatistics();

            internal void Reset()
            {
                _videoMemPoolStatistics.Reset();
                _audioMemPoolStatistics.Reset();
            }
        }

        private HLSMediaSourceStatistics _statistics = new HLSMediaSourceStatistics();

        public HLSMediaSourceStatistics MediaSourceStatistics
        {
            get
            {
                _statistics.Reset();
                if (null != _videoBuffer)
                {
                    _statistics._videoMemPoolStatistics.allocCount = _videoBuffer.FIFOMemoryPool.AllocCount;
                    _statistics._videoMemPoolStatistics.accumulatedAllocSize = _videoBuffer.FIFOMemoryPool.AccumulatedAllocSize;
                    _statistics._videoMemPoolStatistics.poolSize = _videoBuffer.FIFOMemoryPool.TotalSize;
                    _statistics._videoMemPoolStatistics.freeSize = _videoBuffer.FIFOMemoryPool.FreeSize;
                    _statistics._videoMemPoolStatistics.growCount = _videoBuffer.FIFOMemoryPool.GrowCount;
                    _statistics._videoMemPoolStatistics.shrinkCount = _videoBuffer.FIFOMemoryPool.ShrinkCount;
                }
                if (null != _audioBuffer)
                {
                    _statistics._audioMemPoolStatistics.allocCount = _audioBuffer.FIFOMemoryPool.AllocCount;
                    _statistics._audioMemPoolStatistics.accumulatedAllocSize = _audioBuffer.FIFOMemoryPool.AccumulatedAllocSize;
                    _statistics._audioMemPoolStatistics.poolSize = _audioBuffer.FIFOMemoryPool.TotalSize;
                    _statistics._audioMemPoolStatistics.freeSize = _audioBuffer.FIFOMemoryPool.FreeSize;
                    _statistics._audioMemPoolStatistics.growCount = _audioBuffer.FIFOMemoryPool.GrowCount;
                    _statistics._audioMemPoolStatistics.shrinkCount = _audioBuffer.FIFOMemoryPool.ShrinkCount;
                }
                return _statistics;
            }
        }

        #region IDisposable Members
        /// <summary>
        /// Implements IDisposable.Dispose
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Implements Dispose logic
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    lock (_workQueueThreadLock)
                    {
                        // If we have a work queue, post a close message to it
                        if (_workQueueThread != null && _workQueue != null)
                        {
                            // No need to respond to anything because we are shutting down
                            _workQueue.ClearAndEnqueue(new WorkQueueElement(WorkQueueElement.Command.Close, null));

                            // Wait for the thread to close
                            _workQueueThread.Join(3000);

                            // Dispose the work queue
                            _workQueue.Dispose();
                            _workQueue = null;
                        }
                    }
                }
                _disposed = true;
            }
        }
        #endregion

        public List<HLSVariant> Variants
        {
            get { return null; }
        }
    }
}



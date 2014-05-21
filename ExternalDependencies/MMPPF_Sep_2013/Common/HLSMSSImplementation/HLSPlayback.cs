using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Browser;
using System.Security.Cryptography;
using System.Threading;

namespace Silverlight.Samples.HttpLiveStreaming
{

    /// <summary>
    /// Interface for implementing adaptive bitrate changer.
    /// </summary>
    public interface IVariantSelector
    {
        /// <summary>
        /// Interface for callback to application, which allows the application to select the next HLS variant to 
        /// be downloaded and played. 
        /// <param name="previousVariant"> HLS variant that the previous segment was downloaded from </param>
        /// <param name="heuristicSuggestedVariant"> HLS variant that is suggsted by heuritstics algorithm to 
        /// be downloaded for next segment </param>
        /// <param name="nextVariant"> HLS variant to be used for downloading next segment </param>
        /// <param name="availableVariants"> A list of varients that are avaiable to application to select from,
        /// sorted by bitrate in increasing order </param>
        /// </summary>
        void SelectVariant(HLSVariant previousVariant, HLSVariant heuristicSuggestedVariant, ref HLSVariant nextVariant, List<HLSVariant> availableSortedVariants);

        List<HLSVariant> Variants { get; }
    }

    /// <summary>
    /// Indicates a purpose of HLSCallbackImpl existence.
    /// </summary>
    internal enum HLSCallbackPurpose
    {
        Default,                // Default value, purpose not known
        WaitForPlaylist,        // Waiting for playlist HTTP response
        WaitForEncryptionKey,   // Waiting for AES-128 key file response
        WaitForStream           // Waiting for stream HTTP response
    }

    /// <summary>
    /// Exception for HLSPlayback failures
    /// </summary>
    class HLSPlaybackException : Exception
    {
        public HLSPlaybackException(string message) :
            base(message)
        {
        }
    }

    /// <summary>
    /// Private IAsyncResult implementation to be used with HLSPlayback.BeginGetNextStream
    /// </summary>
    internal class HLSCallbackImpl : IAsyncResult, IDisposable
    {
        /// <summary>
        /// Wait handle for this async operation
        /// </summary>
        private volatile ManualResetEvent _waitHandle;

        /// <summary>
        /// Indicates async operation is completed
        /// </summary>
        private volatile bool _isCompleted;

        /// <summary>
        /// State object for this async operation
        /// </summary>
        private object _state;

        /// <summary>
        /// Purpose of this callback
        /// </summary>
        private HLSCallbackPurpose _purpose;

        /// <summary>
        /// User callback.
        /// </summary>
        private AsyncCallback _callback;

        /// <summary>
        /// Internal synchronization object.
        /// </summary>
        private object _lockable;

        /// <summary>
        /// Track whether Dispose has been called
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// If current async result being canceld or not
        /// </summary>
        private volatile bool _aborted;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="callback"></param>
        internal HLSCallbackImpl(AsyncCallback callback)
        {
            _callback = callback;
            _lockable = new object();
        }

        /// <summary>
        /// Triggers async operation completion.
        /// </summary>
        /// <param name="state"></param>
        internal void CompleteWithAsyncState(object state)
        {
            _state = state;
            _isCompleted = true;
            if (_waitHandle != null)
                _waitHandle.Set();
            if (_callback != null)
                _callback(this);
        }

        /// <summary>
        /// Purpose of this callback.
        /// </summary>
        public HLSCallbackPurpose Purpose
        {
            get
            {
                return _purpose;
            }
            set
            {
                _purpose = value;
            }
        }

        /// <summary>
        /// Abort async result
        /// </summary>
        public void Abort()
        {
            lock (_lockable)
            {
                _aborted = true;
            }
        }


        public bool IsAborted
        {
            get
            {
                lock (_lockable)
                {
                    return _aborted;
                }
            }
        }

        #region IAsyncResult Members
        public object AsyncState
        {
            get
            {
                return _state;
            }
        }

        public WaitHandle AsyncWaitHandle
        {
            get
            {
                if (_waitHandle == null)
                {
                    lock (_lockable)
                    {
                        if (_waitHandle == null)
                        {
                            _waitHandle = new ManualResetEvent(false);
                            if (_isCompleted)
                                _waitHandle.Set();
                        }
                    }
                }
                return _waitHandle;
            }
        }

        public bool CompletedSynchronously
        {
            get
            {
                return false;
            }
        }

        public bool IsCompleted
        {
            get
            {
                return _isCompleted;
            }
        }
        #endregion

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
                    if (_waitHandle != null)
                    {
                        _waitHandle.Close();
                        _waitHandle = null;
                    }
                }
                _disposed = true;
            }
        }
        #endregion
    }


    /// <summary>
    /// Abstraction for HTTP Live Streaming playback session.
    /// To begin HLS playback, users should new HLSPlayback()
    /// with program received from HLSPlaylist, then call
    /// BeginGetNextStream in a loop until all streams are played.
    /// </summary>
    public class HLSPlayback : IDisposable
    {
        private int _pendingSubPlayListCount = 0;

        public void OnStartLoadingSubPlayList(int iSubPlayListCount)
        {
            lock (this)
            {
                _pendingSubPlayListCount = iSubPlayListCount;
            }
        }

        /// <summary>
        /// Contains a program that's being played.
        /// </summary>
        private HLSProgram _program;

        /// <summary>
        /// Contains async callback when async operation is in progress,
        /// null any other time.
        /// </summary>
        private volatile HLSCallbackImpl _asyncResult;
        //lrj add
        private TimeSpan _liveDvrMinDuration = TimeSpan.FromSeconds(120);
        //public TimeSpan LiveDvrMinDuration
        //{
        //    get { return _liveDvrMinDuration; }
        //    set { _liveDvrMinDuration = value; }
        //}
        /// <summary>
        /// Current playlist duration
        /// </summary>
        private TimeSpan _duration = TimeSpan.Zero;

        /// <summary>
        /// Accessor for current playlist duration
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
        /// Current playlist or variant that's being played.
        /// </summary>
        private HLSPlaylistImpl _playlist;

        public void ResetPlayback(HLSProgram program, TimeSpan duration)
        {
            _program = program;
            _playlist = null;
            _duration = duration;
            _currentPosition = 0;
        }

        /// <summary>
        /// Contains current index of stream in current playlist.
        /// </summary>
        private int _currentPosition = 0;

        /// <summary>
        /// Accessor for current playlist position
        /// </summary>
        internal int CurrentPosition
        {
            get
            {
                return _currentPosition;
            }

        }

        /// <summary>
        /// Indicates current stream is discontinuous from previously played one.
        /// </summary>
        private bool _discontinuity;

        /// <summary>
        /// Current bitrate playing
        /// </summary>
        private uint _currentBitrate = 0;

        /// <summary>
        /// Hook for implementing adaptive bitrate changer.
        /// </summary>
        private IVariantSelector _variantSelector;

        /// <summary>
        /// Internal heursitics implementation provided by HLS MSS 
        /// </summary>
        private IVariantSelector _heuristics = null;

        /// <summary>
        /// Number of times we will retry downloading a stream file after our 
        /// HttpWebRequest has failed with a WebExceptions (e.g. NotFound error).
        /// </summary>
        private readonly int MAX_NUMBER_OF_RETRIES = 2;

        /// <summary>
        /// Number of times we have already retried downloading the current stream 
        /// but have failed with a WebException
        /// /// </summary>
        private int _reTryCount = 0;

        /// <summary>
        /// Track whether Dispose has been called
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Number of times we have already retried downloading the current stream 
        /// but have failed with a WebException
        /// /// </summary>

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="program"></param>
        public HLSPlayback(HLSProgram program, IVariantSelector heuristics )
        {
            _program = program;
            _heuristics = heuristics;
        }

        /// <summary>
        /// Hook for implementing adaptive bitrate changer.
        /// </summary>
        public IVariantSelector VariantSelector
        {
            get
            {
                return _variantSelector;
            }
            set
            {
                _variantSelector = value;
            }
        }

        public IContainerMetadata Metadata
        {
            get
            {
                return _playlist;
            }
        }

        /// <summary>
        /// Event for detecting when bitrate changes
        /// </summary>
        /// <param name="bitrate"></param>
        public delegate void BitrateChangedEvent(object sender, uint bitrate);

        /// <summary>
        /// Event for detecting when playback bitrate changes
        /// </summary>
        private BitrateChangedEvent _downloadBitrateChanged;

        /// <summary>
        /// Event for detecting when download bitrate changes
        /// </summary>
        public BitrateChangedEvent DownloadBitrateChanged
        {
            get
            {
                return _downloadBitrateChanged;
            }
            set
            {
                _downloadBitrateChanged = value;
            }
        }

        /// <summary>
        /// Event for detecting when download bitrate changes
        /// </summary>
        private BitrateChangedEvent _playbackBitrateChanged;

        /// <summary>
        /// Event for detecting when playback bitrate changes
        /// </summary>
        public BitrateChangedEvent PlaybackBitrateChanged
        {
            get
            {
                return _playbackBitrateChanged;
            }
            set
            {
                _playbackBitrateChanged = value;
            }
        }

        /// <summary>
        /// Event for detecting when next segment is about to start playing
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="uri"></param>
        public delegate void MediaFileChangedEvent(object sender, Uri uri);

        /// <summary>
        /// Event for detecting when next segment is about to start playing
        /// </summary>
        private MediaFileChangedEvent _mediaFileChanged;

        /// <summary>
        /// Event for detecting when next segment is about to start playing
        /// </summary>
        public MediaFileChangedEvent MediaFileChanged
        {
            get
            {
                return _mediaFileChanged;
            }
            set
            {
                _mediaFileChanged = value;
            }
        }

        /// <summary>
        /// Program that's being played.
        /// </summary>
        public HLSProgram Program
        {
            get
            {
                return _program;
            }
            set
            {
                _program = value;
            }
        }

        /// <summary>
        /// record the latest subplaylist loaded
        /// </summary>
        private HLSPlaylistImpl _LastSubPlaylistLoaded = null;
        public HLSPlaylistImpl LastSubPlaylistLoaded
        {
            get
            {
                return _LastSubPlaylistLoaded;
            }
        }
        /// <summary>
        /// media sequence number for current media segment
        /// </summary>
        public long _currentMediaSequenceNumber = 0;

        /// <summary>
        /// Returns media sequence number for current media segment
        /// </summary>
        public long CurrentMediaSequenceNumber
        {
            get
            {
                return _currentMediaSequenceNumber;
            }
        }

        /// <summary>
        /// Returns current media file object
        /// </summary>
        public HLSStream CurrentStream
        {
            get
            {
                lock (this)
                {
                    if (_playlist != null && _currentPosition >= 0 && _currentPosition < _playlist.Streams.Count)
                        return _playlist.Streams[_currentPosition];
                    else
                        return null;
                }
            }
        }

        /// <summary>
        /// Returns current media file object
        /// </summary>
        private class WebRequestState
        {
            public HttpWebRequest WebRequest;
            public bool DiscardData;
            public HLSCallbackImpl AsyncResult;
            public DateTime StartTime;
        }

        /// <summary>
        /// Returns current media file object
        /// </summary>
        private WebRequestState _currentWebRequestState = null;

        /// <summary>
        /// Flag indicating that current stream downloads should be abroted 
        /// </summary>
        private volatile bool _downloadAbortInProgress = false;

        /// <summary>
        /// Locakable object used for thread synchronization while aborting web requests 
        /// </summary>
        private object _requestLock = new object();

        /// <summary>
        /// Hook for selecting a variant to play. Default naive implementation selects first
        /// stream that matches bitrate restrictions.
        /// </summary>
        /// <param name="currentVariant"></param>
        /// <returns></returns>
        protected HLSPlaylistImpl SelectNextVariant()
        {
            HLSVariant suggestedVariant = null; 

            if (_heuristics != null)
                _heuristics.SelectVariant(_playlist as HLSVariant, null, ref suggestedVariant, _program.Variants);


            if (_variantSelector != null)
            {
                HLSVariant variant = null;

                // allow application to override the next variant
                _variantSelector.SelectVariant(_playlist as HLSVariant, suggestedVariant, ref variant, _program.Variants);

                if (variant != null)
                {
                    _currentBitrate = variant.Bitrate;
                    return variant;
                }
            }

            if (suggestedVariant != null)
            {
                _currentBitrate = suggestedVariant.Bitrate;
                return suggestedVariant;
            }

            // In cases that we do not have a heuristic impl and user has not provided a varint selector impl, default to 
            // the highest bitrate 
            _currentBitrate = _program.Variants[_program.Variants.Count - 1].Bitrate; 
            return _program.Variants[_program.Variants.Count - 1];

        }

        /// <summary>
        /// Asynchronous public API for getting next stream to play.
        /// </summary>
        /// <param name="callback"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        public IAsyncResult BeginGetNextStream(AsyncCallback callback, Object state)
        {
            if (_program == null)
                throw new InvalidOperationException("program not set");

            if ( IsWebRequestPending() )
                throw new InvalidOperationException("operation is already in progress");

            _asyncResult = new HLSCallbackImpl(callback);

            if (_playlist == null)
            {
                _playlist = SelectNextVariant();
                _playlist.Playback = this;
                _currentPosition = 0;
                _currentMediaSequenceNumber = 0;
                _discontinuity = true;

                if (IsEndList || _playlist.PlaylistDuration > _liveDvrMinDuration)               
                    _duration = _playlist.PlaylistDuration;

                if (_downloadBitrateChanged != null)
                    _downloadBitrateChanged(this, _playlist.Bitrate);
            }
            else
            {
                _currentPosition++;
                _currentMediaSequenceNumber++;

                HLSPlaylistImpl newPlaylist = SelectNextVariant();

                if (newPlaylist != _playlist)
                {
                    _discontinuity = true;

                    if (_downloadBitrateChanged != null &&
                        newPlaylist.Bitrate != _playlist.Bitrate)
                        _downloadBitrateChanged(this, newPlaylist.Bitrate);

                    _playlist.Playback = null;
                    _playlist = newPlaylist;
                    _playlist.Playback = this;

                }
                else
                {
                    _discontinuity = false;
                }
            }

            if (!_playlist.IsLoaded)
            {
                _asyncResult.Purpose = HLSCallbackPurpose.WaitForPlaylist;
                _playlist.Load();
                return _asyncResult;
            }
            bool a = _playlist.IsDueForReload;
            if (!IsEndList && ((_currentPosition >= _playlist.Streams.Count) || a))
            {
                _asyncResult.Purpose = HLSCallbackPurpose.WaitForPlaylist;
                _playlist.Reload();
                return _asyncResult;
            }

            if (!BeginLoadingNextStream())
            {
                _asyncResult.Dispose();
                _asyncResult = null;
            }

            return _asyncResult;
        }

        /// <summary>
        /// Called by class user from asynchronous callback to get access to response data
        /// </summary>
        /// <param name="asyncResult"></param>
        /// <returns></returns>
        public EncryptedStream EndGetNextStream(IAsyncResult asyncResult)
        {
            if (asyncResult != _asyncResult)
                throw new ArgumentException("Invalid asyncResult value");

            EncryptedStream stream = (EncryptedStream)_asyncResult.AsyncState;
            return stream;
        }

        public void EndPendingWebRequest()
        {
            if (null != _asyncResult)
            {
                _asyncResult.Dispose();
                _asyncResult = null;
            }
        }

        public delegate void OnAllSubPlayListLoaded();

        private OnAllSubPlayListLoaded _allSubPlayListEvent;

        public OnAllSubPlayListLoaded AllSubPlayListEvent
        {
            get
            {
                return _allSubPlayListEvent;
            }
            set
            {
                _allSubPlayListEvent = value;
            }
        }

        /// <summary>
        /// Internal handler for playlist load event
        /// </summary>
        internal void PlaylistLoadEnded()
        {
            if (_pendingSubPlayListCount > 0)
            {
                lock (this)
                {
                    _pendingSubPlayListCount --;
                    if (0 == _pendingSubPlayListCount)
                    {
                        if (null != _allSubPlayListEvent)
                        {
                            _allSubPlayListEvent();
                        }
                    }
                }
            }
            else
            {
                if (IsEndList || _playlist.PlaylistDuration > _liveDvrMinDuration)
                    _duration = _playlist.PlaylistDuration;
                

                _LastSubPlaylistLoaded = _playlist;

                if (_asyncResult != null && _asyncResult.Purpose == HLSCallbackPurpose.WaitForPlaylist)
                {
                    if (!IsEndList 
                          && ( CurrentMediaSequenceNumber > CurrentStream.SequenceNumber ) 
                          && !HLSPlaylist.IsSequenceNubmerRollback( CurrentStream.SequenceNumber, _playlist.Streams.Count, CurrentMediaSequenceNumber ) ) 
                    {
                        _asyncResult.Purpose = HLSCallbackPurpose.WaitForPlaylist;
                        _playlist.Reload();
                    }
                    else if (!BeginLoadingNextStream())
                    {
                        _asyncResult.CompleteWithAsyncState(null);
                    }
                }
            }
        }

        /// <summary>
        /// Internal handler for playlist load errors
        /// </summary>
        internal void PlaylistLoadError(Exception exception)
        {
            if (_asyncResult != null && _asyncResult.Purpose == HLSCallbackPurpose.WaitForPlaylist)
            {
                _asyncResult.CompleteWithAsyncState(null);
            }
        }

        /// <summary>
        /// Internal handler for encryption key load event
        /// </summary>
        /// <param name="uri"></param>
        internal void EncryptionKeyLoadEnded(Uri uri)
        {
            if (_asyncResult != null &&
                _asyncResult.Purpose == HLSCallbackPurpose.WaitForEncryptionKey &&
                CurrentStream.EncryptionKeyUri == uri)
            {
                BeginLoadingNextStreamWorker();
            }
        }

        /// <summary>
        /// Internal handler for encryption key load errors
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="exception"></param>
        internal void EncryptionKeyLoadError(Uri uri, Exception exception)
        {
            if (_asyncResult != null &&
                _asyncResult.Purpose == HLSCallbackPurpose.WaitForEncryptionKey &&
                CurrentStream.EncryptionKeyUri == uri)
            {
                _asyncResult.CompleteWithAsyncState(null);
            }
        }

        /// <summary>
        /// Starts downloading next stream in sequence
        /// </summary>
        /// <returns></returns>
        private bool BeginLoadingNextStream()
        {
            if (CurrentStream == null)
                return false;

            if (_mediaFileChanged != null)
                _mediaFileChanged(this, CurrentStream.Uri);

            if (CurrentStream.EncryptionMethod != HLSEncryptionMethod.None &&
                !_playlist.LoadEncryptionKeyForStream(CurrentStream))
            {
                _asyncResult.Purpose = HLSCallbackPurpose.WaitForEncryptionKey;
                return true;
            }

            BeginLoadingNextStreamWorker();
            return true;
        }


        /// <summary>
        /// Internal worker
        /// </summary>
        private void BeginLoadingNextStreamWorker()
        {
            _asyncResult.Purpose = HLSCallbackPurpose.WaitForStream;

            Uri uri = CurrentStream.Uri;
            HLSTrace.WriteLine("Downloading {0}", uri.ToString());

            _currentMediaSequenceNumber = CurrentStream.SequenceNumber;

            lock (_requestLock)
            {
                //Debug.Assert(_asyncResult != null, "_asyncResult cannot be null");

                if (_downloadAbortInProgress)
                {
                    HLSTrace.WriteLine("Aborting downloading of URI {0}", uri.ToString());
                    return;
                }

                HttpWebRequest request = (HttpWebRequest)WebRequestCreator.ClientHttp.Create(uri);

                request.AllowReadStreamBuffering = false;
                //request.CookieContainer = _playlist.CookieContainer;


                // uses xbox sliveright specific http header to disable http cache in xbox, 
                // for video playback, unlikely users will  watch the same clip again, also cached content
                // make bandwiddth measurement unusable. 
                request.Headers["x-ms-bypassclientsidecache"] = "1";

                _currentWebRequestState = new WebRequestState();
                _currentWebRequestState.WebRequest = request;
                _currentWebRequestState.DiscardData = false;
                _currentWebRequestState.AsyncResult = _asyncResult;
                _currentWebRequestState.StartTime = DateTime.Now;

                request.BeginGetResponse(new AsyncCallback(DataStreamResponseReceived), _currentWebRequestState);
            }
        }

        /// <summary>
        /// Asynchronous callback for handling stream data
        /// </summary>
        /// <param name="asyncResult"></param>
        private void DataStreamResponseReceived(IAsyncResult asyncResult)
        {
            lock (_requestLock)
            {
                WebRequestState requestState = null;
                HttpWebResponse response = null;

                try
                {
                    Debug.Assert(asyncResult.AsyncState is WebRequestState, "asyncResult.AsyncState must be of type WebRequestState");
                    
                    requestState = (WebRequestState)(asyncResult.AsyncState);
                    
                    if (requestState.DiscardData)
                    {
                        return;
                    }

                    response = (HttpWebResponse)requestState.WebRequest.EndGetResponse(asyncResult);
                    HLSTrace.TestInjectRandomError( "DataStreamResponseReceived", 0.1f );
                    HLSTrace.WriteLine("Downloaded response status {0} for {1}", response.StatusDescription, requestState.WebRequest.RequestUri.ToString());

                    if (requestState.DiscardData || requestState.AsyncResult.IsAborted)
                    {
                        requestState.AsyncResult.Dispose();

                        if (response.GetResponseStream() != null)
                            response.GetResponseStream().Close();

                        response.Close();
                        response = null;
                        return;
                    }

                    if (response.GetResponseStream() != null)
                    {
                        EncryptedStream playbackStream = null;
                        bool discontinuity = _discontinuity || CurrentStream.Discontinuity;
                        if (CurrentStream.EncryptionMethod == HLSEncryptionMethod.AES128)
                        {
                            using (AesManaged aes = new AesManaged())
                            {
                                aes.Key = _playlist.GetEncryptionKeyForStream(CurrentStream);
                                aes.IV = SynthesizeInitializationVector();
                                playbackStream = new EncryptedStream(response.GetResponseStream(), requestState.StartTime, response, discontinuity, aes.CreateDecryptor(), TSDemux.TSPacketSize, CurrentStream);
                            }
                        }
                        else
                        {
                            playbackStream = new EncryptedStream(response.GetResponseStream(), requestState.StartTime, response, discontinuity, CurrentStream);
                        }

                        Debug.Assert(_asyncResult != null, "_asyncResult cannot be null");
                        _asyncResult.CompleteWithAsyncState(playbackStream);

                    }
                    else
                    {
                        _asyncResult.CompleteWithAsyncState(null);
                    }
                }
                catch (Exception e)
                {
                    HLSTrace.PrintException(e);
                    if (response != null)
                    {
                        response.Close();
                    }

                    if (requestState.DiscardData || requestState.AsyncResult.IsAborted)
                    {
                        requestState.AsyncResult.Dispose();

                        return;
                    }

                    if (_reTryCount < MAX_NUMBER_OF_RETRIES)
                    {
                        HLSTrace.WriteLine( " download retrying...  count={0}", _reTryCount );
                        BeginLoadingNextStream();
                        _reTryCount++;
                    }
                    else
                    {
                        lock (this)
                        {
                            HLSTrace.WriteLine(" download retries all failed, skip it and download next chunk ...  count={0}", _reTryCount);
                            _reTryCount = 0;
                            _currentPosition++;
                            _currentMediaSequenceNumber++;
                            _discontinuity = true;
                            if (!IsEndList && (_currentPosition >= _playlist.Streams.Count))
                            {
                                _asyncResult.Purpose = HLSCallbackPurpose.WaitForPlaylist;
                                _playlist.Reload();
                                return;
                            }

                            BeginLoadingNextStream();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Creates initialization vector for AES decrypting
        /// </summary>
        /// <returns></returns>
        private byte[] SynthesizeInitializationVector()
        {
            byte[] initializationVector = new byte[16];
            string ivString = CurrentStream.EncryptionIV;
            if (!string.IsNullOrEmpty(ivString))
            {
                if (ivString.Length != 34 || ivString[0] != '0' || ivString[1] != 'x')
                    throw new HLSPlaybackException("invalid IV format");

                int position = 2;
                for (int i = 0; i < 16; i++)
                {
                    initializationVector[i] = byte.Parse(ivString.Substring(position, 2), System.Globalization.NumberStyles.HexNumber);
                    position += 2;
                }
            }
            else
            {
                long mediaSequenceNumber = CurrentMediaSequenceNumber;
                for (int i = 15; i >= 0; i--)
                {
                    initializationVector[i] = (byte)(mediaSequenceNumber & 0xff);
                    mediaSequenceNumber >>= 8;
                }
            }
            return initializationVector;
        }

        /// <summary>
        /// Internal method to update the stream position according to a given mediaSequenceNumber
        /// </summary>
        /// <param name="currentSequenceNumber"></param>
        internal void UpdateStreamPosition( long currentSequenceNumber )
        {
            if (null == _playlist)
            {
                return;
            }

            if (_playlist.Streams.Count == 0)
            {
                // TODO: in case of current playlist is empty(in some rare cases), we need to move to next no-empty playlist.
                _currentPosition = -1;
                return;
            }

            if ( HLSPlaylist.IsSequenceNubmerRollback( 
                    _playlist.Streams[0].SequenceNumber, 
                    _playlist.Streams.Count, 
                    currentSequenceNumber ) )
            {
                // the new playlist must has sequence number rollvered, use the first segement. 
                _currentPosition = 0;
                return;
            }
            ////lrj add

            //if (isstart&&!IsEndList && _playlist.Streams.Count > 12)
            //{
            //    isstart = false;
            //    _currentPosition = _playlist.Streams.Count - 12;
            //    return;
            //}

            if (_currentPosition >= _playlist.Streams.Count)
                _currentPosition = _playlist.Streams.Count - 1;

            // Streams list is sorted based on SequenceNumber, and in some cases can be a very long list. 
            // The mediaSequenceNumber is usually close to the _currentPosition, therefore we start the 
            // search around the _currentPosition. 
            while (_currentPosition < _playlist.Streams.Count - 1 && _playlist.Streams[_currentPosition].SequenceNumber < currentSequenceNumber)
                _currentPosition++;

            while (_currentPosition > 0 && _playlist.Streams[_currentPosition].SequenceNumber > currentSequenceNumber)
                _currentPosition--;
            
        }

        /// <summary>
        /// Returns true if underlying playlist is endlist
        /// </summary>
        public bool IsEndList
        {
            get
            {
                if (_playlist == null)
                    return true;

                return _playlist.IsEndList;
            }
        }

        /// <summary>
        /// Returns playlist target duration
        /// </summary>
        public TimeSpan TargetDuration
        {
            get
            {
                if (_playlist == null)
                    return TimeSpan.Zero;

                return _playlist.TargetDuration;
            }
        }

        /// <summary>
        /// Bitrate of the stream currently being downloaded
        /// </summary>
        public uint CurrentDownloadBitrate
        {
            get
            {
                return _currentBitrate;
            }
        }
        

        /// <summary>
        /// If a stream download is in progress using HttpWebRequest, then this method will flag 
        /// that request to discard its download stream. After this call, no more data is 
        /// pushed into TSDemux or audio/video buffers. Also no new stream download is started 
        /// after this method is called and until ResumeDownloads is called.
        /// </summary>
        public void AbortStreamDownloads()
        {
            lock (_requestLock)
            {
                _downloadAbortInProgress = true;

                if (_currentWebRequestState != null)
                {
                    _currentWebRequestState.DiscardData = true;
                    _currentWebRequestState.WebRequest.Abort();
                    _currentWebRequestState = null;
                }

                if (null != _asyncResult)
                {
                    _asyncResult.Abort();
                    _asyncResult = null;
                }
            }
        }

        /// <summary>
        /// Returns true if we have a httpWebRequest open request waiting for 
        /// response headers. If current web request is flagged to be discarded 
        /// which can be the case during seek or shutdown, then this method would 
        /// return false.
        /// </summary> 
        public bool IsWebRequestPending()
        {
            lock (_requestLock)
            {
                return (null != _asyncResult && null != _currentWebRequestState && null != _currentWebRequestState.WebRequest);
            }
        }

        /// <summary>
        /// Resumes downloading new streams. See AbortStreamDownloads.
        /// </summary> 
        public void ResumeDownloads()
        {
            lock (_requestLock)
            {
                _downloadAbortInProgress = false;

            }
        }

        /// <summary>
        /// Finds the stream in the current playlist that corresponds to the current seekToTime. 
        /// The search is based on the stream durations specified in EXTINF tag of each stream. 
        /// </summary> 
        internal void FindSeekStream(long seekToTime)
        {
            while (!_playlist.IsLoaded)
                Thread.Sleep(1);

            TimeSpan seekPoint = new TimeSpan(seekToTime);
            TimeSpan cumulativeDuration = new TimeSpan(0, 0, 0, 0);
            bool found = false;
            int streamIndex;
            int a = _playlist.Streams.Count;
            for (streamIndex = 0; streamIndex < _playlist.Streams.Count; ++streamIndex)
            {
                Debug.Assert(seekPoint >= cumulativeDuration, "Seekpoint has passed the cumulativeDuration");
                if ((seekPoint >= cumulativeDuration) && (seekPoint < cumulativeDuration + _playlist.Streams[streamIndex].Duration))
                {
                    found = true;
                    break;
                }

                cumulativeDuration += _playlist.Streams[streamIndex].Duration;
            }
            if (found)
            {
                // We need to set the _currentPosition to point to the stream before the stream 
                // we have found. This is because BeginLoadingNextStream would increment the  
                // _currentPosition before it starts downloading.  
                _currentPosition = streamIndex - 1;
                _currentMediaSequenceNumber = _playlist.Streams[streamIndex].SequenceNumber - 1;
                HLSTrace.WriteLine("FindSeekStream found stream # {0} with index {1}", seekPoint, _currentPosition);
            }
            else
            {
                throw new HLSPlaybackException("Seek Failed!");
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
                    _playlist.Playback = null;
                }
                _disposed = true;
            }
        }
        #endregion

    }

}

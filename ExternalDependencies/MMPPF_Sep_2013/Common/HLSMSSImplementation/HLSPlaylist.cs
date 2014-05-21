using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Browser;
using System.Threading;

namespace Silverlight.Samples.HttpLiveStreaming
{

    /// <summary>
    /// Keys for playlist metadata dictionaries.
    /// </summary>
    public enum HLSPlaylistMetaKeys
    {
        Bandwidth,
        ProgramId,
        Codecs,
        Resolution,
        MediaSequence,
        TargetDuration,
        AllowCache,
        Version,
        EndList
    }

    /// <summary>
    /// Keys for stream metadata dictionaries.
    /// </summary>
    public enum HLSStreamMetaKeys
    {
        Title,
        Duration,
        Discontinuity,
        ProgramDateTime,
        EncryptionMethod,
        EncryptionKeyUri,
        EncryptionIV
    }

    /// <summary>
    /// class for calcuate program datetime
    /// </summary>
    public class ProgramDateTime
    {
        public ProgramDateTime() { }
        public DateTime startTime;
        public DateTime TsStartTime = DateTime.MinValue;
    };

    /// <summary>
    /// class for calcuate program datetime
    /// </summary>
    public class SegmentProgramDateTime
    {
        public TimeSpan offset;
        public ProgramDateTime programTime;
    };
    /// <summary>
    /// Supported encryption methods
    /// </summary>
    public enum HLSEncryptionMethod
    {
        None,
        AES128
    }

    /// <summary>
    /// Interface for getting access to playlist metadata (codecs, resolution, etc.)
    /// without creating full dependency upon HLSPlaylist.
    /// </summary>
    public interface IContainerMetadata
    {
        Dictionary<HLSPlaylistMetaKeys, string> Attributes
        {
            get;
        }
    }

    /// <summary>
    /// Exception for playlist errors.
    /// </summary>
    public class HLSPlaylistException : Exception
    {
        public HLSPlaylistException(string message) :
            base(message)
        {
        }
    }

    /// <summary>
    /// Encapsulates a playable stream referenced by HLS playlist.
    /// </summary>
    public class HLSStream : IEquatable<HLSStream>
    {
        /// <summary>
        /// URI of the stream.
        /// </summary>
        private Uri _uri;

        /// <summary>
        /// Base URI (typically URI of container playlist)
        /// </summary>
        private Uri _baseUri;

        /// <summary>
        /// stream sequence number
        /// </summary>
        private long _sequenceNumber;

        private int _timelineIndex;

        public int TimelineIndex
        {
            get
            {
                return _timelineIndex;
            }
        }

        /// <summary>
        /// Any metadata about the stream collected from playlist files.
        /// </summary>
        public Dictionary<HLSStreamMetaKeys, string> _metadata;


        public SegmentProgramDateTime SegmentProgramTime = null;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="metadata"></param>
        public HLSStream(Uri uri, Uri baseUri, Dictionary<HLSStreamMetaKeys, string> metadata, long sequenceNumber, int timelindeIndex, int size)
        {
            _uri = uri;
            _baseUri = baseUri;
            _metadata = metadata;
            _sequenceNumber = sequenceNumber;
            _timelineIndex = timelindeIndex;
            _size = size;
        }
        
        /// <summary>
        /// URI of the stream.
        /// </summary>
        public Uri Uri
        {
            get
            {
                return _uri;
            }
        }

        public Uri BaseUri
        {
            get
            {
                return _baseUri;
            }
        }

        /// <summary>
        /// Sequence number of the stream 
        /// </summary>
        public long SequenceNumber
        {
            get
            {
                return _sequenceNumber;
            }
        }

        /// <summary>
        /// Size of the stream in bytes. Some specific HLS sources specify each TS segment size using 
        /// an #INF tag in their playlists. We default to 0 if this is not specified in the playlist, 
        /// which is the case for most HLS sources. This variable is used by the heuristics code.
        /// </summary>
        private int _size = 0;

        public int Size
        {
            get
            {
                return _size;
            }
        }

        /// <summary>
        /// Test for equality, used in sliding window implementation.
        /// </summary>
        /// <param name="otherItem"></param>
        /// <returns></returns>
        public bool Equals(HLSStream otherItem)
        {
            return otherItem == this || (otherItem._uri.Equals(_uri) && (otherItem._sequenceNumber == _sequenceNumber));
        }

        /// <summary>
        /// Returns duration of stream in 100ns intervals as indicated by playlist file
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                string duration;
                double seconds = 0 ;
                if (_metadata != null && _metadata.TryGetValue(HLSStreamMetaKeys.Duration, out duration) && double.TryParse(duration, out seconds))
                {
                    return TimeSpan.FromMilliseconds(seconds * ConvertHelper.SecondInMS);
                }
                else
                {
                    HLSTrace.WriteLine("Stream duration tag is missing or is invalid, returning 0");
                    return TimeSpan.FromSeconds(0);
                }
            }
        }

        /// <summary>
        /// All stream metadata parsed from playlist files.
        /// </summary>
        public Dictionary<HLSStreamMetaKeys, string> Attributes
        {
            get
            {
                return _metadata;
            }
        }

        /// <summary>
        /// Returns encryption method used by this media file
        /// </summary>
        public HLSEncryptionMethod EncryptionMethod
        {
            get
            {
                string encryptionMethod;
                if (_metadata != null && _metadata.TryGetValue(HLSStreamMetaKeys.EncryptionMethod, out encryptionMethod))
                {
                    switch (encryptionMethod)
                    {
                        case "NONE":
                            return HLSEncryptionMethod.None;
                        case "AES-128":
                            return HLSEncryptionMethod.AES128;
                        default:
                            throw new NotImplementedException("invalid encryption method");
                    }
                }
                else
                    return HLSEncryptionMethod.None;
            }
        }

        /// <summary>
        /// Returns URI of the key file used for decrypting this media file
        /// </summary>
        public Uri EncryptionKeyUri
        {
            get
            {
                string uriString;
                if (_metadata != null && _metadata.TryGetValue(HLSStreamMetaKeys.EncryptionKeyUri, out uriString))
                    return new Uri(_baseUri, uriString);
                else
                    return null;
            }
        }

        /// <summary>
        /// Returns initialization verctor used for decrypting this media file
        /// </summary>
        public string EncryptionIV
        {
            get
            {
                string ivString;
                if (_metadata != null && _metadata.TryGetValue(HLSStreamMetaKeys.EncryptionIV, out ivString))
                    return ivString;
                else
                    return null;
            }
        }

        /// <summary>
        /// Indicates stream has encoding discontinuity according to playlist metadata
        /// </summary>
        public bool Discontinuity
        {
            get
            {
                return _metadata != null && _metadata.ContainsKey(HLSStreamMetaKeys.Discontinuity);
            }
        }
    }


    /// <summary>
    /// Encapsulates a program in HLS. Each playlist contains one or more programs, and each
    /// program consists of one or more variants, typically with different bitrates.
    /// </summary>
    public class HLSProgram
    {
        /// <summary>
        /// ID of the program as indicated by playlist file.
        /// </summary>
        private string _programId;

        /// <summary>
        /// Variants of the program. Each variant is a separate sliding window playlist file.
        /// </summary>
        private List<HLSVariant> _variants;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="programId"></param>
        public HLSProgram(string programId)
        {
            _programId = programId;
        }

        /// <summary>
        /// Variants of the program, sorted by bitrate in increasing order
        /// </summary>
        internal List<HLSVariant> Variants
        {
            get
            {
                return _variants;
            }
        }

        /// <summary>
        /// Adds a variant to the list of variants.
        /// </summary>
        /// <param name="playlist"></param>
        internal void AddVariant(HLSPlaylistImpl playlist)
        {
            if (playlist is HLSVariant)
            {
                int index = 0;

                if (_variants == null)
                    _variants = new List<HLSVariant>();

                // add in an increasing order sorted by bitrate
                for (index = 0; index < _variants.Count; index++)
                {
                    if (playlist.Bitrate < _variants[index].Bitrate)
                        break;
                }

                _variants.Insert(index, (HLSVariant)playlist);

            }
        }

        public string ProgramId
        {
            get
            {
                return _programId;
            }
        }
    }


    /// <summary>
    /// Internal class representing a parsed playlist.
    /// </summary>
    internal class HLSPlaylistData
    {
        /// <summary>
        /// All sub-playlists referenced by this playlist.
        /// </summary>
        public List<HLSPlaylistImpl> _playlists;

        /// <summary>
        /// All streams referenced by this playlist.
        /// </summary>
        public List<HLSStream> _streams;

        /// <summary>
        /// Playlist duration
        /// </summary>
        private TimeSpan _playlistDuration = TimeSpan.Zero;

        /// <summary>
        /// Accessor for playlist duration
        /// </summary>
        public TimeSpan PlaylistDuration
        {
            get
            {
                return _playlistDuration;
            }
            set
            {
                _playlistDuration = value;
            }
        }

        /// <summary>
        /// Metadata about this playlist.
        /// </summary>
        public Dictionary<HLSPlaylistMetaKeys, string> _metadata;
        
        /// <summary>
        /// Default constructor.
        /// </summary>
        public HLSPlaylistData()
        {
            _playlists = new List<HLSPlaylistImpl>();
            _streams = new List<HLSStream>();
            _metadata = new Dictionary<HLSPlaylistMetaKeys, string>();
        }

        /// <summary>
        /// Media sequence number as indicated by playlist file.
        /// </summary>
        public long MediaSequence
        {
            get
            {
                string mediaSequence;
                if (_metadata != null && _metadata.TryGetValue(HLSPlaylistMetaKeys.MediaSequence, out mediaSequence))
                    return long.Parse(mediaSequence);
                else
                    return 0;
            }
        }

        /// <summary>
        /// Parses sub-playlist metadata.
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        static internal Dictionary<HLSPlaylistMetaKeys, string> ParseArgs(string line)
        {
            Dictionary<HLSPlaylistMetaKeys, string> args = new Dictionary<HLSPlaylistMetaKeys, string>();

            string[] parts = line.Split(new char[] { ',' });

            foreach (string part in parts)
            {
                string[] pair = part.Trim().Split(new char[] { '=' });

                if (pair == null || pair.Length != 2)
                {
                 //   throw new HLSPlaylistException("Invalid Playlist Format");
                    
                }
                switch (pair[0])
                {
                    case "BANDWIDTH":
                        args[HLSPlaylistMetaKeys.Bandwidth] = pair[1];
                        break;
                    case "PROGRAM-ID":
                        args[HLSPlaylistMetaKeys.ProgramId] = pair[1];
                        break;
                    case "CODECS":
                        args[HLSPlaylistMetaKeys.Codecs] = pair[1].Trim(new char[] { '"' });
                        break;
                    case "RESOLUTION":
                        args[HLSPlaylistMetaKeys.Resolution] = pair[1];
                        break;
                }
            }

            return args;
        }

        /// <summary>
        /// Parses stream metadata.
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        static internal Dictionary<HLSStreamMetaKeys, string> ParseStreamArgs(string line)
        {
            Dictionary<HLSStreamMetaKeys, string> args = new Dictionary<HLSStreamMetaKeys, string>();

            string[] parts = line.Split(new char[] { ',' });
            if (parts.Length < 2)
                throw new HLSPlaylistException("Invalid Playlist Format");
            if (!string.IsNullOrEmpty(parts[0]))
                args[HLSStreamMetaKeys.Duration] = parts[0];
            if (!string.IsNullOrEmpty(parts[1]))
                args[HLSStreamMetaKeys.Title] = parts[1];
            return args;
        }

        /// <summary>
        /// Parses EXT-X-KEY metadata.
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        static internal Dictionary<HLSStreamMetaKeys, string> ParseEncryptionArgs(string line)
        {
            Dictionary<HLSStreamMetaKeys, string> args = new Dictionary<HLSStreamMetaKeys, string>();

            int current = 0;
            while (current < line.Length)
            {
                int position = line.IndexOf('=', current);
                if (position < 0)
                    throw new HLSPlaylistException("Invalid Playlist Format");

                string key = line.Substring(current, position - current);
                string value;

                if (line[position + 1] == '"')
                {
                    int end = line.IndexOf('"', position + 2);
                    if (end < 0)
                        throw new HLSPlaylistException("Invalid Playlist Format");

                    value = line.Substring(position + 2, end - position - 2);
                    current = end + 1;
                    // Skip comma to get the next key/value pair
                    if (current < line.Length && line[current] == ',')
                    {
                        current++;
                    }               
                }
                else
                {
                    int end = line.IndexOf(',', position + 1);
                    if (end < 0)
                    {
                        value = line.Substring(position + 1);
                        current = line.Length;
                    }
                    else
                    {
                        value = line.Substring(position + 1, end - position - 1);
                        current = end + 1;
                    }
                }

                switch (key)
                {
                    case "METHOD":
                        args[HLSStreamMetaKeys.EncryptionMethod] = value;
                        break;
                    case "URI":
                        args[HLSStreamMetaKeys.EncryptionKeyUri] = value;
                        break;
                    case "IV":
                        args[HLSStreamMetaKeys.EncryptionIV] = value;
                        break;
                }
            }

            string method;
            if (!args.TryGetValue(HLSStreamMetaKeys.EncryptionMethod, out method) || method == "NONE")
                args = null;

            return args;
        }

        /// <summary>
        /// Extended M3U format signatures.
        /// </summary>
        private const char M3UExtPrefixChar         = '#';
        private const string M3UExtMagic            = "#EXTM3U";
        private const string M3UExtStream           = "#EXTINF:";
        private const string M3UExtPlaylist         = "#EXT-X-STREAM-INF:";
        private const string M3UExtMediaSequence    = "#EXT-X-MEDIA-SEQUENCE:";
        private const string M3UExtTargetDuration   = "#EXT-X-TARGETDURATION:";
        private const string M3UExtAllowCache       = "#EXT-X-ALLOW-CACHE:";
        private const string M3UExtVersion          = "#EXT-X-VERSION:";
        private const string M3UExtProgramDateTime  = "#EXT-X-PROGRAM-DATE-TIME:";
        private const string M3UExtKey              = "#EXT-X-KEY:";
        private const string M3UExtDiscontinuity    = "#EXT-X-DISCONTINUITY";
        private const string M3UExtEndList          = "#EXT-X-ENDLIST";
        private const string M3UExtSize          = "#EXT-X-SIZE";

        /// <summary>
        /// Parses entire playlist file.
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        static internal HLSPlaylistData ParsePlaylist(Uri baseUri, HLSPlaylistImpl source, Stream stream)
        {            
            using (StreamReader reader = new StreamReader(stream))
            {
                string line;

                // A valid playlist should start with an M3UExtMagic tag. Skip over any possible blank 
                // lines and make sure the first non-blank line is the M3UExtMagic tag.
                while (true)
                {
                    line = reader.ReadLine(); 
                    if (line == "")
                        continue;
                    else if (line == M3UExtMagic)
                        break;
                    else
                        throw new HLSPlaylistException("invalid playlist format");
                }

                HLSPlaylistData data = new HLSPlaylistData();

                int size = 0;
                bool discontinuity = false;
                long baseSequenceNumber = 0;
                long streamCount = 0;
                string programDateTime = null;
                Dictionary<HLSStreamMetaKeys, string> streamArgs = null;
                Dictionary<HLSStreamMetaKeys, string> encryptionArgs = null;
                ProgramDateTime programTime = null;
                TimeSpan programOffset = new TimeSpan( 0 );
               
                while ((line = reader.ReadLine()) != null)
                {
                    
                    if (line == "")
                        continue;

                    if (line[0] == M3UExtPrefixChar)
                    {                       
                        if (line.StartsWith(M3UExtStream))              // #EXTINF
                        {
                            streamArgs = ParseStreamArgs(line.Substring(line.IndexOf(':') + 1));
                        }
                        else if (line.StartsWith(M3UExtPlaylist))       // #EXT-X-STREAM-INF
                        {
                            Dictionary<HLSPlaylistMetaKeys, string> args = ParseArgs(line.Substring(line.IndexOf(':') + 1));

                            string uriString = reader.ReadLine();
                            HLSTrace.WriteLine("playlist content: {0}", uriString);

                            if (String.IsNullOrEmpty(uriString))
                                throw new HLSPlaylistException("invalid URI");
                            
                            Uri uri = new Uri(baseUri, uriString);

                            HLSVariant variant = new HLSVariant(uri, args);
                            variant.CookieContainer = source.CookieContainer;
                            variant.CachedEncryptionKeys = source.CachedEncryptionKeys;
                            variant.MSS = source.MSS;
                            data._playlists.Add(variant);
                        }
                        else if (line.StartsWith(M3UExtMediaSequence))  // #EXT-X-MEDIA-SEQUENCE
                        {
                            data._metadata[HLSPlaylistMetaKeys.MediaSequence] = line.Substring(line.IndexOf(':') + 1);

                            if (!long.TryParse(data._metadata[HLSPlaylistMetaKeys.MediaSequence], out baseSequenceNumber))
                                throw new HLSPlaylistException("Invalid Playlist Format: MediaSequence" + data._metadata[HLSPlaylistMetaKeys.MediaSequence] + "is not fromatted as a valid integer.");

                        }
                        else if (line.StartsWith(M3UExtTargetDuration))  // #EXT-X-TARGETDURATION
                        {
                            data._metadata[HLSPlaylistMetaKeys.TargetDuration] = line.Substring(line.IndexOf(':') + 1);
                        }
                        else if (line.StartsWith(M3UExtAllowCache))  // #EXT-X-ALLOW-CACHE
                        {
                            data._metadata[HLSPlaylistMetaKeys.AllowCache] = line.Substring(line.IndexOf(':') + 1);
                        }
                        else if (line.StartsWith(M3UExtVersion))  // #EXT-X-VERSION
                        {
                            data._metadata[HLSPlaylistMetaKeys.Version] = line.Substring(line.IndexOf(':') + 1);
                        }
                        else if (line.StartsWith(M3UExtProgramDateTime))  // #EXT-X-PROGRAM-DATE-TIME
                        {
                            programDateTime = line.Substring(line.IndexOf(':') + 1);
                        }
                        else if (line.StartsWith(M3UExtKey))  // #EXT-X-KEY
                        {
                            encryptionArgs = ParseEncryptionArgs(line.Substring(line.IndexOf(':') + 1));
                        }
                        else if (line == M3UExtEndList)  // #EXT-X-ENDLIST
                        {
                            data._metadata[HLSPlaylistMetaKeys.EndList] = "YES";
                        }
                        else if (line == M3UExtDiscontinuity)  // #EXT-X-DISCONTINUITY
                        {
                            discontinuity = true;
                        }
                        else if (line.StartsWith(M3UExtSize))
                        {
                            size = Int32.Parse(line.Substring(line.IndexOf(':') + 1));
                        }                        
                    }
                    else
                    {
                        Uri uri = new Uri(baseUri, line);
                        if (discontinuity)
                        {
                            streamArgs[HLSStreamMetaKeys.Discontinuity] = "YES";
                            discontinuity = false;
                        }

                        if (!string.IsNullOrEmpty(programDateTime))
                        {
                            streamArgs[HLSStreamMetaKeys.ProgramDateTime] = programDateTime;
                            programTime = new ProgramDateTime();
                            programOffset = new TimeSpan( 0 );
                            programTime.startTime = DateTime.Parse( programDateTime );
                            programDateTime = null;
                        }


                        if (encryptionArgs != null)
                        {
                            foreach (HLSStreamMetaKeys key in encryptionArgs.Keys)
                                streamArgs[key] = encryptionArgs[key];
                        }

                        HLSStream hlsStream = new HLSStream(uri, baseUri, streamArgs, baseSequenceNumber + streamCount, 0, size);
                        if( programTime != null )
                        {
                            hlsStream.SegmentProgramTime = new SegmentProgramDateTime();
                            hlsStream.SegmentProgramTime.programTime = programTime;
                            hlsStream.SegmentProgramTime.offset = programOffset;
                            programOffset += hlsStream.Duration;
                        }

                        data._streams.Add(hlsStream);
                        if (streamArgs.ContainsKey(HLSStreamMetaKeys.Duration))
                        {
                            try
                            {
                                data._playlistDuration += hlsStream.Duration;
                            }
                            catch (OverflowException)
                            {
                                throw new HLSPlaylistException("duration in EXTINF tag is invalid");
                            }
                            catch (FormatException)
                            {
                                throw new HLSPlaylistException("duration in EXTINF tag is invalid");
                            }
                            catch
                            {
                                throw;
                            }
                        }
                        else
                        {
                            Debug.Assert(false, " Duration missing for stream");
                        }
                    
                        ++streamCount;
                        streamArgs = null;
                    }
                }
               
                return data;
            }
        }
    }


    /// <summary>
    /// Encapsulates internal-facing playlist file.
    /// </summary>
    public class HLSPlaylistImpl : IContainerMetadata
    {
        /// <summary>
        /// URI of the playlist file.
        /// </summary>
        protected Uri _uri;

        /// <summary>
        /// Indicates the playlist is currently loading or reloading.
        /// </summary>
        protected bool _isLoading;

        /// <summary>
        /// Number of times we will retry downloading a playlist file after our 
        /// HttpWebRequest has failed with a WebExceptions (e.g. NotFound error).
        /// </summary>
        private readonly int MAX_NUMBER_OF_RETRIES = 3;

        /// <summary>
        /// Number of times we have already retried downloading the current playlist 
        /// but have failed with a WebException
        /// /// </summary>
        private int _reTryCount = 0;

        /// <summary>
        /// Indicates the playlist contains a data.
        /// </summary>
        protected bool _everLoaded;

        /// <summary>
        /// Metadata collected about playlist.
        /// </summary>
        protected Dictionary<HLSPlaylistMetaKeys, string> _metadata;

        /// <summary>
        /// All sub-playlists (typically variants) referenced by this playlist.
        /// </summary>
        protected List<HLSPlaylistImpl> _playlists;

        /// <summary>
        /// All streams referenced by this playlist.
        /// </summary>
        protected List<HLSStream> _streams;

        /// <summary>
        /// Active playback objects attached to the playlist.
        /// </summary>
        protected HLSPlayback _playback;

        /// <summary>
        /// Flag indicating if a new playlist was downloaded in last reload attempt
        /// </summary>
        private bool _playlistChanged = false;
        

        /// <summary>
        /// Number of times we have tried reloading a playlist file and yet have received the same playlist
        /// </summary>
        private int _playlistReloadCount = 0;
        public int PlaylistReloadCount
        {
            get
            {
                return _playlistReloadCount;
            }
        }
        
        /// <summary>
        /// Cache of encryption keys used.
        /// </summary>
        protected Dictionary<Uri, byte[]> _cachedEncryptionKeys;

        /// <summary>
        /// Timestamp of last attempt to reload the playlist.
        /// </summary>
        private DateTime _lastReload;
        public DateTime LastReloadTime 
        {
            get
            {
                return _lastReload;
            }
        }
        /// <summary>
        /// Playlist duration which is the aggregate duration of all streams durations.
        /// </summary>
        private TimeSpan _playlistDuration;

        private HLSMediaStreamSource _mss;

        public HLSMediaStreamSource MSS
        {
            get
            {
                return _mss;
            }
            set
            {
                _mss = value;
            }
        }

        public Uri Uri
        {
            get
            {
                return _uri;
            }
        }

        /// <summary>
        /// Accessor for playlist metadata
        /// </summary>
        public Dictionary<HLSPlaylistMetaKeys, string> MetaData
        {
            get
            {
                return _metadata;
            }
        }

        /// <summary>
        ///  Accessor for playlist duration
        /// </summary>
        public TimeSpan PlaylistDuration
        {
            get
            {
                return _playlistDuration;
            }
            set
            {
                _playlistDuration = value;
            }
        }

        /// <summary>
        /// Returns the playlist target duration tag EXT-X-TARGETDURATION
        /// Throws an exception if target duration was missing from the playlist
        /// </summary>
        public TimeSpan TargetDuration
        {
            get
            {
                string targetDuration;
                double seconds = 0;
                if (_metadata != null && _metadata.TryGetValue(HLSPlaylistMetaKeys.TargetDuration, out targetDuration) && double.TryParse(targetDuration, out seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }
                else
                {
                    throw new HLSPlaylistException("'Target Duration Tag' is missing or is invalid");
                }
            }
        }

        /// <summary>
        /// Returns default playlist reload duration, use last segement duration if has segment, if not use target duration
        /// </summary>
        public TimeSpan DefaultReloadDuration
        {
            get
            {
                if (_streams.Count > 0)
                {
                    return _streams[_streams.Count - 1].Duration;
                }
                else
                {
                    return TargetDuration;
                }
            }
        }

        /// <summary>
        /// Shared cookie container for all HTTP requests
        /// </summary>
        private CookieContainer _cookieContainer;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="metadata"></param>
        internal HLSPlaylistImpl(Uri uri, Dictionary<HLSPlaylistMetaKeys, string> metadata)
        {
            _uri = uri;
            _metadata = metadata;
        }

        /// <summary>
        /// Begins loading playlist from network.
        /// </summary>
        protected void StartLoading()
        {
            if (_isLoading)
                return;
            if (_uri == null)
                return;
            // Accoridng to HLS standarad, the client MUST NOT attempt to reload the Playlist 
            // file more frequently than specified by the standard. The following code block 
            // implements this requirement. 
            DateTime now = DateTime.Now;
            TimeSpan waitTime = new TimeSpan(); 

            if (_everLoaded)
            {
                if (_playlistChanged)
                {
                    // The following logic implements this requirement from HLS standadard: 
                    // " When a client loads a Playlist file for the first time or reloads a
                    //   Playlist file and finds that it has changed since the last time it
                    //   was loaded, the client MUST wait for a period of time before
                    //   attempting to reload the Playlist file again.  This period is called
                    //   the initial minimum reload delay.  It is measured from the time that
                    //   the client began loading the Playlist file.
                    //   The initial minimum reload delay is the duration of the last media
                    //   file in the Playlist.  Media file duration is specified by the EXTINF
                    //   tag. "
                    
                    //   if previously loaded playlist is not the same bitrate stream currently trying to reload,
                    //   use the previous download time as the wait offset. 

                    if (Playback != null && Playback.LastSubPlaylistLoaded != null 
                        && ( Playback.CurrentMediaSequenceNumber > Playback.LastSubPlaylistLoaded.LastMediaSequence ) )

                    {
                        // current playback sequence is large than last reloaded media sequence, 
                        // use latest reloaded playlist info as last reload offset, we know the new sequence will not coming soon
                        TimeSpan waitTimeBeforeReload = Playback.LastSubPlaylistLoaded.DefaultReloadDuration;
                        waitTime = Playback.LastSubPlaylistLoaded.LastReloadTime + waitTimeBeforeReload - now;
                    }
                    else
                    {
                        TimeSpan waitTimeBeforeReload = DefaultReloadDuration;
                        waitTime = _lastReload + waitTimeBeforeReload - now;
                    }

                    // playlist changed, reset reload count. 
                    _playlistReloadCount = 0;
                }
                else
                {
                   // The following logic implements this requirement from HLS standadard: 
                   // " if the client reloads a Playlist file and finds 
                   //   that it has not changed then it MUST wait for a period of time before retrying.
                   //   The minimum delay is a multiple of the target duration.  This multiple is 0.5
                   //   for the first attempt, 1.5 for the second, and 3.0 thereafter. "

                    double[] WaitRatioFromReloadCount = { 0.5, 1.5, 3.0};

                    TimeSpan waitTimeBeforeReload = TimeSpan.FromMilliseconds(TargetDuration.TotalMilliseconds * WaitRatioFromReloadCount[_playlistReloadCount > 2 ? 2 : _playlistReloadCount]);
                    waitTime = _lastReload + waitTimeBeforeReload - now;
                    _playlistReloadCount++;
                }
            }
            else
            {
                waitTime = TimeSpan.FromSeconds(0);
                _playlistReloadCount = 0;
            }

            if (_everLoaded && waitTime.TotalMilliseconds > 0 )
            {
                HLSTrace.WriteLine("Delaying the playlist reload for {0} msec. _playlistReloadCount = {1} ", waitTime.TotalMilliseconds, _playlistReloadCount);
                Thread.Sleep(TimeSpan.FromMilliseconds(waitTime.TotalMilliseconds));
            }
            
            _isLoading = true;
            _lastReload = DateTime.Now;
            _playlistChanged = false;

            HttpWebRequest request;

            HLSTrace.WriteLine("Downloading {0}", _uri.ToString());
            

            if (Environment.OSVersion.Platform == PlatformID.Xbox)
            {
                request = (HttpWebRequest)WebRequestCreator.ClientHttp.Create(_uri);

                // apply special header to disable disk cache for xbox
                request.Headers["x-ms-bypassclientsidecache"] = "1";
            }
            else
            {
                // TODO: Not sure how to disable cache for desktop, we should remove this modifier later on. 

                // Using the wait time to reload the playlist works for most HLS servers, however, it 
                // doesnt work for all. This is a workaround to enforce the servers (and the client 
                // stacks) to not cache the playlist file. We append a fake param with a random value 
                // to the URI of playlist if we are receiving the same playlist file content from the server. 
                HLSTrace.WriteLine("Appending a fakeParam to {0} to enforce no-cache.", _uri.ToString()); 
                request = (HttpWebRequest)WebRequestCreator.ClientHttp.Create(new Uri(_uri.ToString() + "?fakeParam=" + DateTime.Now.Millisecond.ToString()));
            }

            request.CookieContainer = CookieContainer;

            request.Headers[HttpRequestHeader.CacheControl] = "no-cache";

            if (null != _mss && null != _mss.OpenParam && null != _mss.OpenParam.optionalHeaderList)
            {
                foreach (HLSMediaStreamSourceOpenParam.OptionalHeader headerPair in _mss.OpenParam.optionalHeaderList)
                {
                    request.Headers[headerPair.header] = headerPair.value;
                }
            }
            
            request.BeginGetResponse(new AsyncCallback(PlaylistLoadCompleteWorker), request);
        }

        /// <summary>
        /// Called when playlist has been loaded from network.
        /// </summary>
        /// <param name="asynchronousResult"></param>
        private void PlaylistLoadCompleteWorker(IAsyncResult asynchronousResult)
        {
            Stream stream = null;
            HttpWebResponse response = null;

            try
            {
                HttpWebRequest request = (HttpWebRequest)asynchronousResult.AsyncState;

                response = (HttpWebResponse)request.EndGetResponse(asynchronousResult);
                HLSTrace.TestInjectRandomError("PlaylistLoadCompleteWorker", 0.2f);

                HLSTrace.WriteLine("Downloaded {0},  response status {1}", request.RequestUri.ToString(), response.StatusDescription);

                stream = response.GetResponseStream();                
                HLSPlaylistData data = HLSPlaylistData.ParsePlaylist(_uri, this, stream);
               
                if (_everLoaded)
                {
                    // replace current playlist with new playlist data
                    UpdatePlaylistData(data);
                    _isLoading = false;
                }
                else
                {
                    _playlistChanged = true;
                    _streams = data._streams;
                    _playlists = data._playlists;

                    if (data._metadata != null)
                    {
                        if (_metadata == null)
                            _metadata = new Dictionary<HLSPlaylistMetaKeys, string>();

                        foreach (HLSPlaylistMetaKeys key in data._metadata.Keys)
                            _metadata[key] = data._metadata[key];
                    }

                    ParsePrograms();

                    if (this is HLSVariant)
                        _playlistDuration = data.PlaylistDuration;

                    _everLoaded = true;
                    _isLoading = false;
                }

                MoveToCurrentMediaSequence();

                PlaylistLoadEnded();
                _reTryCount = 0;

            }
            catch (Exception e)
            {
                HLSTrace.PrintException(e);

                // always reload live playlist for exceptions if have not reached retry limit.
                // sometime, server returns empty playlist.
                // if ((e is WebException) && (_reTryCount < MAX_NUMBER_OF_RETRIES))
                _isLoading = false;

                if (_reTryCount < MAX_NUMBER_OF_RETRIES)
                {
                    ++_reTryCount;
                    HLSTrace.WriteLine("Reloading: ");
                    Reload();
                    return;
                }
                else
                {
                    _reTryCount = 0;
                    HLSTrace.WriteLine("Report Error: ");
                    PlaylistLoadError(e);
                }
            }
            finally
            {
                if( stream != null )
                    stream.Close();

                if( response != null )
                    response.Close();
            }
        }

        /// <summary>
        /// Overridable handler for parsing additional information from playlist data.
        /// Default implementation does nothing.
        /// </summary>
        protected virtual void ParsePrograms()
        {
        }

        /// <summary>
        /// Overridable for handling any playlist updates. Default implementation
        /// only notifies active playbacks as they need to know when playlist has been
        /// refreshed to start playing.
        /// </summary>
        protected virtual void PlaylistLoadEnded()
        {
            if (_playback != null)
            {
                lock (_playback)
                {
                    _playback.PlaylistLoadEnded();
                }
            }
        }

        /// <summary>
        /// Overridable for handling playlist errors.
        /// </summary>
        /// <param name="exception"></param>
        protected virtual void PlaylistLoadError(Exception exception)
        {
            if (_playback != null)
            {
                _playback.PlaylistLoadError(exception);
            }
        }

        /// <summary>
        /// If playlist sequence number is rollbacked.
        /// </summary>
        static public bool IsSequenceNubmerRollback( long newSequenceNumber, long newSegmentCount, long oldSequenceNumber)
        {
            // if the last qeuencenumber of new playlist is smaller than any old seqeunce number, consider it is rollback. 
            return ( newSequenceNumber + newSegmentCount < oldSequenceNumber);
        }
        
        /// <summary>
        /// If playlist has been changed 
        /// </summary>
        /// <param name="newData"></param>
        private bool PlaylistChanged(HLSPlaylistData newData)
        {
            long oldLastSequenceNumber = 0;
            long newLastSequenceNumber = 0;

            if (newData._playlists != null && newData._playlists.Count > 0)
            {
                throw new NotImplementedException("unexpected playlist format");
            }

            if (_streams.Count > 0)
            {
                oldLastSequenceNumber = _streams[_streams.Count - 1].SequenceNumber;
            }

            if (newData._streams.Count > 0)
            {
                newLastSequenceNumber = newData._streams[newData._streams.Count - 1].SequenceNumber;
            }

            // sequence number could rollback after playback a while. 
            if (newLastSequenceNumber > oldLastSequenceNumber
                || IsSequenceNubmerRollback ( newLastSequenceNumber, newData._streams.Count, oldLastSequenceNumber ) )
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// set playback position according to current media sequence number
        /// </summary>
        /// <param name="newData"></param>
        private void MoveToCurrentMediaSequence()
        {
            if (_playback != null)
            {
                lock (_playback)
                {
                    // reset playback stream position during the current media sequence number
                    _playback.UpdateStreamPosition(_playback.CurrentMediaSequenceNumber);
                }
            }
        }

        /// <summary>
        /// Applies updates to playlists.
        /// </summary>
        /// <param name="newData"></param>
        private void UpdatePlaylistData(HLSPlaylistData newData)
        {

            if (newData._playlists != null && newData._playlists.Count > 0)
            {
                throw new NotImplementedException("unexpected playlist format");
            }


            _playlistChanged = PlaylistChanged( newData );

            if (_playlistChanged)
            {
                Streams.Clear();

                foreach (HLSStream newStream in newData._streams)
                    Streams.Add(newStream);
            }

            if (newData._metadata != null)
            {
                if (_metadata == null)
                    _metadata = new Dictionary<HLSPlaylistMetaKeys, string>();

                foreach (HLSPlaylistMetaKeys key in newData._metadata.Keys)
                    _metadata[key] = newData._metadata[key];

                _metadata[HLSPlaylistMetaKeys.MediaSequence] = _streams[0].SequenceNumber.ToString();
            }
        }

        /// <summary>
        /// All playlist metadata parsed from playlist files.
        /// </summary>
        Dictionary<HLSPlaylistMetaKeys, string> IContainerMetadata.Attributes
        {
            get
            {
                return _metadata;
            }
        }

        /// <summary>
        /// Shared cookie container for all HTTP requests
        /// </summary>
        public CookieContainer CookieContainer
        {
            get
            {
                if (_cookieContainer == null)
                    _cookieContainer = new CookieContainer();
                return _cookieContainer;
            }
            set
            {
                _cookieContainer = value;
            }
        }

        /// <summary>
        /// Cache of encryption keys
        /// </summary>
        public Dictionary<Uri, byte[]> CachedEncryptionKeys
        {
            get
            {
                if (_cachedEncryptionKeys == null)
                    _cachedEncryptionKeys = new Dictionary<Uri, byte[]>();
                return _cachedEncryptionKeys;
            }
            set
            {
                _cachedEncryptionKeys = value;
            }
        }

        /// <summary>
        /// Program ID of this playlist if it is a variant of superplaylist. Otherwise, null.
        /// </summary>
        public string ProgramId
        {
            get
            {
                string programId;
                if (_metadata != null && _metadata.TryGetValue(HLSPlaylistMetaKeys.ProgramId, out programId))
                    return programId;
                else
                    return "";
            }
        }

        /// <summary>
        /// Bitrate of this playlist if it is a variant of superplaylist. Otherwise, null.
        /// </summary>
        public uint Bitrate
        {
            get
            {
                string bandwidth;
                if (_metadata != null && _metadata.TryGetValue(HLSPlaylistMetaKeys.Bandwidth, out bandwidth))
                    return uint.Parse(bandwidth);
                else
                    return 0;
            }
        }

        /// <summary>
        /// Media sequence number of this playlist.
        /// </summary>
        public long MediaSequence
        {
            get
            {
                string mediaSequence;
                if (_metadata != null && _metadata.TryGetValue(HLSPlaylistMetaKeys.MediaSequence, out mediaSequence))
                    return long.Parse(mediaSequence);
                else
                    return 0;
            }
        }

        /// <summary>
        /// Last media sequence number of this playlist.
        /// </summary>
        public long LastMediaSequence
        {
            get
            {
                if (_streams.Count > 0)
                {
                    return _streams[_streams.Count - 1].SequenceNumber;
                }
                else
                {
                    return MediaSequence;
                }
            }
        }
        /// <summary>
        /// Returns true for normal playlists, false for sliding window playlists
        /// </summary>
        public bool IsEndList
        {
            get
            {
                string value;
                return _metadata.TryGetValue(HLSPlaylistMetaKeys.EndList, out value) && value == "YES";
            }
        }

        /// <summary>
        /// Begins loading the playlist from network, unless it's already loaded.
        /// </summary>
        public void Load()
        {
            if (!_everLoaded)
                StartLoading();
        }

        /// <summary>
        /// Returns true if the playlist contains valid data, i.e. has ever been loaded.
        /// </summary>
        public bool IsLoaded
        {
            get
            {
                return _everLoaded;
            }
        }

        public void SetLoaded()
        {
            if (!_everLoaded && !_isLoading)
            {
                _everLoaded = true;
            }
        }

        /// <summary>
        /// Contains all active playback objects connected to this playlist.
        /// </summary>
        public HLSPlayback Playback
        {
            get
            {
                return _playback;
            }
            set
            {
                _playback = value;
            }
        }

        /// <summary>
        /// All streams referenced by this playlist. This list shifts with every 
        /// update of sliding window.
        /// </summary>
        public List<HLSStream> Streams
        {
            get
            {
                return _streams;
            }
        }

        /// <summary>
        /// Returns true if is allowed now to start refreshing the playlist per
        /// HTTP Live Streaming spec.
        /// </summary>
        public bool IsDueForReload
        {
            get
            {
                if (!_everLoaded)
                    return true;

                if (IsEndList)
                    return false;

                TimeSpan duration = LastStreamDuration;
                if (duration.TotalMilliseconds == 0)
                    duration = new TimeSpan(100000000);

                return _lastReload + duration < DateTime.Now;
            }
        }

        /// <summary>
        /// Helps with computing duration of last stream.
        /// </summary>
        internal TimeSpan LastStreamDuration
        {
            get
            {
                if (_streams != null && _streams.Count > 0)
                    return _streams[_streams.Count - 1].Duration;
                else
                    return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Starts refreshing the playlist.
        /// </summary>
        public void Reload()
        {
            if (!_isLoading)
                StartLoading();
        }

        /// <summary>
        /// Starts loading of decryption key for the stream. If key is already loaded
        /// or not needed (stream is not encrypted), returns true. Otherwise, false.
        /// </summary>
        /// <returns></returns>
        public bool LoadEncryptionKeyForStream(HLSStream stream)
        {
            if (stream.EncryptionMethod == HLSEncryptionMethod.None)
                return true;

            Uri uri = stream.EncryptionKeyUri;
            if (uri == null)
                return true;

            byte[] key;
            if (_cachedEncryptionKeys != null && _cachedEncryptionKeys.TryGetValue(uri, out key))
                return true;

            HLSTrace.WriteLine("Downloading encryption key {0}", uri.ToString());

            HttpWebRequest request = (HttpWebRequest)WebRequestCreator.ClientHttp.Create(uri);
            request.CookieContainer = CookieContainer;
            request.Headers["x-ms-bypassclientsidecache"] = "1";
            request.BeginGetResponse(new AsyncCallback(EncryptionKeyLoadCompleteWorker), request);
            return false;
        }

        /// <summary>
        /// Called when encryption key file has been loaded from network.
        /// </summary>
        /// <param name="asynchronousResult"></param>
        private void EncryptionKeyLoadCompleteWorker(IAsyncResult asynchronousResult)
        {
            HttpWebRequest request = (HttpWebRequest)asynchronousResult.AsyncState;
            Uri uri = request.RequestUri;
            HttpWebResponse response = null;
            Stream stream = null;
            try
            {
                response = (HttpWebResponse)request.EndGetResponse(asynchronousResult);
                HLSTrace.TestInjectRandomError("EncryptionKeyLoadCompleteWorker", 0.2f);

                HLSTrace.WriteLine("Downloaded {0},  response status {1}", uri.ToString(), response.StatusDescription);

                stream = response.GetResponseStream();
                int keySize = (int)stream.Length;
                if (keySize == 0)
                    throw new InvalidOperationException("invalid encryption key size");

                byte[] keyBytes = new byte[keySize];

                int offset = 0;
                int bytesToRead = keySize;
                while (bytesToRead > 0)
                {
                    int bytesRead = stream.Read(keyBytes, offset, bytesToRead);
                    if (bytesRead == 0)
                        throw new InvalidOperationException("invalid encryption key size");
                    offset += bytesRead;
                    bytesToRead -= bytesRead;
                }

                if (_cachedEncryptionKeys == null)
                    _cachedEncryptionKeys = new Dictionary<Uri, byte[]>();
                _cachedEncryptionKeys[uri] = keyBytes;

                if (_playback != null)
                {
                    _playback.EncryptionKeyLoadEnded(uri);
                }

            }
            catch (Exception exception)
            {
                HLSTrace.PrintException(exception);
                if (_playback != null)
                {
                    _playback.EncryptionKeyLoadError(uri, exception);
                }
            }
            finally
            {
                if (stream != null ) 
                    stream.Close();

                if(response != null)
                    response.Close();
            }
        }

        /// <summary>
        /// Returns cached encryption key for the stream, if any.
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public byte[] GetEncryptionKeyForStream(HLSStream stream)
        {
            if (stream.EncryptionMethod == HLSEncryptionMethod.None)
                return null;

            Uri uri = stream.EncryptionKeyUri;
            if (uri == null)
                return null;

            byte[] key;
            if (_cachedEncryptionKeys != null && _cachedEncryptionKeys.TryGetValue(uri, out key))
                return key;
            else
                return null;
        }

        public void AddStream(HLSStream hlsStream)
        {
            if (null == _streams)
            {
                _streams = new List<HLSStream>();
            }
            _streams.Add(hlsStream);
        }
    }

    /// <summary>
    /// Encapsulate variant playlist
    /// </summary>
    public class HLSVariant : HLSPlaylistImpl
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="baseUri"></param>
        /// <param name="metadata"></param>
        public HLSVariant(Uri uri, Dictionary<HLSPlaylistMetaKeys, string> metadata)
            : base(uri, metadata)
        {
        }
    }

    /// <summary>
    /// Implements user facing playlist functionality.
    /// </summary>
    public class HLSPlaylist : HLSVariant
    {
        /// <summary>
        /// All programs referenced by this playlist. If the playlist mentions no
        /// programs, contains a single default program with ID = ""
        /// </summary>
        protected Dictionary<string, HLSProgram> _programs;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="uri"></param>
        public HLSPlaylist(Uri uri)
            : base(uri, null)
        {
        }

        /// <summary>
        /// Delegate for PlaylistReady event.
        /// </summary>
        public delegate void PlaylistReadyEvent(object sender);

        /// <summary>
        /// Delegate for PlaylistError event.
        /// </summary>
        public delegate void PlaylistErrorEvent(object sender, Exception exception);

        /// <summary>
        /// PlaylistReady event which fires once per playlist lifetime
        /// when the playlist has been loaded and ready to play.
        /// </summary>
        private PlaylistReadyEvent _playlistReady;

        /// <summary>
        /// PlaylistError event which fires when playlist error occurs.
        /// </summary>
        private PlaylistErrorEvent _playlistError;

        /// <summary>
        /// Indicates that either PlaylistReady or PlaylistError has fired already.
        /// </summary>
        private bool _playlistReadyOrErrorCalledOnce;

        /// <summary>
        /// PlaylistReady event which fires once per playlist lifetime
        /// when the playlist has been loaded and ready to play.
        /// </summary>
        public PlaylistReadyEvent PlaylistReady
        {
            get
            {
                return _playlistReady;
            }
            set
            {
                _playlistReady = value;
            }
        }

        /// <summary>
        /// PlaylistReady event which fires once per playlist lifetime
        /// when the playlist has been loaded and ready to play.
        /// </summary>
        public PlaylistErrorEvent PlaylistError
        {
            get
            {
                return _playlistError;
            }
            set
            {
                _playlistError = value;
            }
        }

        /// <summary>
        /// Override that fires PlaylistReady event when playlist is
        /// fully loaded and ready to play.
        /// </summary>
        protected override void PlaylistLoadEnded()
        {
            if (!_playlistReadyOrErrorCalledOnce)
            {
                _playlistReadyOrErrorCalledOnce = true;
                if (_playlistReady != null)
                    _playlistReady(this);
            }

            base.PlaylistLoadEnded();

        }

        /// <summary>
        /// Override that fires PlaylistError event in case of an error.
        /// </summary>
        /// <param name="exception"></param>
        protected override void PlaylistLoadError(Exception exception)
        {
            base.PlaylistLoadError(exception);

            if (!_playlistReadyOrErrorCalledOnce)
            {
                _playlistReadyOrErrorCalledOnce = true;
                if (_playlistError != null)
                    _playlistError(this, exception);
            }
        }

        /// <summary>
        /// Override that builds a list of programs out of available playlist data.
        /// </summary>
        protected override void ParsePrograms()
        {
            _programs = new Dictionary<string, HLSProgram>();

            foreach (HLSPlaylistImpl playlist in _playlists)
            {
                HLSProgram program;
                if (!_programs.TryGetValue(playlist.ProgramId, out program))
                {
                    program = new HLSProgram(playlist.ProgramId);
                    _programs[playlist.ProgramId] = program;
                }
                program.AddVariant(playlist);
            }

            if (_programs.Count == 0)
            {
                string defaultProgramId = "";
                HLSProgram program = new HLSProgram(defaultProgramId);
                program.AddVariant(this);
                _programs[defaultProgramId] = program;
            }
        }

        /// <summary>
        /// All programs referenced by this playlist. If the playlist mentions no
        /// programs, contains a single default program with ID = ""
        /// </summary>
        public List<HLSProgram> Programs
        {
            get
            {
                if (!_everLoaded || _programs == null)
                    return null;

                List<HLSProgram> programs = new List<HLSProgram>();
                foreach (HLSProgram program in _programs.Values)
                    programs.Add(program);
                return programs;
            }
        }


        public void FromHLSExternalPlaylist(HLSExternalPlayList playlistExternal)
        {
            _programs.Clear();
            foreach (HLSPresentation hlsPresentation in playlistExternal.listOfPresentation)
            {
                HLSProgram hlsProgram = new HLSProgram(hlsPresentation.programId);
                _programs[hlsPresentation.programId] = hlsProgram;
                TimeSpan minimalDuration = new TimeSpan(Int64.MaxValue);

                foreach (HLSSegmentList segmentList in hlsPresentation.listOfSegmentList)
                {
                    HLSVariant hlsVariant = new HLSVariant(segmentList.uri, segmentList.metaData);
                    hlsVariant.SetLoaded();
                    hlsProgram.AddVariant(hlsVariant);
                    TimeSpan viriantDuration = TimeSpan.Zero;

                    if (0 == segmentList.listOfSegment.Count)
                    {
                        throw new System.NotSupportedException("No segment in segment list.");
                    }

                    foreach (HLSSegment hlsSegment in segmentList.listOfSegment)
                    {
                        HLSStream hlsStream = new HLSStream(hlsSegment.uri, hlsSegment.baseUri, hlsSegment.metaData, hlsSegment.sequenceNumber, hlsSegment.timelineIndex, hlsSegment.segmentSize);
                        hlsVariant.AddStream(hlsStream);
                        viriantDuration += hlsStream.Duration;
                    }
                    hlsVariant.PlaylistDuration = viriantDuration;
                    if (null == viriantDuration || viriantDuration < minimalDuration)
                    {
                        minimalDuration = viriantDuration;
                    }
                }

                PlaylistDuration = minimalDuration;
            }
        }
    }

    public class HLSSegment
    {
        /// <summary>
        /// URI of the stream.
        /// </summary>
        public Uri uri;

        public Uri baseUri;

        /// <summary>
        /// stream sequence number
        /// </summary>
        public long sequenceNumber;

        /// <summary>
        /// Duration of segment in hundred-nano seconds
        /// </summary>
        public TimeSpan duration;

        public int segmentSize;

        public int timelineIndex;

        /// <summary>
        /// Any metadata about the stream collected from playlist files.
        /// </summary>
        public Dictionary<HLSStreamMetaKeys, string> metaData;
    }

    public class HLSSegmentList
    {
        public Uri uri;
        public uint bitRate; 
        public Dictionary<HLSPlaylistMetaKeys, string> metaData;
        public List<HLSSegment> listOfSegment;
    }

    public class HLSPresentation
    {
        public string programId;
        public List<HLSSegmentList> listOfSegmentList;
    }

    /// <summary>
    /// 
    /// </summary>
    public class HLSExternalPlayList
    {
        public List<HLSPresentation> listOfPresentation;
        public List<TimelineEstablishInfo> timelineEstablishInfoList;
    }

    public class TimelineEstablishInfo
    {
        public TimeSpan timelineStartOffset;
        public bool isMonoIncrease;
        public TimelineEstablishInfo(TimeSpan timelineStartOffset, bool isMonoIncrease)
        {
            this.timelineStartOffset = timelineStartOffset;
            this.isMonoIncrease = isMonoIncrease;
        }
    }

    public class HLSExternalPlayListImpl : HLSExternalPlayList
    {
        internal void FromHLSPlaylist(HLSPlaylist playlistInternal)
        {
            Debug.Assert(playlistInternal.IsLoaded, " cannot generate HLSMainPlayListImpl from unloaded HLSPlaylist");

            listOfPresentation = new List<HLSPresentation>();
            List<HLSProgram> programList = playlistInternal.Programs;
            foreach (HLSProgram hlsProgram in programList)
            {
                HLSPresentation presentation = new HLSPresentation();
                presentation.listOfSegmentList = new List<HLSSegmentList>();
                presentation.programId = hlsProgram.ProgramId;
                listOfPresentation.Add(presentation);

                List<HLSVariant> variantList = hlsProgram.Variants;
                foreach (HLSVariant hlsVariant in variantList)
                {
                    HLSSegmentList segmentList = new HLSSegmentList();
                    segmentList.listOfSegment = new List<HLSSegment>();
                    segmentList.uri = hlsVariant.Uri;
                    segmentList.bitRate = hlsVariant.Bitrate;
                    segmentList.metaData = hlsVariant.MetaData;
                    presentation.listOfSegmentList.Add(segmentList);

                    List<HLSStream> streamList = hlsVariant.Streams;
                    foreach (HLSStream hlsStream in streamList)
                    {
                        HLSSegment hlsSegment = new HLSSegment();
                        hlsSegment.uri = hlsStream.Uri;
                        hlsSegment.baseUri = hlsStream.BaseUri;
                        hlsSegment.sequenceNumber = hlsStream.SequenceNumber;
                        hlsSegment.duration = hlsStream.Duration;
                        hlsSegment.segmentSize = hlsStream.Size;
                        hlsSegment.metaData = hlsStream._metadata;
                        segmentList.listOfSegment.Add(hlsSegment);
                    }
                }
            }
        }
    }
}

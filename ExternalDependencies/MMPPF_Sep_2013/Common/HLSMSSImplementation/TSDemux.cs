using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Silverlight.Samples.HttpLiveStreaming
{

    /// <summary>
    /// Exception for problems with MPEG-TS demuxing.
    /// </summary>
    public class TSDemuxException : Exception
    {
        public TSDemuxException(string message) :
            base(message)
        {
        }
    }

    /// <summary>
    /// Encapsulates Packetized Elementary Stream.
    /// </summary>
    internal class TSStream
    {
        /// <summary>
        /// Enumeration that specifies the type of stream.
        /// </summary>
        public enum StreamContetType
        {
            Audio = 0,
            Video = 1,
            Unknown = 2
        }

        /// <summary>
        /// Packet ID
        /// </summary>
        private uint _pid;

        /// <summary>
        /// Payload processor.
        /// </summary>
        private PESParser _parser;

        /// <summary>
        /// The type of the stream 
        /// </summary>
        private StreamContetType _streamType = StreamContetType.Unknown;

        /// <summary>
        /// Continuity counter of the next TS packet to be parsed. This is used to make sure that the 
        /// TS packets we are receiving are in the right order. 
        /// </summary>
        private int _packetsContinuityCounter = -1;

        /// <summary>
        /// The type of the stream 
        /// </summary>
        public StreamContetType StreamType
        {
            get
            {
                return _streamType;
            }
            set
            {
                _streamType = value;
            }
        }

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="pid"></param>
        public TSStream(uint pid, StreamContetType streamType)
        {
            _pid = pid;
            _streamType = streamType;
        }

        /// <summary>
        /// Pushes a chunk of data down the pipeline.
        /// </summary>
        public void ParseData(byte[] data, int offset, int count, int cc)
        {
            if (_parser != null)
            {
                // Check continuity counter of the new packet 
                if (_packetsContinuityCounter != -1 && _packetsContinuityCounter != cc)
                {
                    HLSTrace.WriteLine("pid 0x{0:X4}: cc=0x{1:X} (expecting 0x{2:X})", _pid, cc, _packetsContinuityCounter);
                    Flush(true);
                }

                _parser.ParseData(data, offset, count);

                // The TS packets cotinuity counters are only 4 bits
                _packetsContinuityCounter = (cc + 1) % 0x10;
            }
        }

        /// <summary>
        /// Flushes any buffers down the pipeline.
        /// </summary>
        public void Flush(bool discontinuity)
        {
            if (_parser != null)
            {
                _packetsContinuityCounter = -1;
                _parser.Flush(discontinuity);
            }
        }

        /// <summary>
        /// Packet ID
        /// </summary>
        public uint Pid
        {
            get
            {
                return _pid;
            }
        }

        /// <summary>
        /// Payload processor.
        /// </summary>
        public PESParser Parser
        {
            get
            {
                return _parser;
            }
            set
            {
                _parser = value;
            }
        }
    }

    /// <summary>
    /// Encapsulates MPEG-TS program.
    /// </summary>
    internal class TSProgram
    {
        /// <summary>
        /// Packetized elementary streams in this program.
        /// </summary>
        private List<TSStream> _streams;

        /// <summary>
        /// PID of this program's PMT
        /// </summary>
        private uint _programPid;

        /// <summary>
        /// Program number
        /// </summary>
        private uint _programNumber;

        /// <summary>
        /// Indicates that PMT has been already parsed.
        /// </summary>
        private bool _parsedPMT;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="programPid"></param>
        /// <param name="programNumber"></param>
        public TSProgram(uint programPid, uint programNumber)
        {
            _programPid = programPid;
            _programNumber = programNumber;
        }

        /// <summary>
        /// Adds elementary stream to this program.
        /// </summary>
        /// <param name="stream"></param>
        public void AddStream(TSStream stream)
        {
            if (_streams == null)
                _streams = new List<TSStream>();
            _streams.Add(stream);
        }

        /// <summary>
        /// Indicates that PMT has been already parsed.
        /// </summary>
        public bool ParsedPMT
        {
            get
            {
                return _parsedPMT;
            }
            set
            {
                _parsedPMT = value;
            }
        }
    }

    internal class TSPacket
    {
        private const uint INVALID_PID = 0xFFFF;    //Invalid PID
        private const uint PAT_PID = 0x0000;        //Program Association Table
        private const uint CAT_PID = 0x0001;        //Conditional Access Table
        private const uint TSDT_PID = 0x0002;       //Transport Stream Description Table
        private const uint NIT_PID = 0x0010;        //Network Information Table
        private const uint SDT_BAT_PID = 0x0011;    //Service Description Table/Boquet association table.
        private const uint EIT_PID = 0x0012;        //Event Information Table
        private const uint RST_PID = 0x0013;        //Running Status Table
        private const uint TDT_PID = 0x0014;        //Time and Date Table
        private const uint MIP_PID = 0x0015;        //Mega-frame initialization Packet 
        private const uint NULLPACKET_PID = 0x1FFF; //Null packet
        private const uint ADS_PID = 0x03F3;        //Ads maker packet

        public uint _transport_error_indicator;
        public uint _payload_unit_start_indicator;
        public uint _transport_priority;
        public uint _pid;
        public uint _transport_scrambling_control;
        public uint _adaptation_field_control;
        public uint _continuity_counter;
        public uint _section_length = 0;

        public byte[] _adaptation_field;
        public bool _discontinuity;
        public int _headerSize;

        private BitstreamReader _bitstream;

        public TSPacket(byte[] byteBuffer)
        {
            _bitstream = new BitstreamReader();
            _bitstream.Init(byteBuffer, 0);

            uint magic = _bitstream.ReadUBits(8);
            if (magic != 0x47)
                throw new TSDemuxException("sync bits not found");

            _transport_error_indicator = _bitstream.ReadUBits(1);
            _payload_unit_start_indicator = _bitstream.ReadUBits(1);
            _transport_priority = _bitstream.ReadUBits(1);
            _pid = _bitstream.ReadUBits(13);
            _transport_scrambling_control = _bitstream.ReadUBits(2);
            _adaptation_field_control = _bitstream.ReadUBits(2);
            _continuity_counter = _bitstream.ReadUBits(4);

            _headerSize = 4;

            if (!IsValidPID())
            {
                throw new TSDemuxException("Invalid TS packet PID!");
            }

            if (HasAdaptationField())
            {
                uint adaptationLength = _bitstream.ReadUBits(8);
                if (adaptationLength > 0)
                {
                    _adaptation_field = new byte[adaptationLength];

                    for (int i = 0; i < adaptationLength; i++)
                    {
                        _adaptation_field[i] = _bitstream.ReadByte();
                    }

                    _discontinuity = ((_adaptation_field[0] & 0x80) != 0x00);
                }

                _headerSize += ((int)adaptationLength + 1);
            }
        }

        public bool HasPayload()
        {
            return ((_adaptation_field_control & 0x01) == 0x01) && (_transport_scrambling_control == 0x00);
        }

        public bool HasAdaptationField()
        {
            return (_adaptation_field_control & 0x02) == 0x02;
        }

        public bool IsValidPID()
        {
            // Reserved PID value
            if (_pid >= 0x0003 && _pid <= 0x000F)
                return false;

            return true;
        }

        bool IsPSIPacket(int pid)
        {
            // Oalid PSI table PID
            if ((pid != INVALID_PID) && (pid == PAT_PID || pid == CAT_PID || pid == TSDT_PID || (pid >= NIT_PID && pid <= MIP_PID) /*|| pid == m_pidPMT*/))
                return true;

            return false;
        }

        /// <summary>
        /// Parses PAT structure.
        /// </summary>
        /// <param name="br"></param>
        public void ParsePAT(ref Dictionary<uint, TSProgram> programs)
        {
            if (_payload_unit_start_indicator != 1)
            {
                throw new NotImplementedException("PAT spanning multiple packets");
            }

            _bitstream.SkipBits(8);
            _headerSize++;

            _bitstream.SkipBits(12);
            uint section_length = _bitstream.ReadUBits(12);
            _bitstream.SkipBits(18);
            uint ver = _bitstream.ReadUBits(5);
            _bitstream.SkipBits(17);
            section_length -= 9;
            int count = (int)section_length / 4;
            int residul = (int)section_length % 4;
            for (int i = 0; i < count; i++)
            {
                uint programNumber = _bitstream.ReadUBits(16);
                _bitstream.SkipBits(3);
                if (programNumber != 0)
                {
                    uint programPid = _bitstream.ReadUBits(13);
                    programs[programPid] = new TSProgram(programPid, programNumber);
                }
                else
                {
                    _bitstream.SkipBits(13);
                }
            }
        }

        /// <summary>
        /// Parses PMT structure.
        /// </summary>
        /// <param name="program"></param>
        /// <param name="br"></param>
        public void ParsePMTHeader()
        {
            _bitstream.SkipBits(8);
            if (_payload_unit_start_indicator != 1)
            {
                throw new NotImplementedException("PMT spanning multiple packets");
            }

            _bitstream.SkipBits(12);
            _section_length = _bitstream.ReadUBits(12);
            uint programNumber = _bitstream.ReadUBits(16);
            _bitstream.SkipBits(2);
            uint ver = _bitstream.ReadUBits(5);

            _bitstream.SkipBits(37);
            uint program_info_length = _bitstream.ReadUBits(12);
            _bitstream.SkipBits((int)program_info_length * 8);
            _section_length -= (9 + program_info_length);
        }

        public bool ParseNextPMTStream(ref uint streamTypeCode, ref uint elementary_PID, ref uint ES_info_length)
        {
            if (_section_length < 9)
                return false;

            streamTypeCode = _bitstream.ReadUBits(8);
            _bitstream.SkipBits(3);
            elementary_PID = _bitstream.ReadUBits(13);
            _bitstream.SkipBits(4);
            ES_info_length = _bitstream.ReadUBits(12);
            _bitstream.SkipBits((int)ES_info_length * 8);
            _section_length -= (5 + ES_info_length);
            return true;
        }
    }

    /// <summary>
    /// Encapsulates MPEG-TS demuxer.
    /// </summary>
    public class TSDemux : IDisposable
    {

        private const uint AVC_STREAM_TYPE_CODE = 0x1B;

        private const uint AAC_STREAM_TYPE_CODE = 0x0F;

        private const uint DDPLUS_STREAM_TYPE_CODE = 0x84;

        /// <summary>
        /// Input stream - file or internet.
        /// </summary>
        private EncryptedStream _stream;

        /// <summary>
        /// Flag indicating end of input stream reached.
        /// </summary>
        private bool _isEndOfStream;

        /// <summary>
        /// Output buffer for audio samples.
        /// </summary>
        private SampleBuffer _audioBuffer;

        /// <summary>
        /// Output buffer for video samples.
        /// </summary>
        private SampleBuffer _videoBuffer;

        /// <summary>
        /// Accessor for playlist metadata
        /// </summary>
        private IContainerMetadata _metadata;

        /// <summary>
        /// Video sample producer.
        /// </summary>
        private MediaFormatParser _videoFormatParser;

        /// <summary>
        /// Audio sample producer.
        /// </summary>
        private MediaFormatParser _audioFormatParser;

        private MediaFormatParser _nullFormatParser;

        /// <summary>
        /// List of all programs in MPEG-2 TS
        /// </summary>
        private Dictionary<uint, TSProgram> _programs;

        /// <summary>
        /// List of all stream in MPEG-2 TS
        /// </summary>
        private Dictionary<uint, TSStream> _streams;

        /// <summary>
        /// Indicates that PAT is already parsed.
        /// </summary>
        private bool _parsedPAT;

        /// <summary>
        /// Track whether Dispose has been called
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Bandwidth history
        /// </summary>
        private BandwidthHistory _BWHistory;

        /// <summary>
        /// MPEG-TS frame size, which is 188 bytes.
        /// </summary>
        public const int TSPacketSize = 188;
        
        /// <summary>
        /// Buffer to hold the TS pakcet that the class is parisng.
        /// </summary>
        private byte[] _TSPacketBuffer;

        /// <summary>
        /// The size of data we attempt to read from network stream while doing 
        /// the bandwidth measurment. This is currently set to 64K chunk size, 
        /// aligned to hold a fixed number of TSPackets. This means in each call 
        /// to ReadChunk method, we would read 64K from the network stream while 
        /// calculating the bandwidth, and then parse and push the TS packets in
        /// this chunk down the pipeline. 
        /// </summary>
        private const int _downloadChunkSize = ( 65536 / TSPacketSize) * TSPacketSize;

        /// <summary>
        /// Buffer to hold the chunk of data being downloaded. 
        /// </summary>
        private byte[] _downloadChunkBuffer;

        /// <summary>
        /// Time when previous readchunk completed 
        /// </summary>
        private DateTime _previousReadEndTime;

        /// <summary>
        ///  Default constructor.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="audioBuffer"></param>
        /// <param name="videoBuffer"></param>
        public TSDemux(EncryptedStream stream, SampleBuffer audioBuffer,
                       SampleBuffer videoBuffer, IContainerMetadata metadata, uint bitrate, 
                       BandwidthHistory BWHistory)
        {
            CommonConstruct(stream, audioBuffer, videoBuffer, metadata, bitrate, BWHistory);
        }

        /// <summary>
        /// Bitrate of the stream currently being demuxed
        /// </summary>
        private uint _bitrate;

        /// <summary>
        /// Special constructor for creating new demux for next stream in sequence
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="previousDemux"></param>
        public TSDemux(EncryptedStream stream, TSDemux previousDemux, IContainerMetadata metadata, uint bitrate, BandwidthHistory BWHistory)
        {
            if (previousDemux == null)
                throw new ArgumentNullException("previousDemux");

            CommonConstruct(stream, previousDemux._audioBuffer, previousDemux._videoBuffer, metadata, bitrate, BWHistory);

            if (!stream.Discontinuity)
                _streams = previousDemux._streams;

            _audioFormatParser = previousDemux._audioFormatParser;
            _videoFormatParser = previousDemux._videoFormatParser;
            _audioFormatParser.HLSStream = stream.HLSStream;
            _videoFormatParser.HLSStream = stream.HLSStream;
        }

        /// <summary>
        /// Common construction code for all constructors
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="audioBuffer"></param>
        /// <param name="videoBuffer"></param>
        private void CommonConstruct(EncryptedStream stream, SampleBuffer audioBuffer,
                                    SampleBuffer videoBuffer, IContainerMetadata metadata, uint bitrate,
                                    BandwidthHistory BWHistory)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");
            _stream = stream;
            _metadata = metadata;
            _audioBuffer = audioBuffer;
            _audioBuffer.ResetOnSegmentStart();
            _videoBuffer = videoBuffer;
            _videoBuffer.ResetOnSegmentStart();

            _previousReadEndTime = stream.RequestStartTime;

            _bitrate = bitrate;

            _downloadChunkBuffer = new byte[_downloadChunkSize];
            _TSPacketBuffer = new byte[TSPacketSize];

            _BWHistory = BWHistory;
        }


        /// <summary>
        /// Indicates end of input stream reached.
        /// </summary>
        public bool IsEndOfStream
        {
            get
            {
                return _isEndOfStream;
            }
            set
            {
                _isEndOfStream = value;
            }
        }

        /// <summary>
        /// Indicates payload media information has been extracted from 
        /// all packetized elementary streams, such as AAC profile, sampling rate, etc.
        /// </summary>
        public bool IsMediaInfoReady
        {
            get
            {
                return _audioFormatParser != null && _audioFormatParser.MediaInfoReady &&
                       _videoFormatParser != null && _videoFormatParser.MediaInfoReady;
            }
        }

        /// <summary>
        /// Reads a chunk of data with size _chunkSize, parses it as TS packets, and pushes 
        /// the TS packet payload down the pipeline.
        /// </summary>
        public void ReadChunk()
        {
            int bytesToRead = _downloadChunkSize;
            int bytesRead = 0;
                 
            while (bytesToRead > 0 && !_isEndOfStream &&!_disposed )
            {
                int read = _stream.Read(_downloadChunkBuffer, bytesRead, bytesToRead);
                if (read == 0)
                {
                    _isEndOfStream = true;
                }

                bytesToRead -= read;
                bytesRead += read;
            }

            lock (this)
            {
                for (int i = 0; i < bytesRead / TSPacketSize && !_disposed; i++)
                {
                    Array.Copy(_downloadChunkBuffer, i * TSPacketSize, _TSPacketBuffer, 0, TSPacketSize);
                    ParsePacket(_TSPacketBuffer);
                }
            }

            if ( bytesRead > 0 )
            {
                TimeSpan readTime = DateTime.Now - _previousReadEndTime;
                _previousReadEndTime = DateTime.Now;
                _BWHistory.AddMeasurement(bytesRead, readTime);

                if (_isEndOfStream)
                {
                    HLSTrace.WriteLine("Download completed: {0}.\n Took {1} seconds, average bandwidth={2} bps  audio:={3}~{4}hns buffer:{5}ms video:{6}~{7}hns buffer:{8}ms ", 
                        _stream.GetResponseUri.ToString(),
                        (DateTime.Now - _stream.RequestStartTime).TotalSeconds,
                        (int)(_BWHistory.GetAverageBandwidth()),
                        _audioBuffer.GetSegmentStart(),
                        (null == _audioBuffer.GetLastSample()) ? _audioBuffer.GetSegmentStart() : _audioBuffer.GetLastSample().AdjustedTimeStamp + _audioBuffer.GetLastSample().Duration.Ticks,
                        (long)(_audioBuffer.BufferLevel.TotalMilliseconds),
                        _videoBuffer.GetSegmentStart(),
                        (null == _videoBuffer.GetLastSample()) ? _videoBuffer.GetSegmentStart() : _videoBuffer.GetLastSample().AdjustedTimeStamp + _videoBuffer.GetLastSample().Duration.Ticks,
                        (long)(_videoBuffer.BufferLevel.TotalMilliseconds) 
                        );
                }
            }
        }

        /// <summary>
        /// Parses a single packet of MPEG-2 Transport Stream. Packet must be exactly 188 bytes long
        /// and must begin with packet signature.
        /// </summary>
        /// <param name="byteBuffer"></param>
        protected void ParsePacket(byte[] byteBuffer)
        {
            TSPacket tsPacket = new TSPacket(byteBuffer);

            if (tsPacket.HasPayload())
            {
                if (tsPacket._pid == 0)
                {
                    if (!_parsedPAT)
                    {
                        _parsedPAT = true;
                        if (_programs == null)
                            _programs = new Dictionary<uint, TSProgram>();

                        tsPacket.ParsePAT(ref _programs);
                    }
                }
                else
                {
                    TSProgram program;
                    if (_programs != null && _programs.TryGetValue(tsPacket._pid, out program))
                    {
                        if (!program.ParsedPMT)
                        {
                            program.ParsedPMT = true;
                            ParsePMT(tsPacket, program);
                        }
                    }
                    else
                    {
                        TSStream stream;
                        if (_streams != null && _streams.TryGetValue(tsPacket._pid, out stream))
                        {
                            if ((tsPacket._transport_error_indicator == 0x01) || tsPacket._discontinuity)
                            {
                                stream.Flush(true);
                                HLSTrace.WriteLine("Flush{0}: TEI {1:X} discontinuity {2:X}", tsPacket._pid, tsPacket._transport_error_indicator, tsPacket._discontinuity);
                            }

                            stream.ParseData(byteBuffer, tsPacket._headerSize, byteBuffer.Length - tsPacket._headerSize, (int)tsPacket._continuity_counter);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Flushes entire pipeline.
        /// </summary>
        public void Flush(bool discontinuity)
        {
            if (null != _streams && null != _streams.Values)
            {
                foreach (TSStream stream in _streams.Values)
                {
                    stream.Flush(discontinuity);
                }
            }
        }

        /// <summary>
        /// Parses PMT structure.
        /// </summary>
        /// <param name="program"></param>
        /// <param name="br"></param>
        private void ParsePMT(TSPacket tsPacket, TSProgram program)
        {
            tsPacket.ParsePMTHeader();

            uint streamTypeCode = 0;
            uint elementary_PID = 0;
            uint ES_info_length = 0;
            while (tsPacket.ParseNextPMTStream(ref streamTypeCode, ref elementary_PID, ref ES_info_length))
            {
                // Allocate stream and payload handlers
                if (_streams == null)
                    _streams = new Dictionary<uint, TSStream>();

                TSStream stream;
                bool discontinuity;

                HLSTrace.WriteLineLow("PMT: type {0} pid {1} len {2}", streamTypeCode.ToString("X"), elementary_PID.ToString("X"), ES_info_length.ToString("X"));

                if (_streams.TryGetValue(elementary_PID, out stream))
                {
                    discontinuity = false;
                }
                else
                {
                    if (streamTypeCode == AVC_STREAM_TYPE_CODE)
                    {
                        stream = new TSStream(elementary_PID, TSStream.StreamContetType.Video);

                        if (_videoFormatParser == null)
                            _videoFormatParser = new H264Parser(_videoBuffer, _metadata, _stream.HLSStream );
                        else
                            _videoFormatParser.Flush();

                        _videoFormatParser.Bitrate = _bitrate;

                        stream.Parser = new PESParser(_videoFormatParser);
                    }
                    else if (streamTypeCode == AAC_STREAM_TYPE_CODE || streamTypeCode == DDPLUS_STREAM_TYPE_CODE)
                    {
                        stream = new TSStream(elementary_PID, TSStream.StreamContetType.Audio);

                        bool discardThisStream = false;

                        foreach (KeyValuePair<uint, TSStream> s in _streams)
                        {
                            if (s.Value.StreamType == TSStream.StreamContetType.Audio)
                            {
                                if (s.Key > elementary_PID)
                                {
                                    if (_nullFormatParser == null)
                                        _nullFormatParser = new NullParser(null);

                                    s.Value.Parser = new PESParser(_nullFormatParser);
                                }
                                else
                                {
                                    discardThisStream = true;
                                    break;
                                }
                            }
                        }

                        if (discardThisStream)
                        {
                            if (_nullFormatParser == null)
                                _nullFormatParser = new NullParser(null);

                            stream.Parser = new PESParser(_nullFormatParser);
                            stream.StreamType = TSStream.StreamContetType.Unknown;
                        }
                        else
                        {
                            if (null != _audioFormatParser)
                            {
                                _audioFormatParser.Flush();
                            }
                            if (streamTypeCode == AAC_STREAM_TYPE_CODE)
                                _audioFormatParser = new ADTSParser(_audioBuffer, _stream.HLSStream);
                            else
                                _audioFormatParser = new DDPlusParser(_audioBuffer, _stream.HLSStream);

                            stream.Parser = new PESParser(_audioFormatParser);
                            stream.StreamType = TSStream.StreamContetType.Audio;
                        }
                    }
                    else
                    {
                        stream = new TSStream(elementary_PID, TSStream.StreamContetType.Unknown);
                        HLSTrace.WriteLine("Ignoring unknown stream type {0}", streamTypeCode);
                        if (_nullFormatParser == null)
                            _nullFormatParser = new NullParser(null);
                        stream.Parser = new PESParser(_nullFormatParser);
                    }

                    _streams[elementary_PID] = stream;
                    discontinuity = true;
                }

                program.AddStream(stream);
                stream.Parser.StartSegment(discontinuity);
            }
        }

        #region IDisposable Members
        /// <summary>
        /// Implements IDisposable.Dispose
        /// </summary>
        public void Dispose()
        {
            lock (this)
            {
                Dispose(true);
            }
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
                    if (_stream != null)
                    {
                        _stream.Dispose();
                        _stream = null;
                    }
                }
                _disposed = true;
            }
        }
        #endregion
    }
}

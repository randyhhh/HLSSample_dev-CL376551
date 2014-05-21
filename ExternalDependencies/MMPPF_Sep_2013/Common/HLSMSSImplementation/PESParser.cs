using System;
using System.Diagnostics;

namespace Silverlight.Samples.HttpLiveStreaming
{

    /// <summary>
    /// Exception for any problems with PES parsing
    /// </summary>
    public class PESParserException : Exception
    {
        public PESParserException(string message) :
            base(message)
        {
        }
    }

    /// <summary>
    /// Implements parser for MPEG-TS Packetized Elementary Stream
    /// </summary>
    public class PESParser
    {
        /// <summary>
        /// Magic constant stored in _bytesRead field to indicate that length of a packet
        /// is not known. Instead, parser scans until it finds next PES packet header
        /// signature, which is 00 00 01 XX, where last byte is stream ID.
        /// </summary>
        private const int Unbounded = 0x7fff0000;

        /// <summary>
        /// Stream ID extracted out of first frame header.
        /// </summary>
        private uint _streamId;

        /// <summary>
        /// Output sampler to parse packet payloads.
        /// </summary>
        private MediaFormatParser _output;

        /// <summary>
        /// Indicates that parser is buffering until it gets entire packet header.
        /// </summary>
        private bool _waitForHeader;

        /// <summary>
        /// Remaining bytes to read in current packet body, or Unbounded if packet length is unknown.
        /// </summary>
        private int _bytesToRead;

        /// <summary>
        /// Current offset in four-byte rolling buffer _marker
        /// </summary>
        private int _markerState;

        /// <summary>
        /// Four-byte rolling buffer used to detect packet header signatures in cases where they 
        /// span multiple incoming data segments (e.g. multiple MPEG-TS frames)
        /// </summary>
        private uint _marker;

        /// <summary>
        /// Presentation Timestamp from last parsed header
        /// </summary>
        private ulong _PTS;

        /// <summary>
        /// Decoder Timestamp from last parsed header
        /// </summary>
        private ulong  _DTS;

        /// <summary>
        /// Temporary buffer for header buffering. Should never grow bigger than a few hundreds bytes.
        /// </summary>
        private byte[] _byteBuffer;

        /// <summary>
        /// Number of bytes currently in buffer.
        /// </summary>
        private int _byteBufferSize;

        /// <summary>
        /// Bit parsing helper
        /// </summary>
        private BitstreamReader _bitstream;

        /// <summary>
        /// Indicates parser is at beginning of new segment and should
        /// be prepared for discontinuities in data
        /// </summary>
        private bool _newSegment = true;

        /// <summary>
        /// Indicates number of bytes skipped at beginning of discontinuous
        /// segment while looking for sync bits. Not used except for diagnostics.
        /// </summary>
        private int _skippedBytes = 0;

        /// <summary>
        /// A private boolean flag used to avoid flooding the debug output with 
        /// warning messages for un-aligned PES headers
        /// </summary>
        private static bool _alignmentWarningShown = false;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="sampler">Output sampler</param>
        public PESParser(MediaFormatParser formatParser)
        {
            _output = formatParser;
            _byteBuffer = new byte[1024];
            _bitstream = new BitstreamReader();
        }

        /// <summary>
        /// Parses chunks of data. This is main entry point of the class.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public void ParseData(byte[] data, int offset, int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException("count");
            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset");
            if (count == 0)
                return;

            int startOffset = offset;

            if (_waitForHeader)
            {
                if (_bytesToRead != 0 || _byteBufferSize <= 0)
                    throw new InvalidOperationException("internal error");

                int previousBufferSize = _byteBufferSize;
                if (_byteBufferSize + count > _byteBuffer.Length)
                    Array.Resize(ref _byteBuffer, _byteBufferSize + count);

                Array.Copy(data, offset, _byteBuffer, _byteBufferSize, count);
                _byteBufferSize += count;

                int headerSize = ParseHeader(_byteBuffer, 0, _byteBufferSize);
                if (headerSize == 0)
                    return;

                if (headerSize < previousBufferSize)
                    throw new InvalidOperationException("internal error");

                offset += headerSize - previousBufferSize;
                count -= headerSize - previousBufferSize;
                _byteBufferSize = 0;
                _waitForHeader = false;
            }

            if (_byteBufferSize != 0 || _waitForHeader)
                throw new InvalidOperationException("internal error");

            while (count > 0)
            {
                if (_bytesToRead == Unbounded)
                {
                    while (count > 0)
                    {
                        _marker = (_marker << 8) | data[offset];
                        if (_markerState == 3)
                        {
                            if (_marker == (0x00000100 | _streamId))
                            {
                                _markerState = 0;
                                _bytesToRead = 0;
                                break;
                            }
                            else
                                _output.WriteByte((byte)(_marker >> 24));
                        }
                        else
                            _markerState++;
                        offset++;
                        count--;
                    }

                    if (_bytesToRead == Unbounded)
                        break;

                    if (offset - 3 >= startOffset)
                    {
                        offset -= 3;
                        count += 3;
                    }
                    else
                    {
                        throw new NotImplementedException("incorrect stream format");
                    }
                }
                else
                    if (_bytesToRead > 0)
                    {
                        if (_bytesToRead >= count)
                        {
                            _output.Write(data, offset, count);
                            _bytesToRead -= count;
                            break;
                        }

                        _output.Write(data, offset, _bytesToRead);
                        offset += _bytesToRead;
                        count -= _bytesToRead;
                        _bytesToRead = 0;
                    }

                if (_bytesToRead != 0 || count <= 0)
                    throw new InvalidOperationException("internal error");

                int headerSize = ParseHeader(data, offset, count);
                if (headerSize > 0)
                {
                    offset += headerSize;
                    count -= headerSize;
                }
                else
                {
                    _waitForHeader = true;
                    if (count > _byteBuffer.Length)
                        Array.Resize(ref _byteBuffer, count);
                    Array.Copy(data, offset, _byteBuffer, 0, count);
                    _byteBufferSize = count;
                    break;
                }
            }
        }

        /// <summary>
        /// Flushes all buffers and sends last data into output sampler.
        /// </summary>
        public void Flush(bool discontinuity)
        {
            if (_bytesToRead == Unbounded)
            {
                while (_markerState > 0)
                {
                    _output.WriteByte((byte)((_marker >> (_markerState * 8)) & 0xff));
                    _markerState--;
                }
            }

            // If we cannot verify integrity of last chunk of data, we should
            // discard it instead of flushing it down the pipeline.
            if (discontinuity)
                _output.DiscardLastData();
            else
                _output.Flush();

            _bytesToRead = 0;
        }

        /// <summary>
        /// Called at beginning of each segment.
        /// </summary>
        /// <param name="discontinuity"></param>
        public void StartSegment(bool discontinuity)
        {
            if (discontinuity)
            {
                Flush(true);
                _newSegment = true;
                _skippedBytes = 0;
            }
        }

        /// <summary>
        /// Attempts to parse PES header at given offset. If successfully parses entire packet header,
        /// returns length of the header and prepares parser for reading packet body by setting
        /// variables _bytesToRead, _marker, _markerState. If there aren't sufficient bytes in
        /// buffer to contain entire header, returns 0 and will be called again when more data
        /// received from network.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        private int ParseHeader(byte[] data, int offset, int count)
        {
            if (count < 10)
                return 0;

            int sync = 0;
            while (sync < count - 2 &&
                (data[offset + sync] != 0x00 ||
                data[offset + sync + 1] != 0x00 ||
                data[offset + sync + 2] != 0x01))
                sync++;

            if (sync > 0)
            {
                if (!_newSegment)
                {
                    // Unless we are at beginning of unaligned segment,
                    // we expect sync bits.
                    throw new PESParserException("sync bits not found");                    
                }

                //If final bytes are non-zero, they cannot start sync bits, so consume them as well.
                if (data[offset + sync] != 0x00)
                    sync++;
                if (data[offset + sync] != 0x00)
                    sync++;
                _bytesToRead = 0;
                _skippedBytes += sync;
                return sync;
            }

            if (_newSegment)
            {
                if (_skippedBytes > 0)
                {
                    //HLSTrace.WriteLine("PESParser: skipped " + _skippedBytes.ToString() + " bytes looking for sync bits");
                }
                _newSegment = false;
            }

            _bitstream.Init(data, offset + 3);

            uint stream_id = _bitstream.ReadUBits(8);
            if (_streamId == 0)
                _streamId = stream_id;

            uint PES_packet_length = _bitstream.ReadUBits(16);
            uint PES_optional_marker = _bitstream.ReadUBits(2);

            if (PES_optional_marker != 0x02)    // No optional headers
            {
                if (PES_packet_length > 0)
                {
                    _bytesToRead = (int)PES_packet_length;
                }
                else
                {
                    _bytesToRead = Unbounded;
                    _markerState = 0;
                    _marker = 0;
                }
                return 6;
            }

            _bitstream.SkipBits(3);     //Skip over the Scrambling control (2 bits) and Priority flag (1 bit)

            uint data_alignment_indicator = _bitstream.ReadUBits(1);

            if (data_alignment_indicator == 1)
            {
                // In cases that previous frame chunk data has been too small, there maybe some lingering data left in the PES 
                // parser buffer. Since the next PES parser data will start from beginning of this new packet sample, flush 
                // out any unparsed data in the buffers, which would parse any data still waiting in our buffers.
                Flush(false);
            }

            if ((data_alignment_indicator != 1) && !_alignmentWarningShown )
            {
                Debug.WriteLine(" Warning: 'data alignment indicator' flag is not set in the PES headers of stream {0} (0x{0:X}). \r\n" +
                                " This indicates that the data headers (e.g., ADTS data header or AVC header) do not neceassirly \r\n" +
                                " follow the PES header immediately. This implementation does not handle such un-aligned data, and \r\n" +
                                " the sample may throw an exception while parsing the data if it fails to find the data headers. \r\n",
                                _streamId);

                _alignmentWarningShown = true; 
            }

            _bitstream.SkipBits(2);     //Skip over the Copy right (1 bit) and Original or Copy flag (1 bit)


            uint PTS_indicator = _bitstream.ReadUBits(1);
            uint DTS_indicator = _bitstream.ReadUBits(1);
            _bitstream.SkipBits(6);
            uint PES_optional_length = _bitstream.ReadUBits(8);
            if (count < 9 + PES_optional_length)
                return 0;

            if (PTS_indicator != 0)
            {
                uint PTS_prefix = _bitstream.ReadUBits(4);
                if (PTS_prefix != 0x02 && PTS_prefix != 0x03)
                    throw new PESParserException("Invalid PTS prefix");

                ulong PTS = _bitstream.ReadBitsULong(3) << 30;
                _bitstream.SkipBits(1);
                PTS |= _bitstream.ReadBitsULong(15) << 15;
                _bitstream.SkipBits(1);
                PTS |= _bitstream.ReadBitsULong(15);
                _bitstream.SkipBits(1);
                _PTS = PTS;

                if (DTS_indicator != 0)
                {
                    uint DTS_prefix = _bitstream.ReadUBits(4);
                    if (DTS_prefix != 0x01)
                        throw new PESParserException("Invalid DTS prefix");

                    ulong DTS = _bitstream.ReadBitsULong(3) << 30;
                    _bitstream.SkipBits(1);
                    DTS |= _bitstream.ReadBitsULong(15) << 15;
                    _bitstream.SkipBits(1);
                    DTS |= _bitstream.ReadBitsULong(15);
                    _bitstream.SkipBits(1);
                    _DTS = DTS;
                }

                HLSTrace.WriteLineLow("Updating PES timestamps for stream_id {0}, PTS (90KHz) = {1}, PTS (HNS) = {2}, DTS (90KHz) = {3}, DTS (HNS) = {4}", 
                    stream_id, _PTS, MediaFormatParser.HnsTimestampFrom90kHzTimestamp((long)_PTS), _DTS, MediaFormatParser.HnsTimestampFrom90kHzTimestamp((long)_DTS));

                _output.UpdateTimestamps(_PTS, _DTS);
            }
            else if (DTS_indicator != 0)
                    throw new PESParserException("DTS without PTS present");


            if (PTS_indicator == 0)
            {
                HLSTrace.WriteLine(" PTS and DTS  are missing for PES packet, stream_id = {0}", stream_id);
            }

            if (PES_packet_length > 0)
            {
                _bytesToRead = (int)(PES_packet_length - PES_optional_length - 3);
            }
            else
            {
                _bytesToRead = Unbounded;
                _markerState = 0;
                _marker = 0;
            }
            _newSegment = true;
            return 9 + (int)PES_optional_length;

        }
    }

}

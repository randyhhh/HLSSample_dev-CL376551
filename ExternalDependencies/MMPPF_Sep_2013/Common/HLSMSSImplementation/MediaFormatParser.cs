using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media;

namespace Silverlight.Samples.HttpLiveStreaming
{

    /// <summary>
    /// Implements common superclass for media format parsers. Parsers process incoming
    /// bytestreams (typically payloads in Packetized Elementary Streams) and generate
    /// sequences of timestamped samples (typically, to play in MediaElement).
    /// Superclass implementation is responsible for buffering and splitting bytestream
    /// into chunks. Subclasses implement format-specific details.
    /// </summary>
    public abstract class MediaFormatParser
    {
        private int _bytesToRead;
        private byte[] _byteBuffer;
        private int _byteBufferSize;
        private Sample _currentSample;
        private uint _bitrate;
        private SampleBuffer _outputBuffer;
        protected List<long> _PTSTimestampList = new List<long>();
        private const int InitialBufferSize = 1024;
        private HLSStream _hlsStream;

        /// <summary>
        /// Bitrate of the current stream being parsed.
        /// </summary>
        public uint Bitrate
        {
            set
            {
                _bitrate = value;
            }
        }

        public HLSStream HLSStream
        {
            get
            {
                return _hlsStream;
            }
            set
            {
                _hlsStream = value;
            }
        }

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="msd"></param>
        public MediaFormatParser(SampleBuffer outputBuffer, HLSStream hlsStream)
        {
            _outputBuffer = outputBuffer;
            _byteBuffer = new byte[InitialBufferSize];
            _hlsStream = hlsStream;
        }

        /// <summary>
        /// Processes a byte of data thrown at us.
        /// </summary>
        /// <param name="b"></param>
        public void WriteByte(byte b)
        {
            if (_byteBufferSize < _byteBuffer.Length)
            {
                _byteBuffer[_byteBufferSize++] = b;
            }
            else
            {
                HandleData(_byteBuffer, 0, _byteBufferSize);
                _byteBuffer[_byteBufferSize++] = b;
            }
        }

        /// <summary>
        /// Processes a chunk of data thrown at us.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public void Write(byte[] data, int offset, int count)
        {
            if (_byteBufferSize > 0)
            {
                // First, process any data previously collected via WriteByte or
                // left over from previous chunks.
                //
                HandleData(_byteBuffer, 0, _byteBufferSize);

                // If any data still remains unprocessed, the chunk must be too
                // small. Append it with new data chunk and handle together. This
                // should be relatively unfrequent situation.
                //
                if (_byteBufferSize > 0)
                {
                    if (_byteBufferSize + count > _byteBuffer.Length)
                        Array.Resize(ref _byteBuffer, _byteBufferSize + count);
                    Array.Copy(data, offset, _byteBuffer, _byteBufferSize, count);
                    _byteBufferSize += count;
                    HandleData(_byteBuffer, 0, _byteBufferSize);
                    return;
                }
            }

            HandleData(data, offset, count);
        }

        /// <summary>
        /// Update timestamp from container data. Container timestamps have 90kHz resolution.
        /// </summary>
        /// <param name="PTS"></param>
        /// <param name="DTS"></param>
        public void UpdateTimestamps(ulong PTS, ulong DTS)
        {
            if (PTS != 0)
            {
                // NullParser doesn't have any output buffer
                if (null != _outputBuffer)
                {
                    _PTSTimestampList.Add(_outputBuffer.On90kHZTsSampleTime(HLSStream.TimelineIndex, PTS));
                }
            }
        }

        /// <summary>
        /// Processes the data after write or flush operation, calling concrete ParseData
        /// implementation in a loop until data chunk is exhausted or remaining chunk
        /// is too small to be useful. Then, any leftover bytes are collected in _buffer
        /// until more data arrives from network.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        private void HandleData(byte[] data, int offset, int count)
        {
            _byteBufferSize = 0;
            while (count > 0)
            {
                // If we still have data to collect in current sample, do that first.
                if (_bytesToRead > 0)
                {
                    if (_bytesToRead >= count)
                    {
                        _currentSample.AddData(data, offset, count);
                        _bytesToRead -= count;
                        break;
                    }

                    _currentSample.AddData(data, offset, _bytesToRead);

                    offset += _bytesToRead;
                    count -= _bytesToRead;
                    _bytesToRead = 0;
                }

                // The sample should typically be complete at this point, so
                // call subclass ParseData now to parse the header of next sample.
                
                int skipBytes = ParseData(data, offset, count);
                if (skipBytes > count)
                    throw new InvalidOperationException("invalid return");

                if (skipBytes == 0 && _bytesToRead == 0)
                {
                    // Remaining data was too small to be useful, as indicated
                    // by ParseData returning 0 and not calling BeginSample/AddToSample.
                    // Collect data into buffer until next time and return.

                    int newLength = InitialBufferSize;
                    if (newLength < count)
                         newLength = count;
                    byte[] newByteBuffer = new byte[newLength];
                    Array.Copy(data, offset, newByteBuffer, 0, count);
                    _byteBuffer = newByteBuffer;
                    _byteBufferSize = count;
                    break;
                }

                offset += skipBytes;
                count -= skipBytes;
            }
        }

        /// <summary>
        /// Flushes all buffers and caches last samples.
        /// </summary>
        public void Flush()
        {
            if (_byteBufferSize > 0)
            {
                // Handle any remaining data.
                HandleData(_byteBuffer, 0, _byteBufferSize);

                // If anything is still unhandled, append it to last sample.
                // This is useful for H.264 HAL units which can have zero padding
                // at the end.
                if (_byteBufferSize > 0)
                {
                    if (_currentSample != null)
                        _currentSample.AddData(_byteBuffer, 0, _byteBufferSize);
                    _byteBufferSize = 0;
                }
            }

            // Finish and enqueue last sample of the stream.
            if (_currentSample != null)
            {
                if (_currentSample.DataStream == null)
                    throw new InvalidOperationException("invalid sample");

                lock (_outputBuffer)
                {
                    _outputBuffer.Enqueue(_currentSample);
                }

                _currentSample = null;
            }

            _bytesToRead = 0;

            _PTSTimestampList.Clear();
        }

        /// <summary>
        /// Discards current sample and any unused data. This method is called
        /// instead of Flush at the end of stream when caller is unable to verify
        /// integrity of last data. Typically it happens for any discontinuities in
        /// transport stream, like bitrate change, skipped segments, etc.
        /// </summary>
        public void DiscardLastData()
        {
            _byteBufferSize = 0;
            _bytesToRead = 0;
            if (null != _currentSample)
            {
                _currentSample.Discard();
            }
            _currentSample = null;
        }

        /// <summary>
        /// Marks the current sample as a refernce or key frame 
        /// </summary>
        public void MarkReferenceSample()
        {
            if (_currentSample != null)
                _currentSample.KeyFrame = true;
        }


        /// <summary>
        /// Finalizes current sample, if any, and creates new sample. Should only
        /// be called from ParseData method.
        /// </summary>
        /// <param name="count">Size of new sample in bytes</param>
        /// <param name="duration">Sample duration in HNS</param>
        /// <param name="timestamp">Timestamp of new sample in HNS</param>
        protected void BeginSample(int count, long duration, long timeStamp)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException("count");

            if (_bytesToRead != 0)
                throw new InvalidOperationException("invalid call");

            if (_currentSample != null)
            {
                // Finish and enqueue previous current sample. It must have
                // collected some data at this point.
                Debug.Assert(_bytesToRead == 0);
                if (_currentSample.DataStream == null)
                    throw new InvalidOperationException("invalid sample");

                lock (_outputBuffer)
                {
                    Debug.Assert(_currentSample.DataStream.Length > 0);
                    _outputBuffer.Enqueue(_currentSample);
                }
            }

            _currentSample = _outputBuffer.AllocSample(timeStamp, duration, _bitrate, _hlsStream, count);
            _bytesToRead = count;
        }

        /// <summary>
        /// Adds next count bytes to the current sample. Should only
        /// be called from ParseData method.
        /// </summary>
        /// <param name="count"></param>
        protected void AddToLastSample(int count)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException("count");

            if (_currentSample != null)
            {
                if (_bytesToRead != 0 )
                    throw new InvalidOperationException("invalid call");

                _bytesToRead += count;
            }
        }

        /// <summary>
        /// Parses bytestream for sample headers, start markers, etc. Implementations of this
        /// method may call BeginSample to indicate when new sample needs to be created.
        /// Returns number of bytes to skip in bytestream (typically size of header that should
        /// not be included in sample data). If BeginSample is called by implementation, the
        /// caller will create new sample and start collecting data for it, starting at first byte
        /// after the skipped bytes. If BeginSample is not called and implementation returns 0,
        /// caller will wait for more data and call again when more data is available. This
        /// is useful to handle situation when there is not enough bytes provided to make
        /// decision whether to start a new sample and what the size of it should be.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        protected abstract int ParseData(byte[] data, int offset, int count);

        /// <summary>
        /// Returns true when parser gathers enough information from 
        /// stream to start playback (e.g. sampling rate, picture dimensions, etc.)
        /// </summary>
        public bool MediaInfoReady
        {
            get
            {
                return _outputBuffer.Description != null;
            }
        }

        /// <summary>
        /// MediaStreamDescription that can be directly passed into MediaStreamSource.
        /// </summary>
        public MediaStreamDescription Description
        {
            get
            {
                return _outputBuffer.Description;
            }
            set
            {
                _outputBuffer.Description = value;
            }
        }

        /// <summary>
        /// Translates 90kHz timestamps to 100ns timestamps
        /// </summary>
        /// <param name="PTS"></param>
        /// <returns></returns>
        public static long HnsTimestampFrom90kHzTimestamp(long PTS)
        {
            return (long)((double)PTS * 1000.0 / 9.0);
        }
    }
}

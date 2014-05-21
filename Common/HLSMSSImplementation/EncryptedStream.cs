using System;
using System.IO;
using System.Security.Cryptography;
using System.Net;

namespace Silverlight.Samples.HttpLiveStreaming
{

    /// <summary>
    /// Helper class for decrypting of network streams.
    /// </summary>
    public class EncryptedStream : IDisposable
    {
        /// <summary>
        /// Input stream
        /// </summary>
        private Stream _stream;


        /// <summary>
        /// HLSStream 
        /// </summary>
        private HLSStream _hlsStream;

        /// <summary>
        /// HttpWebResponse used for downloading this stream
        /// </summary>        
        private HttpWebResponse _response;

        /// <summary>
        /// Decryptor used to decipher stream data
        /// </summary>
        private ICryptoTransform _decryptor;

        /// <summary>
        /// Temporary buffer for decrypted data
        /// </summary>
        private byte[] _decryptBuffer;

        /// <summary>
        /// Second buffer for storing intermediate results
        /// </summary>
        private byte[] _swapBuffer;

        /// <summary>
        /// Current offset in _decryptBuffer
        /// </summary>
        private int _decryptOffset;

        /// <summary>
        /// Remaining bytes in _decryptBuffer
        /// </summary>
        private int _decryptCount;

        /// <summary>
        /// Indicates stream is discontinous to previously played one.
        /// </summary>
        private bool _discontinuity;

        /// <summary>
        /// Track whether Dispose has been called
        /// </summary>
        private bool _disposed;

        public HLSStream HLSStream
        {
            get
            {
                return _hlsStream;
            }
        }

        /// <summary>
        /// Get response url
        /// </summary>
        public Uri GetResponseUri
        {
            get
            {
                return _response.ResponseUri;
            }
        }

        /// <summary>
        /// Track the time when the httpWebRequest for this stream was sent to the server, used by bandwidth calculation
        /// </summary>
        private DateTime _requestStartTime;

        /// <summary>
        /// Constructor for pass-through reading without decrypting
        /// </summary>
        /// <param name="stream"></param>
        public EncryptedStream(Stream stream, DateTime RequestStartTime, HttpWebResponse response, bool discontinuity, HLSStream hlsStream)
        {
            _stream = stream;
            _response = response; 
            _discontinuity = discontinuity;
            _hlsStream = hlsStream;
            _requestStartTime = RequestStartTime;
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="decryptor"></param>
        /// <param name="bufferSize"></param>
        public EncryptedStream(Stream stream, DateTime RequestStartTime, HttpWebResponse response, bool discontinuity, ICryptoTransform decryptor, int bufferSize, HLSStream hlsStream)
        {
            _stream = stream;
            _response = response;
            _discontinuity = discontinuity;
            _decryptor = decryptor;

            _hlsStream = hlsStream;
            _requestStartTime = RequestStartTime;

            if (decryptor != null)
            {
                bufferSize = (bufferSize/_decryptor.InputBlockSize + 2) * _decryptor.InputBlockSize;
                _decryptBuffer = new byte[bufferSize];
                _swapBuffer = new byte[bufferSize];
            }
        }

        /// <summary>
        /// Reads data from input stream and decrypts when needed, otherwise
        /// simply delegates to underlying stream reader.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public int Read(byte[] buffer, int offset, int count)
        {
            if (_decryptor != null)
            {
                if (_decryptCount == 0)
                {
                    int bytesToRead = _decryptBuffer.Length;
                    int bytesRead = 0;
                    while (bytesToRead > 0)
                    {
                        int read = _stream.Read(_decryptBuffer, bytesRead, bytesToRead);
                        if (read == 0)
                            break;

                        bytesToRead -= read;
                        bytesRead += read;
                    }

                    if (bytesRead == 0)
                        return 0;

                    if (bytesToRead == 0)
                    {
                        byte[] destBuffer = _swapBuffer;
                        _decryptCount = _decryptor.TransformBlock(_decryptBuffer, 0, _decryptBuffer.Length, destBuffer, 0);
                        _decryptOffset = 0;
                        _swapBuffer = _decryptBuffer;
                        _decryptBuffer = destBuffer;
                    }
                    else
                    {
                        _swapBuffer = null;
                        _decryptBuffer = _decryptor.TransformFinalBlock(_decryptBuffer, 0, bytesRead);
                        _decryptCount = _decryptBuffer.Length;
                        _decryptOffset = 0;
                    }
                }

                if (_decryptCount <= 0)
                    throw new InvalidOperationException("internal error");

                if (count > _decryptCount)
                    count = _decryptCount;

                Array.Copy(_decryptBuffer, _decryptOffset, buffer, offset, count);
                _decryptOffset += count;
                _decryptCount -= count;
                return count;
            }
            else
            {
                return _stream.Read(buffer, offset, count);
            }
        }

        /// <summary>
        /// Indicates stream is discontinous to previously played one.
        /// </summary>
        public bool Discontinuity
        {
            get
            {
                return _discontinuity;
            }
        }

        /// <summary>
        /// Get the time when the httpWebRequest for this stream was sent to the server
        /// </summary>
        public DateTime RequestStartTime
        {
            get
            {
                return _requestStartTime ;
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
                    if (_decryptor != null)
                    {
                        _decryptor.Dispose();
                        _decryptor = null;
                    }
                    if (_stream != null)
                    {
                        _stream.Close();
                        _stream = null;
                    }
                    if (_response != null)
                    {
                        _response.Close();
                        _response = null;
                    }

                }
                _disposed = true;
            }
        }
        #endregion
    }

}


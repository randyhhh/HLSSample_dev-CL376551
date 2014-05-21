using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows.Media;

namespace Silverlight.Samples.HttpLiveStreaming
{

    internal struct AACAudioFrameInfo
    {
        internal int OutFrameSize;
        internal int NoOfSamples;
        internal int SamplingFrequency;
        internal int NoOfChannels;
        internal int Profile;
        internal int OutSamplingFrequency;
        internal int ExtObjectType;
        internal int DownSampledMode;
    }

    /// <summary>
    /// Exception type for ADTS parsing errors.
    /// </summary>
    public class ADTSParserException : Exception
    {
        public ADTSParserException(string message) :
            base(message)
        {
        }
    }

    /// <summary>
    /// Concrete parser that parses ADTS/AAC stream and generates samples.
    /// </summary>
    public class ADTSParser : MediaFormatParser
    {
        /// <summary>
        /// ADTS frame headers are either 7 or 9 bytes long.
        /// </summary>
        protected const int MaxADTSHeaderLength = 9;

        /// <summary>
        /// Bit reader helper.
        /// </summary>
        protected BitstreamReader _bitstream;

        /// <summary>
        /// Contains details on audio format used
        /// </summary>
        private AudioDataTypesHelper.WAVEFORMATEX _waveFormat;

        /// <summary>
        /// Contains details on AAC frame
        /// </summary>
        private AACAudioFrameInfo _aacInfo;

        /// <summary>
        /// The base time stamp used for calculating current frame time 
        /// stamp. This should be set to the last time stamp parsed from 
        /// the PES headers. 
        /// </summary>
        private long _baseTimeStamp = 0;

        /// <summary>
        /// The time stamp of current AAC frame calculated using the last seen PES 
        /// header time stamp. 
        /// </summary>
        private long _currentFrameTimeStamp = 0;


        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="msd"></param>
        public ADTSParser(SampleBuffer outputBuffer, HLSStream hlsStream)
            : base(outputBuffer, hlsStream)
        {
            _bitstream = new BitstreamReader();
        }

        /// <summary>
        /// See description of abstract method for general information about this method.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        protected override int ParseData(byte[] data, int offset, int count)
        {
            const int ATDSAyncWords = 0xfff0;
            int syncOffset = offset;
            if (count < MaxADTSHeaderLength)
                return 0;

            uint syncBits =  ( (uint)((data[syncOffset] << 8) | data[syncOffset + 1]) ) ;

            // search for valid sync bits(FFF), ignore FFFF which is invalid ATDS header and could be stuffing bits. 
            while ((syncBits == 0xffff || (syncBits & ATDSAyncWords) != ATDSAyncWords) && (offset + count - syncOffset) >= 3)
            {
                syncOffset++;
                syncBits = (uint)((data[syncOffset] << 8) | data[syncOffset + 1]);
            }

            if ((syncBits & ATDSAyncWords) != ATDSAyncWords) 
            {
                return count - 1;
            }

            if ((offset + count - syncOffset) < MaxADTSHeaderLength)
            {
                return 0;
            }

            _bitstream.Init(data, syncOffset);
            _bitstream.SkipBits(12);

            uint mpeg_version = _bitstream.ReadUBits(1);
            uint mpeg_layer = _bitstream.ReadUBits(2);
            uint protection_absent = _bitstream.ReadUBits(1);
            uint profile_code = _bitstream.ReadUBits(2);
            uint sampling_rate_code = _bitstream.ReadUBits(4);
            _bitstream.SkipBits(1);
            uint channel_config = _bitstream.ReadUBits(3);

            _bitstream.SkipBits(4);

            int header_length = protection_absent != 0 ? 7 : 9;
            int frame_length = _bitstream.ReadBits(13);
            _bitstream.SkipBits(11);

            int numberOfAACFrames = _bitstream.ReadBits(2) + 1;

            if (sampling_rate_code >= _aacSamplingRatesFromRateCode.Length)
            {
                HLSTrace.WriteLine(" no good!!!! bad ADTS sync word, skip it ");
                return syncOffset - offset + 2;
            }

            if (syncOffset > offset)
            {
                // the audio frame is not started from the PES buffer boundary, read next frame to be sure. 
                if (count < syncOffset + frame_length + MaxADTSHeaderLength)
                {
                    // return 0 to get more data, need to read to next frame
                    return syncOffset - offset -1;
                }
                else
                {
                    uint syncBitsNext = (uint)((data[syncOffset + frame_length] << 8) | data[syncOffset + frame_length + 1]);
                    if (frame_length == 0 || syncBitsNext == 0xffff || (syncBitsNext & ATDSAyncWords) != ATDSAyncWords)
                    {
                        // bad, did not find next sync bits after frame length, this is bad sync bits. skip the fake sync bits
                        return syncOffset - offset + 2;
                    }
                }
            }

            Debug.Assert(numberOfAACFrames == 1);

            int samplingRate = _aacSamplingRatesFromRateCode[sampling_rate_code];

            // Each ADTS frame contains 1024 raw PCM samples in encoded format. 
            // Therefore, the duration of each frame in seconds is given by 
            // 1024/(sampling frequency). The time stamps passed to MediaElement 
            // are in Hns (100 nanosecond) increments. Therefore, frame duration 
            // is given by  10,000,000 * 1024 / SamplingFrequency
            long frameDuration = (long)(10000000.00 * 1024.00 / (double)samplingRate);

            if (_PTSTimestampList.Count == 0)
            {
                // This ADTS frame does not have a PTS from PES header, and therefore 
                // we should calculate its PTS based on the time passed since last frame.
                _currentFrameTimeStamp += frameDuration;
            }
            else
            {
                _baseTimeStamp = _PTSTimestampList[0];
                _currentFrameTimeStamp = _PTSTimestampList[0];
                _PTSTimestampList.RemoveAt(0);
            }

            BeginSample(frame_length - header_length, frameDuration, _currentFrameTimeStamp);


            if (Description == null)
            {
                if (channel_config != 1 && channel_config != 2)
                    throw new ADTSParserException("unsupported channel config");

                ushort numberOfChannels = (ushort)channel_config;
                ushort aacProfile = (ushort)profile_code;
                const ushort sampleSize = 16;

                _aacInfo = new AACAudioFrameInfo();
                _aacInfo.NoOfSamples = 1024;
                _aacInfo.OutFrameSize = 1024 * numberOfChannels * 2;
                _aacInfo.SamplingFrequency = samplingRate;
                _aacInfo.NoOfChannels = numberOfChannels;
                _aacInfo.Profile = aacProfile;
                _aacInfo.OutSamplingFrequency = samplingRate;
                _aacInfo.ExtObjectType = 0;
                _aacInfo.DownSampledMode = 0;

                _waveFormat = new AudioDataTypesHelper.WAVEFORMATEX();
                _waveFormat.formatTag = 0x1601;      // AAC format flag.
                _waveFormat.channels = numberOfChannels;
                _waveFormat.bitsPerSample = sampleSize;
                _waveFormat.samplesPerSec = samplingRate;
                _waveFormat.avgBytesPerSec = numberOfChannels * _waveFormat.samplesPerSec * sampleSize / 8;
                _waveFormat.blockAlign = (ushort)(numberOfChannels * sampleSize / 8);
                _waveFormat.size = 0x20;  // size of AACAudioFrameInfo. 4 * 8

                Dictionary<MediaStreamAttributeKeys, string> streamAttributes = new Dictionary<MediaStreamAttributeKeys, string>();
                streamAttributes[MediaStreamAttributeKeys.CodecPrivateData] = GetCodecPrivateData();
                Description = new MediaStreamDescription(MediaStreamType.Audio, streamAttributes);
            }

            return header_length + ( syncOffset - offset );
        }

        /// <summary>
        /// Sampling rate codes per ADTS spec
        /// </summary>
        internal static int[] _aacSamplingRatesFromRateCode = {
            96000,
            88200,
            64000,
            48000,
            44100,
            32000,
            24000,
            22050,
            16000,
            12000,
            11025,
            8000,
            7350
        };

        /// <summary>
        /// Builds CodecPrivateData string that can be passed into MediaElement
        /// </summary>
        /// <returns></returns>
        private string GetCodecPrivateData()
        {
            StringBuilder cpdBuilder = new StringBuilder();

            // Now generates the HEX string for both WaveFormatEx plus AACInfo.
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_waveFormat.formatTag));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_waveFormat.channels));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_waveFormat.samplesPerSec));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_waveFormat.avgBytesPerSec));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_waveFormat.blockAlign));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_waveFormat.bitsPerSample));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_waveFormat.size));

            // Add AACInfo part
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_aacInfo.OutFrameSize));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_aacInfo.NoOfSamples));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_aacInfo.SamplingFrequency));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_aacInfo.NoOfChannels));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_aacInfo.Profile));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_aacInfo.OutSamplingFrequency));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_aacInfo.ExtObjectType));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_aacInfo.DownSampledMode));

            return cpdBuilder.ToString();
        }


    }

}

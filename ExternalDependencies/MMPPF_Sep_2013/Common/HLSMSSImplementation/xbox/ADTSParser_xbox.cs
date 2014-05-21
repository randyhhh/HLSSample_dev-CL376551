using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;
using System.Diagnostics;

namespace Silverlight.Samples.HttpLiveStreaming
{

    internal struct HE_AAC_WaveInfoTag
    {
        public AudioDataTypesHelper.WAVEFORMATEX WaveFormatEx;

        // Defines the payload type
        // 0-RAW.  The stream contains raw_data_block() elements only.
        // 1-ADTS. The stream contains an adts_sequence(), as defined by MPEG-2.
        // 2-ADIF. The stream contains an adif_sequence(), as defined by MPEG-2.
        // 3-LOAS. The stream contains an MPEG-4 audio transport stream with a
        //         synchronization layer LOAS and a multiplex layer LATM.
        // All other codes are reserved.
        public ushort PayloadType;

        // This is the 8-bit field audioProfileLevelIndication available in the
        // MPEG-4 object descriptor.  It is an indication (as defined in MPEG-4 audio)
        // of the audio profile and level required to process the content associated 
        // with this stream. For example values 0x28-0x2B correspond to AAC Profile,
        // values 0x2C-0x2F correspond to HE-AAC profile and 0x30-0x33 for HE-AAC v2 profile.
        // If unknown, set to zero or 0xFE ("no audio profile specified").
        public ushort AudioProfileLevelIndication;

        // Defines the data that follows this structure. Currently only one data type is supported:
        // 0- AudioSpecificConfig() (as defined by MPEG-4 Audio, ISO/IEC 14496-3) will follow this structure.
        //    WaveFormatEx.cbSize will indicate the total length including AudioSpecificConfig().
        //    Use HEAACWAVEFORMAT to gain easy access to the address of the first byte of
        //    AudioSpecificConfig() for parsing.
        //    Typical values for the size of AudioSpecificConfig (ASC) are:
        //    - 2 bytes for AAC or HE-AAC v1/v2 with implicit signaling of SBR,
        //    - 5 bytes for HE-AAC v1 with explicit signaling of SBR,
        //    - 7 bytes for HE-AAC v2 with explicit signaling of SBR and PS.
        //    The size may be longer than 7 bytes if the 4-bit channelConfiguration field in ASC is zero,
        //    which means program_config_element() is present in ASC.
        //
        // All other codes are reserved.
        public ushort StructType;

        // These reserved tags should be set to zero
        public ushort Reserved1;
        public int Reserved2;

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
        /// ADTS frame headers are either 7 or 9 bytes long as per ADTS spec
        /// </summary>
        protected const int MaxADTSHeaderLength = 9;


        /// <summary>
        /// Sampling rate codes as per ADTS spec
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
        /// Bit reader helper.
        /// </summary>
        protected BitstreamReader _bitstream;

        /// <summary>
        /// Contains details on HE AAC frame
        /// </summary>
        private HE_AAC_WaveInfoTag _heAACWaveInfoTag;

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

            uint syncBits = ((uint)((data[syncOffset] << 8) | data[syncOffset + 1]));

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

            if (syncOffset > offset)
            {
                if (sampling_rate_code >= _aacSamplingRatesFromRateCode.Length)
                {
                    HLSTrace.WriteLine(" no good!!!! bad ADTS sync word, skip it ");
                    return syncOffset - offset + 2;
                }

                // the audio frame is not started from the PES buffer boundary, read next frame to be sure. 
                if (count < syncOffset + frame_length + MaxADTSHeaderLength)
                {
                    // return 0 to get more data, need to read to next frame
                    return syncOffset - offset - 1;
                }
                else
                {
                    uint syncBitsNext = (uint)((data[syncOffset + frame_length] << 8) | data[syncOffset + frame_length + 1]);
                    if (frame_length == 0 || syncBitsNext == 0xffff || ( syncBitsNext & ATDSAyncWords) != ATDSAyncWords)
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

            if (Description == null)
            {
                if (channel_config != 1 && channel_config != 2)
                    throw new ADTSParserException("unsupported channel config");

                ushort numberOfChannels = (ushort)channel_config;
                ushort aacProfile = (ushort)profile_code;

                _heAACWaveInfoTag.PayloadType = 1;       // Should be set to 1 for ADTS payload according to the standarad
                _heAACWaveInfoTag.AudioProfileLevelIndication = 0;  // Unknown to us, set to 0 as suggested by the standard
                _heAACWaveInfoTag.StructType = 0;       // Set to 0 to indicate that the data that follows the HEAACWAVEINFO structure contains the value of AudioSpecificConfig()  as defined by ISO/IEC 14496-3.   indicated in the standard
                _heAACWaveInfoTag.Reserved1 = 0;        // Should be set to 0 as indicated in the standard
                _heAACWaveInfoTag.Reserved2 = 0;        // Should be set to 0 as indicated in the standard

                _heAACWaveInfoTag.WaveFormatEx.formatTag = AudioDataTypesHelper.WAVE_FORMAT_MPEG_HEAAC;  // Use the HE AAC format tag
                _heAACWaveInfoTag.WaveFormatEx.channels = numberOfChannels;

                // We initialize the audio codec with a minimum sampling rate of 44100. This is a workaround for an 
                // issue in xbox audio renderer which cannot handle a change in sampling frequency that is more than 
                // 2x, for example, it cannot handle switching from 11K audio to 44k audio. 
                _heAACWaveInfoTag.WaveFormatEx.samplesPerSec = (samplingRate >= 44100) ? samplingRate : 44100;

                _heAACWaveInfoTag.WaveFormatEx.avgBytesPerSec = 0;              // Unknown to us, set to 0 as suggested  by the standard
                _heAACWaveInfoTag.WaveFormatEx.blockAlign = 1;                  // Should be set to 1 as indicated by the standard 
                _heAACWaveInfoTag.WaveFormatEx.bitsPerSample = 0;               // Unknown to us, set to 0 as suggested  by the standard

                // WaveFormatEx.size must be set to the number of bytes that follow the WaveFormatEx, which can be set as:
                // _heAACWaveInfoTag.WaveFormatEx.size = sizeof(HE_AAC_WaveInfoTag) - sizeof(AudioDataTypesHelper.WAVEFORMATEX);
                // However, using sizeof(HE_AAC_WaveInfoTag) would generate a compile time error:
                // CS0233:'HE_AAC_WaveInfoTag' does not have a predefined size, therefore sizeof can only be used in an unsafe context. 
                // So we use the hard-coded size here, which should be set to 12 bytes (4 ushort and 1 int).
                _heAACWaveInfoTag.WaveFormatEx.size = 12;

                Dictionary<MediaStreamAttributeKeys, string> streamAttributes = new Dictionary<MediaStreamAttributeKeys, string>();
                streamAttributes[MediaStreamAttributeKeys.CodecPrivateData] = GetCodecPrivateData();
                Description = new MediaStreamDescription(MediaStreamType.Audio, streamAttributes);
            }

            BeginSample(frame_length, frameDuration, _currentFrameTimeStamp);

            return (syncOffset - offset);
        }


        /// <summary>
        /// Builds CodecPrivateData string that can be passed into MediaElement
        /// </summary>
        /// <returns></returns>
        private string GetCodecPrivateData()
        {
            StringBuilder cpdBuilder = new StringBuilder();

            // Now generates the HEX string for both WaveFormatEx plus AACInfo.
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_heAACWaveInfoTag.WaveFormatEx.formatTag));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_heAACWaveInfoTag.WaveFormatEx.channels));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_heAACWaveInfoTag.WaveFormatEx.samplesPerSec));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_heAACWaveInfoTag.WaveFormatEx.avgBytesPerSec));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_heAACWaveInfoTag.WaveFormatEx.blockAlign));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_heAACWaveInfoTag.WaveFormatEx.bitsPerSample));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_heAACWaveInfoTag.WaveFormatEx.size));

            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_heAACWaveInfoTag.PayloadType));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_heAACWaveInfoTag.AudioProfileLevelIndication));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_heAACWaveInfoTag.StructType));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_heAACWaveInfoTag.Reserved1));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_heAACWaveInfoTag.Reserved2));

            HLSTrace.WriteLine("ADTS CodePrivateData is " + cpdBuilder.ToString());

            return cpdBuilder.ToString();
        }


    }

}

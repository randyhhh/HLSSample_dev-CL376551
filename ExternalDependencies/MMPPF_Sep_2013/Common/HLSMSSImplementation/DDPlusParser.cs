using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;

namespace Silverlight.Samples.HttpLiveStreaming
{

    /// <summary>
    /// Exception type for DD+ parsing errors.
    /// </summary>
    public class DDPlusParserException : Exception
    {
        public DDPlusParserException(string message) :
            base(message)
        {
        }
    }

    /// <summary>
    /// Concrete parser that parses Dolby Digital Plus stream and generates samples.
    /// </summary>
    public class DDPlusParser : MediaFormatParser
    {

        /// <summary>
        /// AC3 header size in byte
        /// </summary>
        private const uint AC3_HEADER_SIZE_IN_BYTES = 126; // The AC3 header is 63*2 =  126 bytes 

        /// <summary>
        /// Low byte of the sync word for DD+ headers
        /// </summary>     
        private const byte SyncWordLowByte = 0x0b;
        
        /// <summary>
        /// High byte of the sync word for DD+ headers
        /// </summary>     
        private const byte SyncWordHighByte = 0x77;

        /// <summary>
        /// Bit reader helper.
        /// </summary>
        protected BitstreamReader _bitstream;

        /// <summary>
        /// Contains details on audio format used
        /// </summary>
        private AudioDataTypesHelper.WAVEFORMATEXTENSIBLE _waveFormatEx;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="msd"></param>
        public DDPlusParser(SampleBuffer outputBuffer, HLSStream hlsStream)
            : base(outputBuffer,hlsStream)
        {
            _bitstream = new BitstreamReader();
        }

        /// <summary>
        /// Parses bitstream of DD+ for sample headers, start markers, etc. 
        /// See description of abstract method for more information about this method.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        protected override int ParseData(byte[] data, int offset, int count)
        {
            int frequency = 0;

            // Search for the DD+ sync word which is 0x0B 0x77 
            while ((offset < count - 1) && (data[offset] != SyncWordLowByte || data[offset + 1] != SyncWordHighByte))
                offset++;

            // make sure we have the whole header in the current buffer
            if (offset > count - AC3_HEADER_SIZE_IN_BYTES)
                return 0;

            _bitstream.Init(data, offset);

            _bitstream.SkipBits(16);  // Skip over the sync word

            int streamType = _bitstream.ReadBits(2);
            int subStreamId = _bitstream.ReadBits(3);
            int frameSize = _bitstream.ReadBits(11);
            int fsCode = _bitstream.ReadBits(2);
            int fsCode2 = _bitstream.ReadBits(2);
            int audioCodingMode = _bitstream.ReadBits(3);
            int lowFrequencyEffectFlag = _bitstream.ReadBits(1);
            int bitstreamId = _bitstream.ReadBits(5);
            long frameDuration = 0;
            _bitstream.SkipBits(3);

            bool isDDPlus = (bitstreamId <= 16) && (bitstreamId > 10);

            if (!isDDPlus)
            {
                throw new DDPlusParserException("Not Implemented!");
            }
            else
            {

                if (fsCode  != 0x3)
                {
                    frequency = (int)(AudioDataTypesHelper.DDPlusSampleRateCodeToValue[fsCode ]);
                    frameDuration = AudioDataTypesHelper.DDPlusFrameDuration[fsCode, fsCode2];
                }
                else
                {
                    if (fsCode2 == 0x3)
                        throw new DDPlusParserException("Bad AC3 Audio header fsCode == fsCode2 == 0x3");

                    frequency = (int)(AudioDataTypesHelper.DDPlusSampleRateCodeToValue[fsCode2] >> 1); // 24, 22.05 or 16 kHz
                    frameDuration = AudioDataTypesHelper.DDPlusLowSampleRateFrameDuration[fsCode2];
                }

                frameSize = 2 * (frameSize + 1);

                if (streamType != 0 || subStreamId != 0)
                    throw new DDPlusParserException("Not supported case in DDPlus parser");

            }


            int channels = AudioDataTypesHelper.OutputModeToChannels[audioCodingMode] + lowFrequencyEffectFlag;
            int channelsMask = (int)AudioDataTypesHelper.ChannelMasks[audioCodingMode];
            if (lowFrequencyEffectFlag != 0)
            {
                channelsMask |= (int)AudioDataTypesHelper.SpeakerPositions.SPEAKER_LOW_FREQUENCY;
            }

            if (Description == null)
            {
                _waveFormatEx = new AudioDataTypesHelper.WAVEFORMATEXTENSIBLE();
                _waveFormatEx.Format.formatTag = 0xFFFE;
                _waveFormatEx.Format.channels = (ushort)channels;
                _waveFormatEx.dwChannelMask = channelsMask;
                _waveFormatEx.Samples = 0;
                
                // We initialize the audio codec with a minimum sampling rate of 44100. This is a workaround for an 
                // issue in xbox audio renderer which cannot handle a change in sampling frequency that is more than 
                // 2x, for example, it cannot handle switching from 11K audio to 44k audio. 
                _waveFormatEx.Format.samplesPerSec = (frequency >= 44100) ? frequency : 44100;

                // Next line generates a compile time error: 
                // CS0233: 'WAVEFORMATEXTENSIBLE' does not have a predefined size, therefore sizeof can only be used in an unsafe context. 
                // So we will use hard-coded size instead. Note that the _waveFormatEx is passed to MediaElement as an encoded Hex string.
                // _waveFormatEx.Format.cbSize = sizeof(AudioDataTypesHelper.WAVEFORMATEXTENSIBLE) - sizeof(AudioDataTypesHelper.WAVEFORMATEX);
                _waveFormatEx.Format.size = 2 + 4 + 16;
                _waveFormatEx.SubFormat = new Guid("{0xa7fb87af, 0x2d02, 0x42fb, {0xa4, 0xd4, 0x5, 0xcd, 0x93, 0x84, 0x3b, 0xdd}}");

                Dictionary<MediaStreamAttributeKeys, string> streamAttributes = new Dictionary<MediaStreamAttributeKeys, string>();
                streamAttributes[MediaStreamAttributeKeys.CodecPrivateData] = GetCodecPrivateData();
                Description = new MediaStreamDescription(MediaStreamType.Audio, streamAttributes);
            }

            BeginSample(frameSize, frameDuration, _PTSTimestampList[0]);
            _PTSTimestampList.RemoveAt(0);

            return 0;

        }

        /// <summary>
        /// Builds CodecPrivateData string that can be passed into MediaElement
        /// </summary>
        /// <returns></returns>
        private string GetCodecPrivateData()
        {
            StringBuilder cpdBuilder = new StringBuilder();

            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_waveFormatEx.Format.formatTag));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_waveFormatEx.Format.channels));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_waveFormatEx.Format.samplesPerSec));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_waveFormatEx.Format.avgBytesPerSec));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_waveFormatEx.Format.blockAlign));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_waveFormatEx.Format.bitsPerSample));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_waveFormatEx.Format.size));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForWord(_waveFormatEx.Samples));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForDWord(_waveFormatEx.dwChannelMask));
            cpdBuilder.Append(AudioDataTypesHelper.GetHexStringForGuid(_waveFormatEx.SubFormat));

  
            HLSTrace.WriteLine("DDPlus CodePrivateData is " + cpdBuilder.ToString());

            return cpdBuilder.ToString();
        }

    }

}

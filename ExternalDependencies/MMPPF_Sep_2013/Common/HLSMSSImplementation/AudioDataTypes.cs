using System;
using System.Text;


namespace Silverlight.Samples.HttpLiveStreaming
{
    public class AudioDataTypesHelper
    {

        // Format Tag for AAC or MPEG-4 HE-AAC v1/v2 streams with any payload (ADTS, ADIF, LOAS/LATM, RAW
        internal readonly static ushort WAVE_FORMAT_MPEG_HEAAC = 0x1610; 

        // Speaker Positions for dwChannelMask in WAVEFORMATEXTENSIBLE:
        internal enum SpeakerPositions : int
        {
            SPEAKER_FRONT_LEFT              = 0x1,
            SPEAKER_FRONT_RIGHT             = 0x2,
            SPEAKER_FRONT_CENTER            = 0x4,
            SPEAKER_LOW_FREQUENCY           = 0x8,
            SPEAKER_BACK_LEFT               = 0x10,
            SPEAKER_BACK_RIGHT              = 0x20,
            SPEAKER_FRONT_LEFT_OF_CENTER    = 0x40,
            SPEAKER_FRONT_RIGHT_OF_CENTER   = 0x80,
            SPEAKER_BACK_CENTER             = 0x100,
            SPEAKER_SIDE_LEFT               = 0x200,
            SPEAKER_SIDE_RIGHT              = 0x400,
            SPEAKER_TOP_CENTER              = 0x800,
            SPEAKER_TOP_FRONT_LEFT          = 0x1000,
            SPEAKER_TOP_FRONT_CENTER        = 0x2000,
            SPEAKER_TOP_FRONT_RIGHT         = 0x4000,
            SPEAKER_TOP_BACK_LEFT           = 0x8000,
            SPEAKER_TOP_BACK_CENTER         = 0x10000,
            SPEAKER_TOP_BACK_RIGHT          = 0x20000
        };

        /* output mode to number of output channels table */
        internal readonly static int[] OutputModeToChannels = { 2, 1, 2, 3, 3, 4, 4, 5 };

        internal readonly static SpeakerPositions[] ChannelMasks = 
        {  
            SpeakerPositions.SPEAKER_FRONT_LEFT | SpeakerPositions.SPEAKER_FRONT_RIGHT,
            SpeakerPositions.SPEAKER_FRONT_CENTER,
            SpeakerPositions.SPEAKER_FRONT_LEFT | SpeakerPositions.SPEAKER_FRONT_RIGHT,
            SpeakerPositions.SPEAKER_FRONT_LEFT | SpeakerPositions.SPEAKER_FRONT_RIGHT | SpeakerPositions.SPEAKER_FRONT_CENTER,
            SpeakerPositions.SPEAKER_FRONT_LEFT | SpeakerPositions.SPEAKER_FRONT_RIGHT | SpeakerPositions.SPEAKER_BACK_CENTER,
            SpeakerPositions.SPEAKER_FRONT_LEFT | SpeakerPositions.SPEAKER_FRONT_RIGHT | SpeakerPositions.SPEAKER_FRONT_CENTER | SpeakerPositions.SPEAKER_BACK_CENTER,
            SpeakerPositions.SPEAKER_FRONT_LEFT | SpeakerPositions.SPEAKER_FRONT_RIGHT | SpeakerPositions.SPEAKER_BACK_LEFT | SpeakerPositions.SPEAKER_BACK_RIGHT,
            SpeakerPositions.SPEAKER_FRONT_LEFT | SpeakerPositions.SPEAKER_FRONT_RIGHT | SpeakerPositions.SPEAKER_FRONT_CENTER | SpeakerPositions.SPEAKER_BACK_LEFT | SpeakerPositions.SPEAKER_BACK_RIGHT
        };

        internal struct WAVEFORMATEX
        {
            public ushort formatTag;         /* format type */
            public ushort channels;          /* number of channels (i.e. mono, stereo...) */
            public int    samplesPerSec;     /* sample rate */
            public int    avgBytesPerSec;    /* for buffer estimation */
            public ushort blockAlign;        /* block size of data */
            public ushort bitsPerSample;     /* number of bits per sample of mono data */
            public ushort size;              /* Specifies the size, in bytes, of the format data after the WAVEFORMATEX structure. */
        }


        internal struct WAVEFORMATEXTENSIBLE
        {
            public WAVEFORMATEX Format;
            public ushort Samples;
            public int dwChannelMask;      /* which channels are */
            public Guid SubFormat;
        }

        /* AC3 sample rate code to value table */
        internal readonly static ulong[] DDPlusSampleRateCodeToValue = { 48000, 44100, 32000 };

        // Duration in hundred nano seconds (HNS = 10^-7 sec) of one Dolby Digital Plus frame
        // for the lower sampling rate (which always has 1536 samples / frame)
        internal readonly static uint[] DDPlusLowSampleRateFrameDuration = { 640000, // 24 kHz
                                                                        696599, // 22.05 kHz
                                                                        960000, // 16 kHz
                                                                      };

        // Duration in hundred nano seconds (HNS = 10^-7 sec) of one Dolby frame, as a function
        // of sample rate and number of blocks per frame.
        // Index to this array is [fscod][numblkscod]
        internal readonly static uint[,] DDPlusFrameDuration = {  // '256 Samples (1 block)'   '512 samples (2 block)'  '768 samples (3 blocks)'  '1536 samples (6 blocks)'
                                                                    {   53333,                        106667,                     160000,                 320000    }, //  48 kHz
                                                                    {   58050,                        116100,                     174150,                 348299    }, //  44.1 kHz
                                                                    {   80000,                        160000,                     240000,                 480000    }, //  32 kHz
                                                               };
        // Number of bits in DDPlus frame
        internal readonly static ulong[,] DDPlusFrameSizeTab = 
        {
            /* 48kHz */
            {   64, 64, 80, 80, 96, 96, 112, 112,
                128, 128, 160, 160, 192, 192, 224, 224,
                256, 256, 320, 320, 384, 384, 448, 448,
                512, 512, 640, 640, 768, 768, 896, 896,
                1024, 1024, 1152, 1152, 1280, 1280 },
            /* 44.1kHz */
            {   69, 70, 87, 88, 104, 105, 121, 122,
                139, 140, 174, 175, 208, 209, 243, 244,
                278, 279, 348, 349, 417, 418, 487, 488,
                557, 558, 696, 697, 835, 836, 975, 976,
                1114, 1115, 1253, 1254, 1393, 1394 },
            /* 32kHz */
            {   96, 96, 120, 120, 144, 144, 168, 168,
                192, 192, 240, 240, 288, 288, 336, 336,
                384, 384, 480, 480, 576, 576, 672, 672,
                768, 768, 960, 960, 1152, 1152, 1344, 1344,
                1536, 1536, 1728, 1728, 1920, 1920 } 
        };

        /// <summary>
        /// Helper function
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        internal static char GetHexChar(byte b)
        {
            char c;

            if (b > 0x0F) throw new Exception("Out of Range");

            if (b <= 9 && b >= 0)
            {
                c = (char)(b + 0x30);
            }
            else
            {
                c = (char)((b - 10) + 0x41);
            }

            return c;
        }

        /// <summary>
        /// Helper function
        /// </summary>
        /// <param name="word"></param>
        /// <returns></returns>
        internal static string GetHexStringForWord(ushort word)
        {
            StringBuilder sb = new StringBuilder();
            //
            // Stored in little ending.
            // w = a.b.c.d  such as 0x 1601.
            // it will be converted as c.d.a.b
            //
            byte bTemp;

            bTemp = (byte)((word & 0x00F0) >> 4);
            sb.Append(GetHexChar(bTemp));

            bTemp = (byte)(word & 0x000F);
            sb.Append(GetHexChar(bTemp));

            bTemp = (byte)((word & 0xF000) >> 12);
            sb.Append(GetHexChar(bTemp));

            bTemp = (byte)((word & 0x0F00) >> 8);
            sb.Append(GetHexChar(bTemp));

            return sb.ToString();
        }

        /// <summary>
        /// Helper function
        /// </summary>
        /// <param name="dword"></param>
        /// <returns></returns>
        internal static string GetHexStringForDWord(int dword)
        {
            StringBuilder sb = new StringBuilder();
            ushort hword;
            ushort lword;

            lword = (ushort)(dword & 0x0000FFFF);

            sb.Append(GetHexStringForWord(lword));

            hword = (ushort)((dword & 0xFFFF0000) >> 16);

            sb.Append(GetHexStringForWord(hword));

            return sb.ToString();
        }

        /// Helper function
        /// </summary>
        /// <param name="dword"></param>
        /// <returns></returns>
        internal static string GetHexStringForGuid(Guid guid)
        {
            StringBuilder sb = new StringBuilder();

            byte[] guidByteArray = guid.ToByteArray();

            for (int i = 0; i < 16; ++i)
            {
                sb.Append(GetHexChar((byte)((guidByteArray[i] & 0xF0) >> 4)));
                sb.Append(GetHexChar((byte)(guidByteArray[i] & 0x0F)));
            }

            return sb.ToString();
        }
    }

}

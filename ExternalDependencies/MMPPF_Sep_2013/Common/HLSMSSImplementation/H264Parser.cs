using System.Collections.Generic;
using System.Windows.Media;
using System.Diagnostics;


namespace Silverlight.Samples.HttpLiveStreaming
{

    /// <summary>
    /// Concrete parser that breaks H.264 video stream into individual frames.
    /// </summary>
    public class H264Parser : MediaFormatParser
    {
        // NAL unit types we care about in this impl. See Table 7-1 in ISO/IEC 14496-10:2003(E) for details
        protected enum NALUnitType
        {
            IDRUnit             = 5,
            AccessUnitDelimiter = 9
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="msd"></param>
        public H264Parser(SampleBuffer outputBuffer, IContainerMetadata metadata, HLSStream hlsStream)
            : base(outputBuffer, hlsStream)
        {
            string[] resolution = null;

            string s;
            if (metadata.Attributes != null &&
                metadata.Attributes.TryGetValue(HLSPlaylistMetaKeys.Resolution, out s))
            {
                string[] components = s.Split(new char[] { 'x' });
                if (components != null && components.Length == 2)
                    resolution = components;
            }

            if (resolution == null)
            {
                HLSTrace.WriteLine("Missing 'Resolution' tag in HLS MetaKeys, defaulting to the maximum supported resolution of 1280x720.");
                resolution = new string[] { "1280", "720" };
            }

            Dictionary<MediaStreamAttributeKeys, string> streamAttributes = new Dictionary<MediaStreamAttributeKeys, string>();
            streamAttributes[MediaStreamAttributeKeys.VideoFourCC] = "H264";
            streamAttributes[MediaStreamAttributeKeys.Width] = resolution[0];
            streamAttributes[MediaStreamAttributeKeys.Height] = resolution[1];
            Description = new MediaStreamDescription(MediaStreamType.Video, streamAttributes);
        }

        /// <summary>
        /// Internal method for locating next NAL unit of specific type.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="end"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        private int FindNALUnitStart(byte[] data, int offset, int end, NALUnitType type)
        {
            for (int i = offset; i < end - 3; i++)
            {
                if (data[i] == 0 &&
                    data[i + 1] == 0 &&
                    data[i + 2] == 1 &&
                    (data[i + 3] & 0x1F) == (byte)type)
                {
                   if (i > offset && data[i - 1] == 0)
                        i--;
                   return i;
                }
            }
            return -1;
        }


        /// <summary>
        /// Overridable for parsing incoming chunk of H.264 stream.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        protected override int ParseData(byte[] data, int offset, int count)
        {
            int bytesToSample;
            int nalUnitStart = FindNALUnitStart(data, offset, offset + count, NALUnitType.AccessUnitDelimiter);

            // TODO: the TS format does not have duration for video, 
            // set a default duration 30fps for now, decoder/source should not use audio duration
            const long duration = 333666;

            if (nalUnitStart >= 0)
            {
                if (nalUnitStart > offset)
                {
                    if (FindNALUnitStart(data, offset, offset + nalUnitStart, NALUnitType.IDRUnit) != -1)
                        MarkReferenceSample();

                    AddToLastSample(nalUnitStart - offset);
                    return 0;
                }


                int nextNalUnitStart = FindNALUnitStart(data, offset + 4, offset + count, NALUnitType.AccessUnitDelimiter);
                
                if (nextNalUnitStart > 0)
                {
                    bytesToSample = nextNalUnitStart - nalUnitStart;
                }
                else
                {
                    bytesToSample = count;
                    for (int i = 0; i < 4  && offset + bytesToSample > 0; i ++)
                    {
                        if (data[offset + bytesToSample - 1] != 0  && data[offset + bytesToSample - 1] != 1)
                            break;
                        
                        bytesToSample --;
                    }
                }

                Debug.Assert(bytesToSample > 0);

                //lrj
                if (_PTSTimestampList != null && _PTSTimestampList.Count > 0)
                {
                    BeginSample(bytesToSample, duration, _PTSTimestampList[0]);
                    _PTSTimestampList.RemoveAt(0);


                    if (FindNALUnitStart(data, offset + 4, offset + bytesToSample, NALUnitType.IDRUnit) != -1)
                        MarkReferenceSample();
                }
                else
                {
                    //HLSTrace.WriteLine("HLS MSS Error: {0}", "_PTSTimestampList is null");
                }

                //BeginSample(bytesToSample, duration, _PTSTimestampList[0]);
                //_PTSTimestampList.RemoveAt(0);
                
                //if (FindNALUnitStart(data, offset + 4, offset + bytesToSample, NALUnitType.IDRUnit) != -1)
                //    MarkReferenceSample();

                return 0;
            }

            bytesToSample = count;
            for (int i = 0; i < 4 && offset + bytesToSample > 0; i++)
            {
                if (data[offset + bytesToSample - 1] != 0 && data[offset + bytesToSample - 1] != 1)
                    break;

                bytesToSample--;
            }

            if (bytesToSample > 0)
                AddToLastSample(bytesToSample);
            return 0;
        }

    }

}

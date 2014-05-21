using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;

namespace Silverlight.Samples.HttpLiveStreaming
{
    /// <summary>
    /// Exception type for Null parsing errors.
    /// </summary>
    public class NullParserException : Exception
    {
        public NullParserException(string message) :
            base(message)
        {
        }
    }

    /// <summary>
    /// Concrete parser that parses Null/AAC stream and generates samples.
    /// </summary>
    public class NullParser : MediaFormatParser
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="msd"></param>
        public NullParser(SampleBuffer outputBuffer)
            : base(outputBuffer, null)
        {
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
            // We accept all samples passed to this parser. 
            // We consume all the data passed in as header data by returning count, which means 
            // no data samples are actually generated and passed to the MediaStreamSource. 
            return count;
        }
    }

}

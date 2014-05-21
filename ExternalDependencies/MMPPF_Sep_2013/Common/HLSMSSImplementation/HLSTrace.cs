using System;
using System.Diagnostics;
using System.Threading;


namespace Silverlight.Samples.HttpLiveStreaming
{
    public class HLSTrace
    {
        private static Exception _lastException = null;
        private static Random _random = null;

        [Conditional("HLSMSS_TRACE"), Conditional("HLSMSS_TRACE_HIGH"), Conditional("HLSMSS_TRACE_LOW")]
        public static void PrintException(Exception e)
        {
            _lastException = e;
            WriteLineHigh("Hit exception: " + e.Message + "\n" + e.StackTrace);
        }

        [Conditional("HLSMSS_INJECTERROR")]
        public static void TestInjectRandomError ( string funName, float errorRadio )
        {
            if( _random == null )
            {
                _random = new Random();
            }
            const int randomRange = 1000000;
            int randomValue = _random.Next(0, randomRange);

            if (randomValue <= (int)(errorRadio * randomRange))
            {
                throw (new Exception(" Inject random exception for test purpose, in funciton " + funName ));
            }
        }

        private static void WriteLineCommon(string format, params object[] arg)
        {
            Debug.WriteLine("{0} ThreadID={1}({2}) {3}", DateTime.Now.ToString("hh:mm:ss.fff"), Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.Name, String.Format(format, arg));
        }

        [Conditional("HLSMSS_TRACE"), Conditional("HLSMSS_TRACE_HIGH"), Conditional("HLSMSS_TRACE_LOW")]
        public static void WriteLineHigh(string format, params object[] arg)
        {
            WriteLineCommon(format, arg);
        }

        [Conditional("HLSMSS_TRACE"), Conditional("HLSMSS_TRACE_LOW")]
        public static void WriteLine(string format, params object[] arg)
        {
            WriteLineCommon(format, arg);
        }

        [Conditional("HLSMSS_TRACE_LOW")]
        public static void WriteLineLow(string format, params object[] arg)
        {
            WriteLineCommon(format, arg);
        }

    }
}

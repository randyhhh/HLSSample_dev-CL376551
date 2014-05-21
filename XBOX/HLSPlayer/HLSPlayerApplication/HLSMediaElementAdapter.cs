using System;
using System.Collections.Generic;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Silverlight.Samples.HttpLiveStreaming;

namespace HLSPlayerApplication
{
    /// <summary>
    /// Implements an IMEA for HLS by extending the base adapter.
    /// </summary>
    public class HLSMediaElementAdapter : MediaElementAdapter
    {
        public HLSMediaStreamSource MediaStreamSource { get { return _mss; } }

        public HLSMediaStreamSourceOpenParam OpenParam
        {
            get { return _mss == null ? null : _mss.OpenParam; }
            set
            {
                _mss = new HLSMediaStreamSource(value);
                MediaElement.SetSource(_mss);
            }
        }

        protected override void DisposeCore()
        {
            if (_mss != null)
            {
                _mss.Dispose();
            }

            base.DisposeCore();
        }

        protected override bool IsLiveCore
        {
            get
            {
                if (_mss == null || _mss.Playback == null)
                    return false;

                return !_mss.Playback.IsEndList;
            }
        }

        HLSMediaStreamSource _mss;
    }
}

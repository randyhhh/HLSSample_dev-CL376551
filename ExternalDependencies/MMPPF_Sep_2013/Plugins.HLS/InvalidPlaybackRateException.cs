using System;
using Microsoft.SilverlightMediaFramework.Plugins.HLS.Resources;

namespace Microsoft.SilverlightMediaFramework.Plugins.HLS
{
    /// <summary>
    /// Represents errors that occur when a playback rate specified is not supported for the media item.
    /// </summary>
    public class InvalidPlaybackRateException : Exception
    {
        private readonly string _message;

        public InvalidPlaybackRateException(double invalidPlaybackRate)
            : this(invalidPlaybackRate, null)
        {
        }

        public InvalidPlaybackRateException(double invalidPlaybackRate, Exception innerException)
            : base(string.Empty, innerException)
        {
            _message = string.Format(HLSMediaPluginResources.UnsupportedPlaybackRateMessage, invalidPlaybackRate);
        }

        public override string Message
        {
            get { return _message; }
        }
    }
}
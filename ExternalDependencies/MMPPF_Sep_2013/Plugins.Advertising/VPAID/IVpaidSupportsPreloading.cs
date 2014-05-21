
using Microsoft.SilverlightMediaFramework.Plugins.Primitives;

namespace Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID
{
    public interface IVpaidSupportsPreloading : IVpaid
    {
        object CurrentAdContext { get; }
        void PreloadAd(double width, double height, string viewMode, int desiredBitrate, string creativeData, string environmentVariables, object appendToAdContext);
    }
}

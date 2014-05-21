using System;
using System.Windows;
using Microsoft.SilverlightMediaFramework.Utilities.Extensions;
using Microsoft.SilverlightMediaFramework.Plugins.Primitives;

namespace Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID
{
    /// <summary>
    /// A VPAID ad player that can play cips using an existing IMediaPlugin object retrieved from IPlayer.ActiveMediaPlugin using the ScheduleAd method
    /// </summary>
    public class AdClipLinearAdPlayerXbox : AdClipLinearAdPlayer
    {
        internal AdClipLinearAdPlayerXbox(ICreativeSource AdSource, IAdTarget AdTarget, IPlayer AdHost)
            : base(AdSource, AdTarget, AdHost)
        {
            ((Microsoft.SilverlightMediaFramework.Core.SMFPlayer)AdHost).PreBack += new EventHandler(OnAdUserClose);
        }

        public override void Dispose()
        {
            ((Microsoft.SilverlightMediaFramework.Core.SMFPlayer)AdHost).PreBack -= new EventHandler(OnAdUserClose);
            base.Dispose();
        }
    }
}

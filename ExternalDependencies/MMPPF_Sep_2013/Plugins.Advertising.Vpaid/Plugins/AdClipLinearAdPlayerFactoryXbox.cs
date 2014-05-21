using System;
using System.Windows;
using Microsoft.SilverlightMediaFramework.Plugins.Metadata;
using Microsoft.SilverlightMediaFramework.Utilities.Extensions;
using Microsoft.SilverlightMediaFramework.Plugins.Primitives.Advertising;
using System.ComponentModel.Composition;

namespace Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID.Plugins
{
    /// <summary>
    /// Provides an IVpaidFactory implementation for the AdClipLinearAdPlayer
    /// </summary>
    [PartCreationPolicy(CreationPolicy.NonShared)]
    [ExportGenericPlugin(PluginName = PluginName, PluginDescription = PluginDescription, PluginVersion = PluginVersion)]
    public class AdClipLinearAdPlayerFactoryXbox : AdClipLinearAdPlayerFactory
    {
        private const string PluginName = "AdClipLinearAdPlayerFactoryXbox";
        private const string PluginDescription = "An ad player capable of playing video ads inside the player's ActiveMediaPlugin.";
        private const string PluginVersion = "2.2012.1005.0";

        public override PriorityCriteriaEnum CheckSupport(ICreativeSource AdSource, IAdTarget AdTarget)
        {
            var result = base.CheckSupport(AdSource, AdTarget);
            if (result != PriorityCriteriaEnum.NotSupported)
            {
                result = result | PriorityCriteriaEnum.Trump;    // adding Trump to boost priority over the base class
            }
            return result;
        }

        public override IVpaid GetVpaidPlayer(ICreativeSource AdSource, IAdTarget AdTarget)
        {
            return new AdClipLinearAdPlayerXbox(AdSource, AdTarget, player);
        }
    }
}

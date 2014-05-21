using System;
using System.Windows;
using Microsoft.SilverlightMediaFramework.Plugins.Metadata;
using Microsoft.SilverlightMediaFramework.Utilities.Extensions;
using System.ComponentModel.Composition;

namespace Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID.Plugins
{
    /// <summary>
    /// Provides an IVpaidFactory implementation for the AdaptiveVideoAdPlayer
    /// </summary>
    [PartCreationPolicy(CreationPolicy.NonShared)]
    [ExportGenericPlugin(PluginName = PluginName, PluginDescription = PluginDescription, PluginVersion = PluginVersion)]
    public class AdaptiveVideoAdPlayerFactoryXbox : AdaptiveVideoAdPlayerFactory
    {
        private const string PluginName = "AdaptiveVideoAdPlayerFactoryXbox";
        private const string PluginDescription = "An ad player capable of playing adaptive video ads without dependencies on the underlying video type.";
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
            return new AdaptiveVideoAdPlayerXbox(AdSource, AdTarget, player as IDvrPlayer);
        }
    }

}

using System;
using System.Windows;
using Microsoft.SilverlightMediaFramework.Plugins.Metadata;
using Microsoft.SilverlightMediaFramework.Utilities.Extensions;
using System.ComponentModel.Composition;

namespace Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID
{
    /// <summary>
    /// Provides an IVpaidFactory implementation for the ImageAdPlayer
    /// </summary>
    [PartCreationPolicy(CreationPolicy.NonShared)]
    [ExportGenericPlugin(PluginName = PluginName, PluginDescription = PluginDescription, PluginVersion = PluginVersion)]
    public class ImageAdPlayerFactoryXbox : ImageAdPlayerFactory
    {
        private const string PluginName = "ImageAdPlayerFactoryXbox";
        private const string PluginDescription = "An ad player capable of showing an image as a linear ad";
        private const string PluginVersion = "2.2012.1005.0";
                
        public override PriorityCriteriaEnum CheckSupport(ICreativeSource AdSource, IAdTarget AdTarget)
        {
            var result = base.CheckSupport(AdSource, AdTarget);
            if (result != PriorityCriteriaEnum.NotSupported)
            {
                result = result | PriorityCriteriaEnum.Trump;    // adding Trump to boost priority over traditional ImageAdPlayer
            }
            return result;
        }

        public override IVpaid GetVpaidPlayer(ICreativeSource AdSource, IAdTarget AdTarget)
        {
            return new ImageAdPlayerXbox(AdSource, AdTarget, player as IDvrPlayer);
        }
    }
}

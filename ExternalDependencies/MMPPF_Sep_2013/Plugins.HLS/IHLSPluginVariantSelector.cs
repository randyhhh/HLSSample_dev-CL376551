using System;
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
using System.Collections.Generic;

namespace Microsoft.SilverlightMediaFramework.Plugins.HLS
{
    public interface IHLSPluginVariantSelector : IVariantSelector
    {
        void SelectVariant(HLSVariant previousVariant, HLSVariant heuristicSuggestedVariant, ref HLSVariant nextVariant, List<HLSVariant> availableSortedVariants);

        List<HLSVariant> Variants { get; }
    }
}

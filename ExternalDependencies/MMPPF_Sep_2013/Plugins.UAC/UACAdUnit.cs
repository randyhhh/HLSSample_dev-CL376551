using System.Collections.Generic;
using Microsoft.Advertising;
using Microsoft.SilverlightMediaFramework.Plugins.Advertising.VAST;
using Microsoft.SilverlightMediaFramework.Plugins.Primitives.Advertising;

namespace Microsoft.SilverlightMediaFramework.Plugins.UAC
{
    /// <summary>
    /// The individual ad unit for a single VAST document.
    /// </summary>
    public class UACAdUnit : AdUnitBase
    {
        /// <summary>
        /// Creates a new VastAdUnit to serve as a handle to the running ad
        /// </summary>
        /// <param name="Source">The source associated with this ad</param>
        internal UACAdUnit(IAdSource Source)
            : base(Source)
        {
            Packages = ((PackageAdSource)Source).Packages;
        }

        /// <summary>
        /// The UAC ad package that the source refered to. This may be null until populated depending on if the manifest was loaded at the time it was scheduled or if it needs to be created from a uri.
        /// </summary>
        public IList<AdPackage> Packages { get; internal set; }

        /// <summary>
        /// Returns the UAC ad pod associated with this ad unit
        /// </summary>
        public new UACAdPod AdPod
        {
            get { return base.AdPod as UACAdPod; }
            set { base.AdPod = value; }
        }
    }
}
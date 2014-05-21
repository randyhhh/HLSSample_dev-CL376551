using System.Linq;
using System.Collections.Generic;
using Microsoft.Advertising;
using Microsoft.SilverlightMediaFramework.Plugins.Primitives.Advertising;

namespace Microsoft.SilverlightMediaFramework.Plugins.UAC
{
    public class PackageAdSource : IAdSequencingSource
    {
        private AdPod m_adpod;

        public PackageAdSource(AdPod pod)
        {
            m_adpod = pod;
        }

        public IList<AdPackage> Packages
        {
            get 
            {
                IList<AdPackage> packages = new List<AdPackage>();
                foreach (var package in m_adpod.AdPackages)
                {
                    if (package.Error == null)
                    {
                        packages.Add(package);
                    }
                }

                return packages; 
            }
        }

        public IEnumerable<IAdSequencingSource> Sources
        {
            get { return Enumerable.Empty<IAdSequencingSource>(); }
        }

        public string Format { get; set; }

        public string Uri { get; set; }

        public string AltReference { get; set; }

        public IEnumerable<IAdSequencingTarget> Targets
        {
            get { return Enumerable.Empty<IAdSequencingTarget>(); }
        }
    }
}

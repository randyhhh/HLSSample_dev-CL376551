using System;
using System.Collections.Generic;
using Microsoft.Advertising;
using Microsoft.SilverlightMediaFramework.Plugins.Primitives.Advertising;

namespace Microsoft.SilverlightMediaFramework.Plugins.UAC
{
    public class PackageAdTrigger : IAdSequencingTrigger
    {
        public PackageAdTrigger(AdPod pod, string HandlerId, TimeSpan StartTime)
        {
            Id = Guid.NewGuid().ToString();
            Description = string.Empty;
            Source = new PackageAdSource(pod) { Format = HandlerId };
            this.StartTime = StartTime;
        }

        public TimeSpan StartTime { get; private set; }

        PackageAdSource source;
        /// <summary>
        /// The ad source for the trigger.
        /// </summary>
        public PackageAdSource Source
        {
            get { return source; }
            private set
            {
                source = value;
                sources = new[] { source };
            }
        }

        public string Id { get; private set; }

        public string Description { get; private set; }

        private IAdSequencingSource[] sources;
        IEnumerable<IAdSequencingSource> IAdSequencingTrigger.Sources
        {
            get { return sources; }
        }

        TimeSpan? IAdSequencingTrigger.Duration
        {
            get { return null; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Advertising;
using Microsoft.SilverlightMediaFramework.Plugins.Advertising.VAST;
using Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID;

namespace Microsoft.SilverlightMediaFramework.Plugins.UAC
{
    /// <summary>
    /// Represents a single VAST document, which can contain many Ads.
    /// </summary>
    public class UACAdPod : Microsoft.SilverlightMediaFramework.Plugins.Advertising.VAST.AdPod
    {
        /// <summary>
        /// A reference to a factory that can help create and populate these models at the appropriate times.
        /// </summary>
        internal UACCreativeFactory AdModelFactory { get; private set; }

        /// <summary>
        /// The VAST AdUnit associated with this model. This object provides information about the source used to load this AdPod.
        /// </summary>
        public UACAdUnit AdUnit { get; private set; }

        public UACAdPod(UACAdUnit AdUnit, UACCreativeFactory AdModelFactory)
            : base()
        {
            this.AdUnit = AdUnit;
            this.AdModelFactory = AdModelFactory;
        }

        /// <summary>
        /// Starts loading the AdPod. This calls out to the AdModelFactory to do much of the actual downloading and processing of wrapper ads.
        /// </summary>
        /// <param name="Completed">Callback to notify when we are finished. Passes a success param.</param>
        public void LoadAsync(Action<bool> Completed)
        {
            // now that we're done loading wrappers, add the Ads. Note: VastAd can only hold inline ads
            bool itemAdded = false;
            foreach (var package in AdUnit.Packages)
            {
                base.Ads.Add(new UACAd(package, this));
                itemAdded = true;
            }
            Completed(itemAdded);
        }
    }

    /// <summary>
    /// Represents a single VAST ad. Cooresponds to the Ad node of the VAST document.
    /// </summary>
    public class UACAd : Microsoft.SilverlightMediaFramework.Plugins.Advertising.VAST.Ad
    {
        /// <summary>
        /// A reference to the parent AdPod.
        /// </summary>
        internal UACAdPod ParentAdPod { get; private set; }

        /// <summary>
        /// The VAST Ad model that came directly from the Ad node of the VAST document.
        /// </summary>
        public AdPackage Ad { get; private set; }

        internal UACAd(AdPackage Ad, UACAdPod Parent)
            : base()
        {
            this.Ad = Ad;
            this.ParentAdPod = Parent;
            this.FailureStrategy = Parent.FailurePolicy;
            this.CloseCompanionsOnComplete = Parent.CloseCompanionsOnComplete;

            // for now we just have a single linear ad per ad.
            // in a full VAST implemenation we might have groups based on sequence numbers.
            base.CreativeSets.Add(new UACCreativeSet(new AdPackage[] { Ad }, this));
        }
    }

    /// <summary>
    /// Represents a group of creatives intended to play at the same time.
    /// </summary>
    public class UACCreativeSet : CreativeSet
    {
        /// <summary>
        /// A reference to the parent Ad.
        /// </summary>
        internal UACAd ParentAd { get; private set; }

        /// <summary>
        /// A collection of inline creatives with the same sequence number. These come directly from the deserialized VAST document.
        /// </summary>
        public IEnumerable<AdPackage> InlineCreatives { get; private set; }

        internal UACCreativeSet(IEnumerable<AdPackage> Creatives, UACAd Parent)
            : base()
        {
            this.InlineCreatives = Creatives;
            this.ParentAd = Parent;
            this.FailureStrategy = Parent.FailureStrategy;
            foreach (var item in InlineCreatives)
            {
                base.Creatives.Add(new LinearUACCreative(item, this));
            }
        }

        /// <summary>
        /// Indicates whether or not the creative set contains a linear creative.
        /// </summary>
        public bool ContainsLinear
        {
            get
            {
                return InlineCreatives.Count() > 0;
            }
        }

        /// <summary>
        /// Filters a list of creatives based on target dependencies.
        /// If one creative uses a target that has a dependency on another target, only permit the creative if the dependency target was used.
        /// </summary>
        /// <param name="Creatives">The list of creatives to filter.</param>
        /// <returns>The filtered list of creatives.</returns>
        protected override IEnumerable<Creative> GetAllowedCreatives(IEnumerable<Creative> Creatives)
        {
            var usedTargets = Creatives.OfType<UACCreative>().Select(c => c.ActiveCreative.Target.TargetSource).ToList();
            foreach (var Creative in Creatives)
            {
                // only run creatives that have successfull dependency targets
                if (Creative is UACCreative)
                {
                    var creative = Creative as UACCreative;
                    IAdTarget target = creative.ActiveCreative.Target;

                    bool dependencyFailure = false;
                    foreach (var targetSource in target.TargetDependencies)
                    {
                        if (!usedTargets.Contains(targetSource))
                        {
                            dependencyFailure = true;
                            break;
                        }
                    }
                    if (!dependencyFailure)
                    {
                        yield return Creative;
                    }
                }
                else
                {
                    yield return Creative;
                }
            }
        }
    }

    /// <summary>
    /// A creative for a linear ad
    /// </summary>
    public class LinearUACCreative : UACCreative
    {
        internal LinearUACCreative(AdPackage creative, UACCreativeSet parent)
            : base(creative, parent)
        { }

        /// <summary>
        /// Always returns true. Linear ads are expected to have durations and control the lifespan.
        /// </summary>
        public override bool ControlsLifespan
        {
            get { return true; }
        }

        bool Load(bool addToTarget)
        {
            ActiveCreative = AdModelFactory.GetLinearCreative(
                base.Creative, base.ParentCreativeSet.ParentAd.ParentAdPod.AdUnit.Source, addToTarget);

            if (ActiveCreative != null)
            {
                id = ActiveCreative.Source.Id;
            }
            return (ActiveCreative != null);
        }

        public override bool PreLoad()
        {
            return Load(false);
        }

        /// <summary>
        /// Loads the ActiveCreative.
        /// </summary>
        /// <returns>Indicates success.</returns>
        public override bool Load()
        {
            if (ActiveCreative == null)
            {
                return Load(true);
            }
            else
            {
                //put video in target
                return ActiveCreative.Target.AddChild(ActiveCreative.Player);
            }
        }

        private string id;
        public override string Id
        {
            get
            {
                return id;
            }
        }
    }

    /// <summary>
    /// The base class for a creative
    /// </summary>
    public abstract class UACCreative : Creative
    {
        /// <summary>
        /// The deserialized model from the VAST document.
        /// </summary>
        internal protected AdPackage Creative { get; private set; }

        /// <summary>
        /// An object that represents a running creative.
        /// </summary>
        public ActiveCreative ActiveCreative { get; protected set; }

        /// <summary>
        /// The creative set that creative belongs to.
        /// </summary>
        public UACCreativeSet ParentCreativeSet { get; private set; }

        internal UACCreative(AdPackage creative, UACCreativeSet parent)
        {
            this.ParentCreativeSet = parent;
            this.Creative = creative;
        }

        /// <summary>
        /// Actually runs the creative.
        /// </summary>
        public override void RunAsync()
        {
            AdModelFactory.AdHandler.PlayCreative(this);
        }

        /// <summary>
        /// Cancels the running creative.
        /// </summary>
        public override void Cancel()
        {
            AdModelFactory.AdHandler.CancelCreative(this);
            Failed();
        }

        public override string Id
        {
            get { return null; }
        }

        /// <summary>
        /// Shortcut to the AdModelFactory.
        /// Should be protected AND internal but no way in C# to indicate this. 'protected internal' notation does OR.
        /// </summary>
        internal UACCreativeFactory AdModelFactory
        {
            get
            {
                return ParentCreativeSet.ParentAd.ParentAdPod.AdModelFactory;
            }
        }
    }
}
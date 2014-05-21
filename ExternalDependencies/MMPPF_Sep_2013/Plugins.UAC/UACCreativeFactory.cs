using System;
using System.Linq;
using Microsoft.Advertising;
using Microsoft.SilverlightMediaFramework.Plugins.Advertising.VPAID;
using Microsoft.SilverlightMediaFramework.Plugins.Primitives.Advertising;

namespace Microsoft.SilverlightMediaFramework.Plugins.UAC
{
    /// <summary>
    /// Provides a way for the ad models to load creatives
    /// </summary>
    public class UACCreativeFactory
    {
        public UACCreativeFactory(AdHandler AdHandler)
        {
            adHandler = AdHandler;
        }

        readonly AdHandler adHandler;
        public AdHandler AdHandler { get { return adHandler; } }

        public ActiveCreative GetLinearCreative(AdPackage package, IAdSource AdSource, bool addToTarget)
        {
            //get the target. In this case because it is a linear ad, it will aways be the player's main adcontainer
            var target = AdHandler.FindTarget(AdSource, new AdCreativeSource(null, null));
            if (target == null)
            {
                return null;
            }
            else
            {
                return GetLinearCreative(target, package, addToTarget);
            }
        }

        ActiveCreative GetLinearCreative(IAdTarget adTarget, AdPackage pkgElement, bool addToTarget)
        {
            IVpaid adPlayer = null;
            ICreativeSource adSource = null ;
            var vPaidFactories = AdHandler.GetVpaidFactories().ToList();
            do
            {
                // get a list of all eligible media files
                var mediaAdSources = pkgElement.VideoResources.ToDictionary(
                    m => m as VideoResource, m => new AdCreativeSource(pkgElement, m));

                var rankedMedia =
                    (from mediaAdSource in mediaAdSources
                     let vPaidFactoryAndPriority = vPaidFactories.ToDictionary(f => f, 
                     f => f.CheckSupport(mediaAdSource.Value, adTarget))
                     where vPaidFactoryAndPriority.Values.Any(v => v > PriorityCriteriaEnum.NotSupported)
                     let BestVpaidFactoryAndPriority = vPaidFactoryAndPriority.OrderByDescending(kvp => kvp.Value).First()
                     let rank = BestVpaidFactoryAndPriority.Value
                     orderby rank descending
                     select new
                     {
                         MediaFile = mediaAdSource.Key,
                         AdSource = mediaAdSource.Value,
                         VpaidFactory = BestVpaidFactoryAndPriority.Key,
                         Rank = rank,
                     }).ToList();

                if (rankedMedia.Any())
                {
                    // get all media with the best rankings
                    var topRank = rankedMedia.First().Rank;
                    var bestMedia = rankedMedia.Where(m => m.Rank == topRank);

                    // favor adaptive media if IsSmoothEnabled flag is set. Default is true.
                    var adaptiveMedia = bestMedia.Where(m => m.AdSource.IsStreaming);
                    var nonAdaptiveMedia = bestMedia.Except(adaptiveMedia);
                    if (AdHandler.IsSmoothEnabled)
                    {
                        if (adaptiveMedia.Any()) bestMedia = adaptiveMedia;
                    }
                    else
                    {
                        if (nonAdaptiveMedia.Any()) bestMedia = nonAdaptiveMedia;
                    }

                    double targetBitrateKbps = (double)AdHandler.AdHost.PlaybackBitrate / 1024;
                    if (targetBitrateKbps == 0.0)
                    {
                        // progressive videos won't have a bitrate. Therefore, target based on the one in the middle
                        targetBitrateKbps = rankedMedia.Average(m => m.MediaFile.Bitrate);
                    }

                    // get the media with the closest bitrate
                    var bitrateMedia = bestMedia
                        .GroupBy(m => Math.Abs(m.MediaFile.Bitrate))
                        .OrderBy(m => m.Key <= AdHandler.MaxBitrateKbps ? 0 : m.Key - AdHandler.MaxBitrateKbps)
                        .ThenBy(m => Math.Abs(m.Key - targetBitrateKbps))
                        .FirstOrDefault();
                    if (bitrateMedia != null && bitrateMedia.Any())
                        bestMedia = bitrateMedia;

                    // get the media with the closest size
                    var sizedMedia =
                        from m in bestMedia
                        let x = AdHandler.AdHost.VideoArea.ActualHeight - m.MediaFile.Height
                        let y = AdHandler.AdHost.VideoArea.ActualWidth - m.MediaFile.Width
                        let delta = x + y
                        orderby delta
                        select new { Media = m, DeltaX = x, DeltaY = y };

                    // try to get the one with the closest size but both dimensions are within the current size
                    var selectedMedia = sizedMedia.Where(sm => sm.DeltaX >= 0 && sm.DeltaY >= 0).Select(sm => sm.Media).FirstOrDefault();
                    if (selectedMedia == null) // couldn't find one, instead get one with the closest size where only one dimension is over the current size
                        selectedMedia = sizedMedia.Where(sm => sm.DeltaX >= 0 || sm.DeltaY >= 0).Select(sm => sm.Media).FirstOrDefault();
                    if (selectedMedia == null) // couldn't find one, instead get one with the closest size
                        selectedMedia = sizedMedia.Select(sm => sm.Media).LastOrDefault();

                    // see if there were any bitrates, if not grab which ever one was first
                    if (selectedMedia == null)
                        selectedMedia = bestMedia.First();

                    //finally, get the ad player
                    adSource = selectedMedia.AdSource;
                    adPlayer = selectedMedia.VpaidFactory.GetVpaidPlayer(adSource, adTarget);

                    if (adPlayer == null)
                    {
                        //Log.Output(OutputType.Error, "Error - cannot find player to support video ad content. ");
                        // this should never happen and is the result of a bad VPaid plugin.
                        throw new Exception("VpaidFactory agreed to accept AdSource and then returned null during call to GetVPaidPlugin.");
                    }
                    // handshake with the ad player to make sure the version of VPAID is OK
                    if (adPlayer == null || !adHandler.VpaidController.Handshake(adPlayer))
                    {
                        // the version is no good, remove the factory from the list and try again.
                        vPaidFactories.Remove(selectedMedia.VpaidFactory);
                        if (!vPaidFactories.Any())
                        {
                            return null;
                        }
                        adPlayer = null;
                    }
                }
                else
                {
                    return null;
                }
            } while (adPlayer == null);

            if (addToTarget)
            {
                //put video in target
                if (!adTarget.AddChild(adPlayer))
                {
                    return null;
                }
            }

            return new ActiveCreative(adPlayer, adSource, adTarget);
        }
    }
}

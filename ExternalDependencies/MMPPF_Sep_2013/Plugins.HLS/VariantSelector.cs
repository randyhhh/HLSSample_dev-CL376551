using System;
using System.Collections.Generic;
using System.Diagnostics;
using Silverlight.Samples.HttpLiveStreaming;

namespace Microsoft.SilverlightMediaFramework.Plugins.HLS
{
    /// <summary>
    /// User commands used for manual bitrate switching
    /// </summary>
    internal enum BitrateCommand
    {
        IncreaseBitrate,
        DecreaseBitrate,
        Random,
        Auto,
        DoNotChange
    }

    internal class VariantSelector : IVariantSelector
    {
        public VariantSelector(BitrateCommand BitrateCommand)
        {
            // Set initial bitrate program
            _bitrateCommand = BitrateCommand;
        }

        /// <summary>
        /// Sorted list of stream variants available for playback
        /// </summary>
        private List<HLSVariant> _sortedAvailableVariants;

        private volatile BitrateCommand _bitrateCommand = BitrateCommand.DoNotChange;

        /// <summary>
        /// Simulation for adaptive bitrate switching
        /// </summary>
        private class HLSVariantBitrateComparer : IComparer<HLSVariant>
        {
            public int Compare(HLSVariant x, HLSVariant y)
            {
                if (x == null)
                    return 1;

                if (y == null)
                    return -1;

                return (x.Bitrate == y.Bitrate) ? 0 : ((x.Bitrate < y.Bitrate) ? -1 : 1);
            }
        }

        /// <summary>
        /// Interface for callback to application, which allows the application to select the next HLS variant to 
        /// be downloaded and played. 
        /// <param name="previousVariant"> HLS variant that the previous segment was downloaded from </param>
        /// <param name="heuristicSuggestedVariant"> HLS variant that is suggsted by heuritstics algorithm to 
        /// be downloaded for next segment </param>
        /// <param name="nextVariant"> HLS variant to be used for downloading next segment </param>
        /// <param name="availableVariants"> A list of varients that are avaiable to application to select from,
        /// sorted by bitrate in increasing order </param>
        /// </summary>
        void IVariantSelector.SelectVariant(HLSVariant previousVariant, HLSVariant heuristicSuggestedVariant, ref HLSVariant nextVariant, List<HLSVariant> availableSortedVariants)
        {
            if (_sortedAvailableVariants == null)
            {
                _sortedAvailableVariants = new List<HLSVariant>(availableSortedVariants);

                // Handle cases that the playlist contains variants with the same program ID and same bandwidth.
                // We will just keep the first variant we see, and remove any variant that has the same bandwidth.
                for (int i = 0; i < _sortedAvailableVariants.Count; i++)
                {
                    for (int j = i + 1; j < _sortedAvailableVariants.Count; j++)
                    {
                        Debug.Assert(_sortedAvailableVariants[i].ProgramId == _sortedAvailableVariants[j].ProgramId, "The HLS Sample does not support playlists with different program IDs");
                        if (_sortedAvailableVariants[i].Bitrate == _sortedAvailableVariants[j].Bitrate)
                            _sortedAvailableVariants.RemoveAt(j);
                    }
                }

                HLSVariantBitrateComparer bitrateComparer = new HLSVariantBitrateComparer();
                _sortedAvailableVariants.Sort(bitrateComparer);

                while (_sortedAvailableVariants.Count > 0)
                {
                    // We assume any variant that its bitrate is lower than 100,000 is audio only
                    if (_sortedAvailableVariants[0].Bitrate != 0 && _sortedAvailableVariants[0].Bitrate < 90000)
                        _sortedAvailableVariants.RemoveAt(0);
                    else
                        break;
                }
            }

            if (!_sortedAvailableVariants.Contains(heuristicSuggestedVariant))
            {
                int i;
                for (i = 0; i < _sortedAvailableVariants.Count - 1; i++)
                {
                    if (heuristicSuggestedVariant.Bitrate < _sortedAvailableVariants[i].Bitrate ||
                       (heuristicSuggestedVariant.Bitrate >= _sortedAvailableVariants[i].Bitrate && heuristicSuggestedVariant.Bitrate < _sortedAvailableVariants[i + 1].Bitrate))
                        break;
                }

                heuristicSuggestedVariant = _sortedAvailableVariants[i];
            }

            if (previousVariant == null)
            {
                // If this is the first segment, default to the variant suggested by the heuristics algorithm
                nextVariant = heuristicSuggestedVariant;
                return;
            }

            nextVariant = heuristicSuggestedVariant;
        }


        public List<HLSVariant> Variants { get; set; }
    }

}

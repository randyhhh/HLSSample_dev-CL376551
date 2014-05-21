using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Xbox.Controls;
using Silverlight.Samples.HttpLiveStreaming;
using System.Collections.Generic;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Input;
using System.Diagnostics;
using System.Windows.Documents;

namespace HLSPlayerApplication
{
    
    public partial class FullScreenPlayer : Page, IVariantSelector
    {
        private Uri _sourceURI;

        /// <summary>
        /// Use 30 second buffer
        /// </summary>
        protected readonly TimeSpan BufferLength = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Parser for 608/708 closed caption (CC) data
        /// </summary>
        private CC608Parser _ccParser = new CC608Parser();

        /// <summary>
        /// Flag indicating if closed captioning is enabled.
        /// </summary>
        private bool _isCCEnabled = false; 

        /// <summary>
        /// Constructor
        /// </summary>
        public FullScreenPlayer()
        {
            InitializeComponent();
        }

        private static bool _isFirstPlay = true;

        /// <summary>
        /// Executes when the user navigates to this page.
        /// </summary>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            IDictionary<string, string> queryString = this.NavigationContext.QueryString;

            if (queryString.ContainsKey("SourceURI"))
            {
                _sourceURI = new Uri(queryString["SourceURI"], UriKind.RelativeOrAbsolute);
            }
            else
            {
                System.Diagnostics.Debug.Assert(false, "FullScreenPlayer.OnNavigatedTo: SourceURI is not set.");
                NavigationService.GoBack();
            }

            HLSMediaStreamSourceOpenParam openParam = new HLSMediaStreamSourceOpenParam();
            openParam.uri = _sourceURI;

            if (queryString.ContainsKey("AlignAfterSeek"))
            {
                bool.TryParse(queryString["AlignAfterSeek"], out openParam.ifAlignBufferAfterSeek);
            }

            //
            // Set initial bandwidth to 1Mb for first time of playback, following playback doesn't need to
            // set initial bandwidth so that HLSMSS will use its bandwidth history across playback sessions.
            // A real production implementation would be:
            // 1. Use mediaElement1.MediaStreamSource.BandwidthHistory.GetAverageBandwidth to retrieve runtime 
            // average bandwidth, and presistent it to disk or cloud with certian expiration threshold when exiting
            // app.
            // 2. Read the average bandwidth data from disk or cloud, then set initial bandwidth when first video
            // is played.
            // 3. If app has the intelligence of which group of URl is fast, which group is slower. Then app can 
            // maintain its own bandwidth tracking logic to always set initial bandwidth for each playback session
            //
            if (_isFirstPlay)
            {
                _isFirstPlay = false;
                openParam.initialBandwidth = 1024 * 1024;
            }

            mediaElementAdapter.OpenParam = openParam;

            mediaElementAdapter.MediaElement.MediaFailed += mediaElementAdapter_MediaFailed;
            mediaElementAdapter.MediaElement.MediaEnded += mediaElementAdapter_MediaEnded;
            mediaElementAdapter.MediaElement.BufferingProgressChanged += mediaElementAdapter_BufferingProgressChanged;

            // Optional initialization
            mediaElementAdapter.MediaStreamSource.Playback.DownloadBitrateChanged += Async_DownloadBitrateChanged;
            mediaElementAdapter.MediaStreamSource.Playback.PlaybackBitrateChanged += Async_PlaybackBitrateChanged;
            mediaElementAdapter.MediaStreamSource.Playback.MediaFileChanged += Playback_MediaFileChanged_Async;

            mediaElementAdapter.MediaStreamSource.BufferLength = BufferLength;
            mediaElementAdapter.MediaStreamSource.Playback.VariantSelector = this;

            
            // Enable 608/708 closed caption  
            Microsoft.Xbox.Media.MediaCapabilities.Enable708Captioning = true;

            // Start playback
            mediaElementAdapter.Play();

            textBlockMessage.Visibility = Visibility.Collapsed;
            // Set up timer for display updates
            DispatcherTimer dt = new DispatcherTimer();
            dt.Interval = new TimeSpan(0, 0, 0, 0, 500);
            dt.Tick += new EventHandler(Timer_Tick);
            dt.Start();

            _bitrateCommand = BitrateCommand.Auto;
            userCommandBitrateTextBlock.Text = "HLS bitrate is set to Auto";

            base.OnNavigatedTo(e);
        }

        /// <summary>
        /// Event handler for updating the closed caption text rendered on the screen. This 
        /// event is triggered by the 608parser whenever the rendered closed caption text 
        /// needs to be updated. The parsed closed caption data are passed back as a collection
        /// of text Runs which can be just added to a TextBlock inlines. These text runs may include 
        /// color information or text decorations and style such as italic or underlined. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void _ccParser_CCUpdateEvent(object sender, CC608Parser.CCUpdateEventArgs e)
        {
            // The parsed closed caption data are passed back as a collection of text Runs
            // which can be just added a text block inlines. 
            ClosedCaptionTextBlock.Inlines.Clear();

            foreach (Run r in e.TextRunList)
            {
                ClosedCaptionTextBlock.Inlines.Add(r);
            }
        }

        /// <summary>
        /// Event handler to receive marker events for closed caption data. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void mediaElementAdapter_MarkerReached(object sender, TimelineMarkerRoutedEventArgs e)
        {
            HLSTrace.WriteLine("Time = {0}, Type = {1}, Text = {2}",  e.Marker.Time.ToString(), e.Marker.Type.ToString(), e.Marker.Text);

            // The closed caption data are marked with type "CC708" and are encoded as base 64 strings. 
            if (e.Marker.Type.ToString() == "CC708")
            {
                byte[] ccEncodedData = System.Convert.FromBase64String(e.Marker.Text);
                _ccParser.Parse(ccEncodedData); 
            }
        }


        /// <summary>
        /// Sorted list of stream variants available for playback
        /// </summary>
        private List<HLSVariant> _sortedAvailableVariants;

        /// <summary>
        /// User commands used for manual bitrate switching
        /// </summary>
        private enum BitrateCommand
        {
            IncreaseBitrate,
            DecreaseBitrate,
            Random,
            Auto,
            DoNotChange
        }
        
        private volatile BitrateCommand _bitrateCommand;

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
                    if (_sortedAvailableVariants[0].Bitrate != 0 && _sortedAvailableVariants[0].Bitrate < 100000)
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

            // Select the next variant to play based on manual user input
            switch (_bitrateCommand)
            {
                case BitrateCommand.IncreaseBitrate:
                    foreach (HLSVariant variant in _sortedAvailableVariants)
                    {
                        if (variant.Bitrate > previousVariant.Bitrate)
                        {
                            nextVariant = variant;
                            break;
                        }
                    }

                    _bitrateCommand = BitrateCommand.DoNotChange;
                    break;
                
                case BitrateCommand.DecreaseBitrate:
                    foreach (HLSVariant variant in _sortedAvailableVariants)
                    {
                        if (variant.Bitrate < previousVariant.Bitrate)
                            nextVariant = variant;
                        else
                            break;
                    }

                    _bitrateCommand = BitrateCommand.DoNotChange;
                    break;

                case BitrateCommand.Random:
                    Random randomGenerator = new Random();
                    nextVariant = _sortedAvailableVariants[randomGenerator.Next(0, _sortedAvailableVariants.Count - 1)];
                    break;

                case BitrateCommand.Auto:
                    // use the variant that is suggested by the MSS built in heuristics algorithm
                    nextVariant = heuristicSuggestedVariant;
                    break;

                case BitrateCommand.DoNotChange:
                    // use the previous varient. don't change the stream
                    nextVariant = previousVariant;
                    break;
            }

            

            HLSTrace.WriteLine("Now downloading from {0} bps variant", nextVariant.Bitrate.ToString("#,##;(#,##)"));
        }

        /// <summary>
        /// Initialization
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LayoutRoot_Loaded(object sender, RoutedEventArgs e)
        {
            this.GamePadButtonDown += new EventHandler<GamePadButtonEventArgs>(FullScreenPlayer_GamePadButtonDown);

            // This will allow the Page control to receive the button events. 
            this.IsTabStop = true;
            
            // This will set the focus to the MediaTransport control so that it receives the gamepad and remote control events. Any button 
            // not handled by MediaTransport control will be routed to the Page control. 
            MediaTransport.Focus();

        }

        /// <summary>
        /// Updates buffer level display on timer
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Timer_Tick(object sender, EventArgs e)
        {
            string timeString;
            if (mediaElementAdapter.MediaStreamSource.HasProgramDateTime)
            {
                DateTime programTime = mediaElementAdapter.MediaStreamSource.GetProgramTime(mediaElementAdapter.MediaElement.Position);
                timeString = " Program time:" + programTime.ToShortDateString() + " " + programTime.ToLongTimeString();
            }
            else
            {
                timeString = " Playback time:" + mediaElementAdapter.MediaElement.Position.ToString(@"dd\-hh\:mm\:ss");
            }

            bufferLevelTextBlock.Text = "Buffer Level: " +
                            ((int)(mediaElementAdapter.MediaStreamSource.BufferLevel.TotalMilliseconds * 100 / BufferLength.TotalMilliseconds)).ToString() + "% " +
                            ((int)mediaElementAdapter.MediaStreamSource.BufferLevel.TotalMilliseconds).ToString() + "ms " +
                            (mediaElementAdapter.MediaStreamSource.BufferLevelInBytes / 1024).ToString() + "KB " +
                            " Bandwidth:" + mediaElementAdapter.MediaStreamSource.BandwidthHistory.GetAverageBandwidth().ToString("#,##;(#,##)") + " bps " +
                            (_isCCEnabled ? "CC is enabled" : "CC is disabled") + timeString;
                             

            if (_sortedAvailableVariants != null)
            {
                availableBitratesText.Text = " There are " + _sortedAvailableVariants.Count.ToString() + " stream variants available: ";
                foreach (HLSVariant variant in _sortedAvailableVariants)
                    availableBitratesText.Text += variant.Bitrate.ToString("#,##;(#,##)") + " bps; ";
            }

            if (_bitrateCommand == BitrateCommand.DoNotChange)
                userCommandBitrateTextBlock.Text = " - ";

            HLSTrace.WriteLine("Buffer Level: %{0}  or {1} KByte ", (int)(mediaElementAdapter.MediaStreamSource.BufferLevel.TotalMilliseconds * 100 / BufferLength.TotalMilliseconds), mediaElementAdapter.MediaStreamSource.BufferLevelInBytes / 1024);
            HLSTrace.WriteLine("Allocated memory {0} KByte.", GC.GetTotalMemory(true)/1024);
        }


        /// <summary>
        /// Updates bitrate display
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="newBandwidth"></param>
        private void Async_DownloadBitrateChanged(object sender, uint newBandwidth)
        { 
            Dispatcher.BeginInvoke(new Action<uint>(MSS_OnDownloadBitrateChanged), newBandwidth);
        }

        /// <summary>
        /// Updates playback bitrate display
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="newBandwidth"></param>
        private void Async_PlaybackBitrateChanged(object sender, uint newBandwidth)
        {
            Dispatcher.BeginInvoke(new Action<uint>(MSS_OnPlaybackBitrateChanged), newBandwidth);
        }

        /// <summary>
        /// Updates playback bitrate display
        /// </summary>
        /// <param name="newBandwidth"></param>
        private void MSS_OnPlaybackBitrateChanged(uint newBandwidth)
        {
            if (_sortedAvailableVariants != null)
            {
                double signalLevel = 0.00;
                foreach (HLSVariant variant in _sortedAvailableVariants)
                {
                    signalLevel += 1.00;
                    if (variant.Bitrate == newBandwidth)
                        break;
                }

                signalLevel = signalLevel / (double)_sortedAvailableVariants.Count;

                if (signalLevel >= 0.00 && signalLevel < .33)
                    MediaTransport.SignalStrengthMode = SignalStrengthMode.Low;
                else if (signalLevel >= 0.33 && signalLevel < .66)
                    MediaTransport.SignalStrengthMode = SignalStrengthMode.Medium;
                else if (signalLevel >= 0.66 && signalLevel < 1.00)
                    MediaTransport.SignalStrengthMode = SignalStrengthMode.High;
                else if (signalLevel == 1.00)
                    MediaTransport.SignalStrengthMode = SignalStrengthMode.Full;
                else
                    MediaTransport.SignalStrengthMode = SignalStrengthMode.None;
            }
            else
            {
                MediaTransport.SignalStrengthMode = SignalStrengthMode.None;
            }

            playbackBitrateTextBlock.Text = "Playback Bitrate = " + newBandwidth.ToString("#,##;(#,##)") + " bps";
        }

        /// <summary>
        /// Updates download bitrate display
        /// </summary>
        /// <param name="newBandwidth"></param>
        private void MSS_OnDownloadBitrateChanged(uint newBandwidth)
        {
            downloadBitrateTextBlock.Text = "Download Bitrate = " + newBandwidth.ToString("#,##;(#,##)") + " bps";
        }

        /// <summary>
        /// Updates segment URL display
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="uri"></param>
        private void Playback_MediaFileChanged_Async(object sender, Uri uri)
        {
            Dispatcher.BeginInvoke(new Action<object, Uri>(Playback_MediaFileChanged), sender, uri);
        }

        /// <summary>
        /// Updates segment URL display
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="uri"></param>
        private void Playback_MediaFileChanged(object sender, Uri uri)
        {
            urlTextBlock.Text = uri.ToString();
        }

        private void FullScreenPlayer_GamePadButtonDown(object sender, GamePadButtonEventArgs e)
        {
            if (e.Button == GamePadButton.DPadUp)
            {
                _bitrateCommand = BitrateCommand.IncreaseBitrate;
                userCommandBitrateTextBlock.Text = "HLS bitrate is set to be incremented";
            }
            else if (e.Button == GamePadButton.DPadDown)
            {
                _bitrateCommand = BitrateCommand.DecreaseBitrate;
                userCommandBitrateTextBlock.Text = "HLS bitrate is set to be decremented";
            }
            else if (e.Button == GamePadButton.DPadRight || e.Button == GamePadButton.DPadLeft)
            {
                if (_bitrateCommand == BitrateCommand.Auto)
                {
                    _bitrateCommand = BitrateCommand.DoNotChange;
                    userCommandBitrateTextBlock.Text = "HLS bitrate is set to 'Do Not Change'";
                }
                else
                {
                    _bitrateCommand = BitrateCommand.Auto;
                    userCommandBitrateTextBlock.Text = "HLS bitrate is set to Auto";
                }
            }
            else if (e.Button == GamePadButton.Y)
            {
                ExitPage();
            }
            else if (e.Button == GamePadButton.X)
            {
                if (_isCCEnabled)
                {
                    mediaElementAdapter._mediaElement.MarkerReached -= new TimelineMarkerRoutedEventHandler(mediaElementAdapter_MarkerReached);
                    _ccParser.CCUpdateEvent -= new CC608Parser.CCUpdateEventHandler(_ccParser_CCUpdateEvent);
                    
                    ClosedCaptionTextBlock.Inlines.Clear();
                    
                    _isCCEnabled = false;
                }
                else
                {
                    // The 608/708 CC data are passed back via marker events. Add an event handler to receive the CC data.  
                    mediaElementAdapter._mediaElement.MarkerReached += new TimelineMarkerRoutedEventHandler(mediaElementAdapter_MarkerReached);

                    // Add an event handler to CC parser, which is triggered when the closed caption text on the 
                    // screen needs to be updated. 
                    _ccParser.CCUpdateEvent += new CC608Parser.CCUpdateEventHandler(_ccParser_CCUpdateEvent);

                    _isCCEnabled = true;
                }
            
            }

        }

        private void mediaElementAdapter_BufferingProgressChanged(object sender, RoutedEventArgs e)
        {
            //mediaElementAdapter.BufferingProgress;
        }

        private void ExitPage()
        {
            if (NavigationService.CanGoBack)
            {
                mediaElementAdapter.MediaElement.MediaEnded -= mediaElementAdapter_MediaEnded;
                mediaElementAdapter.MediaElement.MediaFailed -= mediaElementAdapter_MediaFailed;
                mediaElementAdapter.MediaElement.BufferingProgressChanged -= mediaElementAdapter_BufferingProgressChanged;
                mediaElementAdapter.MediaStreamSource.Playback.DownloadBitrateChanged -= Async_DownloadBitrateChanged;

                mediaElementAdapter.MediaStreamSource.Playback.PlaybackBitrateChanged -= Async_PlaybackBitrateChanged;
                mediaElementAdapter.MediaStreamSource.Playback.MediaFileChanged -= Playback_MediaFileChanged_Async;

                // This is crucial to tear down the pipeline including source
                mediaElementAdapter.MediaElement.Source = null;
                NavigationService.GoBack();
            }
        }
        private void mediaElementAdapter_MediaEnded(object sender, RoutedEventArgs e)
        {
            ExitPage();
        }

        private void mediaElementAdapter_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            textBlockMessage.Visibility = Visibility.Visible;
            textBlockMessage.Text = "Error: " +  e.ErrorException.Message + "\n" + e.ErrorException.StackTrace;
        }
    }
}

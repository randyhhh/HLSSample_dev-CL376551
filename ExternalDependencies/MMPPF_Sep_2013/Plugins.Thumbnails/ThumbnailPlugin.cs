using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.SilverlightMediaFramework.Core;
using Microsoft.SilverlightMediaFramework.Plugins.Metadata;
using Microsoft.SilverlightMediaFramework.Plugins.Primitives;
using Microsoft.SilverlightMediaFramework.Utilities.Extensions;

namespace Microsoft.SilverlightMediaFramework.Plugins.Thumbnails
{
    [PartCreationPolicy(CreationPolicy.NonShared)]
    [ExportGenericPlugin(PluginName = PluginName, PluginDescription = PluginDescription, PluginVersion = PluginVersion)]
    public class ThumbnailPlugin : IGenericPlugin
    {
        const string PluginName = "ThumbnailPlugin";
        const string PluginDescription = "Supports the ability to predict, pre-fetch and cache thumbnails used for scrubbing, RW, & FF.";
        const string PluginVersion = "2.2012.1005.0";

        public const string MetaDataItemUrlPattern = "Microsoft.SilverlightMediaFramework.Thumbnails.UrlPattern";
        public const string MetaDataItemMaxCacheSize = "Microsoft.SilverlightMediaFramework.Thumbnails.MaxCacheSize";
        public const string MetaDataItemPermanentCacheSize = "Microsoft.SilverlightMediaFramework.Thumbnails.PermanentCacheSize";
        public const string MetaDataItemMaxSimultaneousRequests = "Microsoft.SilverlightMediaFramework.Thumbnails.MaxSimultaneousRequests";
        public const string MetaDataItemPredictionInterval = "Microsoft.SilverlightMediaFramework.Thumbnails.PredictionInterval";
        public const string MetaDataItemThumbnailRequestDelay = "Microsoft.SilverlightMediaFramework.Thumbnails.ThumbnailRequestDelay";
        public const string MetaDataItemKeyframeIntervalSeconds = "Microsoft.SilverlightMediaFramework.Thumbnails.KeyframeIntervalSeconds";
        public const string MetaDataItemIsUrlPatternSequential = "Microsoft.SilverlightMediaFramework.Thumbnails.IsUrlPatternSequential";

        SMFPlayer smf;
        ThumbnailManager thumbManager;
        string ThumbnailImageUrlPattern;

        bool isEnabled;
        /// <summary>
        /// Indicates that the thumbnail manager is enabled.
        /// </summary>
        public bool IsEnabled
        {
            get { return isEnabled; }
            set
            {
                isEnabled = value;
                smf.ThumbnailStrategy = IsEnabled ? ThumbnailStrategyEnum.ThumbnailRequest : ThumbnailStrategyEnum.None;
            }
        }

        /// <summary>
        /// Gets and sets whether or not the urls are based on timestamp or index (sequential). Default is false.
        /// </summary>
        public bool IsUrlPatternSequential { get; set; }

        public bool IsLoaded { get; private set; }

        public event Action<IPlugin, Microsoft.SilverlightMediaFramework.Plugins.Primitives.LogEntry> LogReady;

        public event Action<IPlugin> PluginLoaded;

        public event Action<IPlugin> PluginUnloaded;

        public event Action<IPlugin, Exception> PluginLoadFailed;

        public event Action<IPlugin, Exception> PluginUnloadFailed;

        public void Load()
        {
            try
            {
                thumbManager = new ThumbnailManager();
                smf.MediaOpened += smf_MediaOpened;
                smf.PlaylistItemChanged += smf_PlaylistItemChanged;
                smf.PlayRateChanged += smf_PlayRateChanged;
                smf.BufferingProgressChanged += smf_BufferingProgressChanged;
                smf.MediaTransport.ThumbnailRequest += MediaTransport_ThumbnailRequest;

                ReportIfError(() => smf.GlobalConfigMetadata.FirstOrDefault(c => c.Key == MetaDataItemMaxCacheSize).IfNotNull(m => thumbManager.MaxCacheSize = int.Parse(m.Value.ToString())));
                ReportIfError(() => smf.GlobalConfigMetadata.FirstOrDefault(c => c.Key == MetaDataItemMaxSimultaneousRequests).IfNotNull(m => thumbManager.MaxSimultaneousRequests = int.Parse(m.Value.ToString())));
                ReportIfError(() => smf.GlobalConfigMetadata.FirstOrDefault(c => c.Key == MetaDataItemPredictionInterval).IfNotNull(m => thumbManager.PredictionInterval = TimeSpan.Parse(m.Value.ToString())));
                ReportIfError(() => smf.GlobalConfigMetadata.FirstOrDefault(c => c.Key == MetaDataItemThumbnailRequestDelay).IfNotNull(m => thumbManager.ThumbnailRequestDelay = TimeSpan.Parse(m.Value.ToString())));
                ReportIfError(() => smf.GlobalConfigMetadata.FirstOrDefault(c => c.Key == MetaDataItemPermanentCacheSize).IfNotNull(m => thumbManager.PermanentCacheSize = int.Parse(m.Value.ToString())));
                thumbManager.LoadThumbnailAsync += thumbManager_LoadThumbnailAsync;
                thumbManager.ShowThumbnail += thumbManager_ShowImage;

                IsLoaded = true;
                PluginLoaded.IfNotNull(p => p(this));
            }
            catch (Exception ex)
            {
                PluginLoadFailed.IfNotNull(p => p(this, ex));
            }
        }

        public void Unload()
        {
            try
            {
                smf.MediaOpened -= smf_MediaOpened;
                smf.PlaylistItemChanged -= smf_PlaylistItemChanged;
                smf.PlayRateChanged -= smf_PlayRateChanged;
                smf.BufferingProgressChanged -= smf_BufferingProgressChanged;
                smf.MediaTransport.ThumbnailRequest -= MediaTransport_ThumbnailRequest;
                smf = null;

                thumbManager.LoadThumbnailAsync -= thumbManager_LoadThumbnailAsync;
                thumbManager.ShowThumbnail -= thumbManager_ShowImage;
                thumbManager = null;

                IsLoaded = false;
                PluginUnloaded.IfNotNull(p => p(this));
            }
            catch (Exception ex)
            {
                PluginUnloadFailed.IfNotNull(p => p(this, ex));
            }
        }

        public void SetPlayer(FrameworkElement Player)
        {
            smf = Player as SMFPlayer;
        }

        void ReportIfError(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                var log = new LogEntry();
                log.Severity = LogLevel.Warning;
                log.Type = "ThumbnailPlugin";
                log.Message = ex.Message;
                LogReady.IfNotNull(l => l(this, log));
            }
        }

        void smf_MediaOpened(object sender, EventArgs e)
        {
            if (IsEnabled)
            {
                thumbManager.MaxPosition = smf.EndPosition;
                thumbManager.MinPosition = smf.StartPosition;
                thumbManager.QueuePermanentThumbnails();
            }
        }

        void MediaTransport_ThumbnailRequest(object sender, Xbox.Controls.ThumbnailRequestEventArgs e)
        {
            thumbManager.ThumbnailRequest(e.Position, (int)e.PlayRate);
        }

        void smf_BufferingProgressChanged(object sender, CustomEventArgs<double> e)
        {
            if (e.Value >= 1.0)
            {
                smf.BufferingProgressChanged -= smf_BufferingProgressChanged;
                thumbManager.LoadThumbnails();
            }
        }

        void smf_PlayRateChanged(object sender, PlayRateChangedEventArgs e)
        {
            thumbManager.PlayRateChanged((int)e.PlayRate);
        }

        void smf_PlaylistItemChanged(object sender, CustomEventArgs<Core.Media.PlaylistItem> e)
        {
            thumbManager.Clear();
            if (e.Value != null)
            {
                // default properties
                thumbManager.KeyframeIntervalSeconds = smf.ThumbnailIntervalSeconds;
                IsUrlPatternSequential = false;

                ReportIfError(() => e.Value.CustomMetadata.FirstOrDefault(c => c.Key == MetaDataItemKeyframeIntervalSeconds).IfNotNull(m => thumbManager.KeyframeIntervalSeconds = int.Parse(m.Value.ToString())));
                ReportIfError(() => e.Value.CustomMetadata.FirstOrDefault(c => c.Key == MetaDataItemIsUrlPatternSequential).IfNotNull(m => IsUrlPatternSequential = Convert.ToBoolean(int.Parse(m.Value.ToString()))));

                var metadata = e.Value.CustomMetadata.FirstOrDefault(m => m.Key == MetaDataItemUrlPattern);
                if (metadata != null)
                {
                    ThumbnailImageUrlPattern = metadata.Value.ToString();
                    IsEnabled = true;
                }
                else
                {
                    IsEnabled = false;
                }
            }
        }

        void thumbManager_ShowImage(object sender, BitmapImage bmp)
        {
            smf.SetThumbnail(bmp);
        }

        void thumbManager_LoadThumbnailAsync(int timestamp, object state)
        {
            WebClient wc = new WebClient();
            wc.OpenReadCompleted += wc_OpenReadCompleted;

            string url;
            if (!IsUrlPatternSequential)
            {
                url = string.Format(ThumbnailImageUrlPattern, timestamp);
            }
            else
            {
                url = string.Format(ThumbnailImageUrlPattern, timestamp / thumbManager.KeyframeIntervalSeconds);
            }

            wc.OpenReadAsync(new Uri(url), state);
        }

        void wc_OpenReadCompleted(object sender, OpenReadCompletedEventArgs e)
        {
            WebClient wc = sender as WebClient;
            wc.OpenReadCompleted -= wc_OpenReadCompleted;

            BitmapImage bmp = null;
            try
            {
                if (e.Error == null)
                {
                    bmp = new BitmapImage();
                    using (var stream = e.Result)
                    {
                        bmp.SetSource(stream);
                    }
                }
            }
            finally
            {
                if (thumbManager != null)
                {
                    thumbManager.LoadThumbnailCompleted(bmp, e.UserState);
                }
            }
        }
    }
}

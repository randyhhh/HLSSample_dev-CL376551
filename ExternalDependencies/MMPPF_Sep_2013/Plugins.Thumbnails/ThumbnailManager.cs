using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;
using System.ComponentModel;
using System.Windows.Threading;

namespace Microsoft.SilverlightMediaFramework.Plugins.Thumbnails
{
    /// <summary>
    /// Provides the ability to predict, cache, and retrieve thumbnails depending on the playrate and position of a video.
    /// </summary>
    public class ThumbnailManager : INotifyPropertyChanged
    {
        const bool LoadOnPlay = false;

        /// <summary>
        /// The maximum position on the timeline. Thumbnails will not be retrieved past this point
        /// </summary>
        public TimeSpan MaxPosition { get; set; }

        /// <summary>
        /// The minimum position on the timeline. Thumbnails will not be retrieved past this point 
        /// </summary>
        public TimeSpan MinPosition { get; set; }

        /// <summary>
        /// Callback to allow the application to provide the thumbnail for the given position when using a predictive pattern. Developer MUST call LoadThumbnailCompleted method when finished even if an exception occurred.
        /// </summary>
        public event Action<int, object> LoadThumbnailAsync;

        /// <summary>
        /// Tells the application which thumbnail to show.
        /// </summary>
        public event Action<object, BitmapImage> ShowThumbnail;

        List<CachedThumbnail> LoadingThumbnails = new List<CachedThumbnail>();
        Dictionary<int, CachedThumbnail> ThumbnailCache = new Dictionary<int, CachedThumbnail>();
        Queue<CachedThumbnail> PermanentCache;

        int keyframeIntervalSeconds = 5;
        /// <summary>
        /// Gets and sets the interval by which to update the thumbnail
        /// </summary>
        public int KeyframeIntervalSeconds
        {
            get { return keyframeIntervalSeconds; }
            set
            {
                keyframeIntervalSeconds = value;
                if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("KeyframeIntervalSeconds"));
            }
        }

        int maxCacheSize = 30;
        /// <summary>
        /// Gets and sets the maximum number of thumbnails that should be stored in the cache
        /// </summary>
        public int MaxCacheSize
        {
            get { return maxCacheSize; }
            set
            {
                maxCacheSize = value;
                while (ThumbnailCache.Count > value)
                {
                    var leastValuableThumbnail = ThumbnailCache.OrderBy(t => t.Value.Score).First();
                    OnThumbnailRemoved(leastValuableThumbnail.Value);
                    ThumbnailCache.Remove(leastValuableThumbnail.Key);
                }
                if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("MaxCacheSize"));
            }
        }

        int maxSimultaneousRequests = 2;
        /// <summary>
        /// Gets and sets the maximum number of simultaneous requests
        /// </summary>
        public int MaxSimultaneousRequests
        {
            get { return maxSimultaneousRequests; }
            set
            {
                maxSimultaneousRequests = value;
                if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("MaxSimultaneousRequests"));
            }
        }

        int permanentCacheSize = 10;
        /// <summary>
        /// Gets and sets the size of the cache reserved for permanently cached items.
        /// </summary>
        public int PermanentCacheSize
        {
            get { return permanentCacheSize; }
            set
            {
                if (permanentCacheSize != value)
                {
                    permanentCacheSize = value;
                    if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("PermanentCacheSize"));
                }
            }
        }

        TimeSpan thumbnailRequestDelay = TimeSpan.Zero;
        /// <summary>
        /// Gets and sets the delay that occurs when one thumbnail request completes and the next in the queue is started.
        /// </summary>
        public TimeSpan ThumbnailRequestDelay
        {
            get { return thumbnailRequestDelay; }
            set
            {
                thumbnailRequestDelay = value;
                if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("ThumbnailRequestDelay"));
            }
        }

        TimeSpan predictionInterval = TimeSpan.FromMilliseconds(250);
        /// <summary>
        /// Gets and sets the amount of time in between when the ThumbnailRequest event will be honored. This prevents it from triggering the prediction logic too often
        /// </summary>
        public TimeSpan PredictionInterval
        {
            get { return predictionInterval; }
            set
            {
                predictionInterval = value;
                if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("PredictionInterval"));
            }
        }

        /// <summary>
        /// Causes all permanently cached thumbnails to be loaded.
        /// </summary>
        public void QueuePermanentThumbnails()
        {
            var duration = MaxPosition.Subtract(MinPosition);
            var interval = duration.TotalSeconds / permanentCacheSize;

            var permanentCacheIntervals = Enumerable.Range(0, PermanentCacheSize)
                    .Select(i => GetRoundedPosition(TimeSpan.FromSeconds(i * interval)))
                    .Distinct();

            // update the permanent cache size, this could be less than what was originally requested due to non-distinct intervals.
            PermanentCacheSize = permanentCacheIntervals.Count();

            PermanentCache = new Queue<CachedThumbnail>(
                permanentCacheIntervals.Select(i =>
                    new CachedThumbnail()
                    {
                        TimeStamp = i,
                        Permanent = true
                    }
                    )
                );
        }

        DateTime lastPrediction = DateTime.MinValue;
        double ThumbnailRate;
        int seekTime;
        int? displayedTime;
        /// <summary>
        /// Called when a thumbnail is requested
        /// </summary>
        /// <param name="Position">The Position of the thumbnail that was requested</param>
        /// <param name="PlayRate">The rate of play. Used to help predict future thumbnail requests</param>
        public void ThumbnailRequest(TimeSpan Position, int PlayRate)
        {
            if (DateTime.Now.Subtract(lastPrediction) > PredictionInterval)
            {
                ThumbnailRate = GetPredictionUnitRate(Position, PlayRate);
                if (ThumbnailRate != 0)
                {
                    var predictions = GetSortedPredictions(Position);
                    BufferPredictions(predictions);
                }
                else
                {
                    ThumbnailPredictions = new List<int>();
                }
                lastPrediction = DateTime.Now;
            }

            seekTime = GetRoundedPosition(Position);
            if (!displayedTime.HasValue || displayedTime.Value != seekTime)
            {
                // could optimize by pre-sorting cache
                // set the thumbnail image to the closest one
                if (ThumbnailCache.Any())
                {
                    var thumb = ThumbnailCache.OrderBy(t => Math.Abs(t.Key - GetRoundedPosition(Position))).First();
                    if (!displayedTime.HasValue || displayedTime.Value != thumb.Key)
                    {
                        if (ShowThumbnail != null) ShowThumbnail(this, thumb.Value.Bitmap);
                        displayedTime = thumb.Key;
                    }
                }
            }
        }

        private void CancelThumbnailRequests()
        {
            thumbnailPredictions.Clear();
        }

        /// <summary>
        /// Call when PlayRate changes.
        /// </summary>
        /// <param name="PlayRate">The new PlayRate</param>
        public void PlayRateChanged(int PlayRate)
        {
            if (PlayRate == PlayRate_NotPlaying)
            {
                LastScrubPosition = null;
            }
            else if (PlayRate == PlayRate_Playing) 
            {
                if (!LoadOnPlay)
                {
                    CancelThumbnailRequests();
                }
            }
        }

        List<int> thumbnailPredictions = new List<int>();
        /// <summary>
        /// A list of current thumbnail predictions
        /// </summary>
        public List<int> ThumbnailPredictions
        {
            get { return thumbnailPredictions; }
            set
            {
                thumbnailPredictions = value;
                OnThumbnailPredictionsChanged();
            }
        }

        const int PlayRate_Playing = 1;
        const int PlayRate_NotPlaying = 0;
        TimeSpan? LastScrubPosition;
        double GetPredictionUnitRate(TimeSpan Position, int Playrate)
        {
            double rate;
            if (Playrate == PlayRate_NotPlaying)   // normal scrubbing
            {
                if (LastScrubPosition.HasValue)
                {
                    TimeSpan delta = Position.Subtract(LastScrubPosition.Value);
                    rate = delta.TotalMilliseconds / PredictionInterval.TotalMilliseconds;
                }
                else
                {
                    rate = 1;   // not enough data to know which way they're going or how fast, just start loading at a rate of 1.
                }
                LastScrubPosition = Position;
            }
            else // FF or RW
            {
                rate = (double)Playrate;
            }
            return rate;
        }

        IEnumerable<TimeSpan> GetSortedPredictions(TimeSpan Position)
        {
            TimeSpan stamp = Position;
            //TimeSpan needed = TimeSpan.Zero;
            while (stamp >= MinPosition && stamp <= MaxPosition)
            {
                yield return stamp;

                // predict the position of the subsequent ThumbnailRequest call
                stamp = TimeSpan.FromMilliseconds(stamp.TotalMilliseconds + ThumbnailRate * PredictionInterval.TotalMilliseconds);
            }
        }

        /// <summary>
        /// Starts loading thumbnail images that we predict we'll need in order of importance.
        /// This is expected to start adding items to the ThumbnailCache.
        /// </summary>
        /// <param name="Predictions">A collection of prediction objects</param>
        void BufferPredictions(IEnumerable<TimeSpan> Predictions)
        {
            if (Predictions.Any())
            {
                ThumbnailPredictions = Predictions.Select(GetRoundedPosition).Distinct().Take(MaxCacheSize).ToList();
            }
            else
            {
                ThumbnailPredictions = new List<int>();
            }

            // start fetching thumbnails
            LoadThumbnails();
        }

        public void LoadThumbnails()
        {
            if (LoadThumbnailAsync != null)
            {
                while (LoadingThumbnails.Count < MaxSimultaneousRequests && (ThumbnailPredictions.Any() || (PermanentCache != null && PermanentCache.Any())))
                {
                    if (PermanentCache != null && PermanentCache.Any())
                    {
                        // we're still loading the permanent cache
                        if (LoadingThumbnails.Count + ThumbnailCache.Count < MaxCacheSize)
                        {
                            var thumbnailToLoad = PermanentCache.Dequeue();
                            LoadThumbnail(thumbnailToLoad);
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        // re-order the queue based on what is cached already
                        // this also sets a value on all thumbnails in the cache
                        var thumbnailsToLoad = CreateThumbnailQueue();

                        if (thumbnailsToLoad.Any())
                        {
                            var thumbnailToLoad = thumbnailsToLoad.Dequeue();

                            // predict what the cache will look like once the currently loading thumbnails have loaded.
                            var futureCache = EffectiveThumbnailCache.ToList();
                            // check to see if this new thumbnail is more important than anything currently in the cache (if the cache is full)
                            // when comparing scores, don't count the first x thumbnails since those will be replaced by the currently loading thumbnails.
                            if (LoadingThumbnails.Count + ThumbnailCache.Count < MaxCacheSize || thumbnailToLoad.Score > futureCache.OrderBy(t => t.Score).Skip(LoadingThumbnails.Count).First().Score)
                            {
                                LoadThumbnail(thumbnailToLoad);
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
        }

        void LoadThumbnail(CachedThumbnail thumbnailToLoad)
        {
            LoadingThumbnails.Add(thumbnailToLoad);
            OnThumbnailRequested(thumbnailToLoad);
            LoadThumbnailAsync(thumbnailToLoad.TimeStamp, thumbnailToLoad);
        }

        /// <summary>
        /// Returns the ThumbnailCache, plus whatever is currently loading
        /// </summary>
        IEnumerable<CachedThumbnail> EffectiveThumbnailCache
        {
            get
            {
                return ThumbnailCache.Values.Concat(LoadingThumbnails);
            }
        }

        Queue<CachedThumbnail> CreateThumbnailQueue()
        {
            Queue<CachedThumbnail> result = new Queue<CachedThumbnail>();
            Queue<CachedThumbnail> endOfQueue = new Queue<CachedThumbnail>();

            // reset the value of each item in the cache
            foreach (var thumb in ThumbnailCache)
            {
                thumb.Value.Score = 0;
            }

            int score = 100 + ThumbnailPredictions.Count();
            // re-order the queue based on what is cached already
            foreach (var thumb in ThumbnailPredictions)
            {
                score--;
                if (!LoadingThumbnails.Any(t => t.TimeStamp == thumb))  // make sure we're not loading this thumbnail already
                {
                    double closestCachedThumbOffset = 0;    // the distance in rate units of the closest cached thumbnail.
                    CachedThumbnail closestThumbnail = null;
                    if (ThumbnailCache.Any())
                    {
                        closestThumbnail = ThumbnailCache.OrderBy(t => Math.Abs(t.Key - thumb)).First().Value;
                        var closestCachedThumbDelta = Math.Abs(closestThumbnail.TimeStamp - thumb);
                        closestCachedThumbOffset = closestCachedThumbDelta / ThumbnailRate;
                    }

                    if (closestThumbnail == null || closestCachedThumbOffset >= 1)
                    {
                        // no thumbnail was found or the closest thumbnail was too far away to be useful
                        result.Enqueue(new CachedThumbnail() { TimeStamp = thumb, Score = score });
                    }
                    else if (closestCachedThumbOffset == 0)
                    {
                        // we need this one! value it according to how soon we need it.
                        closestThumbnail.Score = score;
                    }
                    else if (closestCachedThumbOffset < 1)
                    {
                        // we have something in the cache that is close enough, bump the priority out
                        endOfQueue.Enqueue(new CachedThumbnail() { TimeStamp = thumb, Score = score });
                        // update the value of the cached thumbnail
                        closestThumbnail.Score++;
                    }
                }
            }

            while (endOfQueue.Any())
            {
                result.Enqueue(endOfQueue.Dequeue());
            }

            return result;
        }

        /// <summary>
        /// Called in response to LoadThumbnailAsync to provide the thumbnail for the given position. Developer MUST call LoadThumbnailCompleted method when finished even if an exception occurred.
        /// </summary>
        /// <param name="bmp">The BitmapImage created. Set to null if no image was found.</param>
        /// <param name="state">The UserState passed from LoadThumbnailAsync. Do not alter this parameter</param>
        public void LoadThumbnailCompleted(BitmapImage bmp, object state)
        {
            var thumbnail = (CachedThumbnail)state;

            if (LoadingThumbnails.Contains(thumbnail))  // this is used to indicate that the operation was canceled.
            {
                if (bmp != null)
                {
                    thumbnail.Bitmap = bmp;
                    OnThumbnailAdded(thumbnail);
                    ThumbnailCache.Add(thumbnail.TimeStamp, thumbnail);
                    if (ThumbnailCache.Count > MaxCacheSize)
                    {
                        var leastValuableThumbnail = ThumbnailCache.OrderBy(t => t.Value.Score).First();
                        OnThumbnailRemoved(leastValuableThumbnail.Value);
                        ThumbnailCache.Remove(leastValuableThumbnail.Key);
                    }
                }

                LoadingThumbnails.Remove(thumbnail);
                if (thumbnailRequestDelay > TimeSpan.Zero)
                {
                    var timer = new DispatcherTimer();
                    timer.Interval = thumbnailRequestDelay;
                    timer.Tick += timer_Tick;
                    timer.Start();
                }
                else
                {
                    LoadThumbnails();
                }
            }
        }

        void timer_Tick(object sender, EventArgs e)
        {
            var timer = sender as DispatcherTimer;
            timer.Stop();
            timer.Tick -= timer_Tick;
            LoadThumbnails();
        }

        /// <summary>
        /// Provides information about which thumbnail was just requested to the application
        /// </summary>
        public event Action<object, CachedThumbnail> ThumbnailRequested;
        void OnThumbnailRequested(CachedThumbnail t)
        {
            if (ThumbnailRequested != null) ThumbnailRequested(this, t);
        }

        /// <summary>
        /// Provides information about which thumbnail was added to the cache
        /// </summary>
        public event Action<object, CachedThumbnail> ThumbnailAdded;
        void OnThumbnailAdded(CachedThumbnail t)
        {
            if (ThumbnailAdded != null) ThumbnailAdded(this, t);
        }

        /// <summary>
        /// Provides information about which thumbnail was removed
        /// </summary>
        public event Action<object, CachedThumbnail> ThumbnailRemoved;
        void OnThumbnailRemoved(CachedThumbnail t)
        {
            if (ThumbnailRemoved != null) ThumbnailRemoved(this, t);
        }

        /// <summary>
        /// Informs the application that the thumbnail prediction collection changed.
        /// </summary>
        public event Action<object, List<int>> ThumbnailPredictionsChanged;
        void OnThumbnailPredictionsChanged()
        {
            if (ThumbnailPredictionsChanged != null) ThumbnailPredictionsChanged(this, thumbnailPredictions);
        }

        int GetRoundedPosition(TimeSpan Position)
        {
            var s = (int)Position.TotalSeconds;
            return KeyframeIntervalSeconds * (s / KeyframeIntervalSeconds);
        }

        /// <summary>
        /// Informs the application that a given property has changed.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        public void Clear()
        {
            LoadingThumbnails.Clear();
            ThumbnailCache.Clear();
            if (PermanentCache != null) PermanentCache.Clear();
        }
    }

    /// <summary>
    /// Used to store cached thumbnails
    /// </summary>
    public class CachedThumbnail
    {
        internal CachedThumbnail() { }

        /// <summary>
        /// The BitmapImage of the thumbnail
        /// </summary>
        public BitmapImage Bitmap { get; set; }

        /// <summary>
        /// The timestamp of the thumbnail in seconds
        /// </summary>
        public int TimeStamp { get; set; }

        /// <summary>
        /// Indicates whether or not the thumbnail is permanent. If true, it will not be removed from the cache.
        /// </summary>
        public bool Permanent { get; set; }

        int score;
        /// <summary>
        /// The score of the thumbnail based on the latest predictions. Score will be the maximum possible value if permanent.
        /// </summary>
        public int Score
        {
            get
            {
                return Permanent ? int.MaxValue : score;
            }
            internal set { score = value; }
        }

    }
}

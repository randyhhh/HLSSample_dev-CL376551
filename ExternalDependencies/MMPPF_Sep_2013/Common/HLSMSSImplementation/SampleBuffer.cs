using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Threading;


namespace Silverlight.Samples.HttpLiveStreaming
{
    public class ConvertHelper
    {
        public const long MSInHNS = 10000;
        public const long SecondInMS = 1000;
        public const long SecondInHNS = MSInHNS * SecondInMS;
        public const long MinuteInSecond = 60;
        public const long MinutInHNS = MinuteInSecond * SecondInHNS;
        public const long HourInMinute = 60;
        public const long HourInHNS = HourInMinute * MinutInHNS;
    }

    /// <summary>
    /// Encapsulates a single video or audio sample.
    /// </summary>
    public class Sample
    {
        private FIFOMemoryPool.PoolAllocItem _dataItem;

        public FIFOMemoryPool.PoolAllocItem PoolItem
        {
            get
            {
                return _dataItem;
            }
        }

        /// <summary>
        /// Sample timestamp in 100ns intervals
        /// </summary>
        private long _timestamp;

        private long _adjustedTimeStamp;

        /// <summary>
        /// Sample duration in 100ns intervals
        /// </summary>
        private long _duration;

        /// <summary>
        /// Bitrate of the stream that this sample originated from 
        /// </summary>
        private uint _bitrate;

        private HLSStream _hlsStream = null;

        public int TimelineIndex
        {
            get
            {
                if (_hlsStream != null)
                {
                    return _hlsStream.TimelineIndex;
                }
                else
                {
                    return 0;
                }
            }
        }

        public SegmentProgramDateTime SegmentTime
        {
            get
            {

                if (_hlsStream != null)
                {
                    return _hlsStream.SegmentProgramTime;
                }
                else
                {
                    return null;
                }
            }
        }

        private MediaStreamDescription _msd;

        public MediaStreamDescription MSD
        {
            get
            {
                return _msd;
            }
        }

        private SampleBuffer _sampleBuffer;

        private static Dictionary<MediaSampleAttributeKeys, string> _keyFrameAttr;

        private static Dictionary<MediaSampleAttributeKeys, string> _nonKeyFrameAttr;

        private static Dictionary<MediaSampleAttributeKeys, string> _emptyAttributes = new Dictionary<MediaSampleAttributeKeys,string>();

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="timestamp"></param>
        /// <param name="timestamp">Sample timestamp in HNS units</param>
        /// <param name="duration">Sample duartion in HNS units</param>
        /// <param name="bitrate">Bitrate of the stream that this sample was created from</param>
        public Sample(long timestamp, long duration, uint bitrate, HLSStream hlsStream, int dataSize, MediaStreamDescription msd, SampleBuffer buffer)
        {
            if (null == _keyFrameAttr)
            {
                _keyFrameAttr = new Dictionary<MediaSampleAttributeKeys,string>();
                _keyFrameAttr[MediaSampleAttributeKeys.KeyFrameFlag] = Boolean.TrueString;
            }

            if (null == _nonKeyFrameAttr)
            {
                _nonKeyFrameAttr = new Dictionary<MediaSampleAttributeKeys,string>();
                _nonKeyFrameAttr[MediaSampleAttributeKeys.KeyFrameFlag] = Boolean.FalseString;
            }

            Reset(timestamp, duration, bitrate, hlsStream, dataSize, msd, buffer);
        }

        public void Reset(long timestamp, long duration, uint bitrate, HLSStream hlsStream, int dataSize, MediaStreamDescription msd, SampleBuffer buffer)
        {
            _timestamp = timestamp;
            _duration = duration;
            _bitrate = bitrate;
            _hlsStream = hlsStream;
            _msd = msd;
            _isKeyFrame = false;
            _adjustedTimeStamp = 0;
            if (dataSize != -1 && null != buffer)
            {
                _dataItem = buffer.FIFOMemoryPool.Alloc(dataSize);
            }
            else
            {
                _dataItem = null;
            }
            _sampleBuffer = buffer;
        }

        public MediaStreamSample ToMediaStreamSample()
        {
            if (null != _dataItem)
            {
                if (_isKeyFrame)
                {
                    return new MediaStreamSample(_msd, DataStream, 0, DataStream.Length, AdjustedTimeStamp, _keyFrameAttr);
                }
                else
                {
                    return new MediaStreamSample(_msd, DataStream, 0, DataStream.Length, AdjustedTimeStamp, _nonKeyFrameAttr);
                }
            }
            else
            {
                return new MediaStreamSample(_msd, null, 0, 0, 0, _emptyAttributes);
            }
        }

        /// <summary>
        /// Returns HLSStream the sample belongs
        /// </summary>
        public HLSStream HLSStream
        {
            get
            {
                return _hlsStream;
            }
        }
        /// <summary>
        /// Returns sample data
        /// </summary>
        public Stream DataStream
        {
            get
            {
                return _dataItem;
            }
        }

        public bool IsPooled
        {
            get
            {
                return (null != _dataItem);
            }
        }

        public void Discard()
        {
            _sampleBuffer.RecycleSample(this);
        }

        /// <summary>
        /// Returns sample timestamp in 100ns intervals
        /// </summary>
        public long Timestamp
        {
            get
            {   
                return _timestamp;
            }
        }

        public long AdjustedTimeStamp
        {
            get
            {
                return _adjustedTimeStamp;
            }
            set
            {
                _adjustedTimeStamp = value;
            }
        }

        /// <summary>
        /// Returns the bitrate of the stream that this sample originated from 
        /// </summary>
        public uint Bitrate
        {
            get
            {
                return _bitrate;
            }
        }

        /// <summary>
        /// Returns the duration of the sample 
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                return new TimeSpan(_duration);
            }
        }

        /// <summary>
        /// Key frame flag
        /// </summary>
        private bool _isKeyFrame = false;


        /// <summary>
        /// Sets/Gets flag indicating if the current sample is a key frame 
        /// </summary>
        public bool KeyFrame
        {
            get
            {
                return _isKeyFrame;
            }
            set
            {
                _isKeyFrame = value;
            }
        }

        /// <summary>
        /// Appends to sample data.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public void AddData(byte[] data, int offset, int count)
        {
            if (null == _dataItem)
            {
                _dataItem = _sampleBuffer.FIFOMemoryPool.Alloc(count);
            }
            _dataItem.Write(data, offset, count);
        }
    }

    public class TimelineEventInfo
    {
        public class TimelineEvent
        {
            public int upcomingTimelineIndex;
            public long currentTimelineEndTimeStampHNS;
            public long upcomingTimelineStartTimeStampHNS;
        }

        public int timelineIndex;
        public TimelineEvent timelineEvent;
    }

    /// <summary>
    /// Implements storage for samples ready to be consumed by MediaElement.
    /// </summary>
    public class SampleBuffer
    {
        private FIFOMemoryPool _pool;

        public FIFOMemoryPool FIFOMemoryPool
        {
            get
            {
                return _pool;
            }
        }

        private double _shrinkThresholdPercentage = 0.3;

        /// <summary>
        /// Samples currently in buffer
        /// </summary>
        private Queue<Sample> _samples;

        private Queue<Sample> _sampleWrapperPool;

        /// <summary>
        /// Maintains pointer to last sample added to queue for computing diagnostic data
        /// </summary>
        private Sample _lastSample = null;

        /// <summary>
        /// returns the last sample in the buffer
        /// </summary>
        public Sample GetLastSample()
        {
            return _lastSample;
        }

        /// <summary>
        /// Maintains total size of all samples in buffer
        /// </summary>
        private volatile int _totalSizeOfSamples;

        /// <summary>
        /// Signals end of playback.
        /// </summary>
        private bool _endOfPlayback;

        /// <summary>
        /// hold how many pending sample request has not been fulfilled. 
        /// </summary>
        private int _pendingSampleRequests = 0;

        /// <summary>
        /// called when pipeline asked for a sample
        /// </summary>
        public int OnSampleRequested()
        {
            return System.Threading.Interlocked.Increment(ref _pendingSampleRequests);
        }


        /// <summary>
        /// variable to track the start timestamp of a ts file
        /// </summary>
        private long _hnsSegmentStart;
        public long GetSegmentStart()
        {
            lock (this)
            {
                return _hnsSegmentStart;
            }
        }
        /// <summary>
        /// Reset firstSampleInSegment marker. 
        /// </summary>
        public void ResetOnSegmentStart()
        {
            lock (this)
            {
                _hnsSegmentStart = -1;
            }
        }
        /// <summary>
        /// Media stream description.
        /// </summary>
        private MediaStreamDescription _msd = null;

        public class TimeLineInfo
        {
            public long _timelineStartOffsetHNS = -1;
            public long _timelineStartTimeStamp = -1;
            public bool _isMonoIncrease = false;
            
            public TimeLineInfo(long timelineStartOffsetHNS, bool isMonoIncrease)
            {
                _timelineStartOffsetHNS = timelineStartOffsetHNS;
                _isMonoIncrease = isMonoIncrease;
            }

            public TimeLineInfo(TimeLineInfo timelineInfo)
            {
                _timelineStartOffsetHNS = timelineInfo._timelineStartOffsetHNS;
                _timelineStartTimeStamp = timelineInfo._timelineStartTimeStamp;
                _isMonoIncrease = timelineInfo._isMonoIncrease;
                _90ktsoffset = timelineInfo._90ktsoffset;
                _last90kts = timelineInfo._last90kts;

            }

            /// <summary>
            // received a ts sample from parser
            /// </summary>
            /// <param name="tsSampleTime"> transport stream sample time in 90khz</param>
            /// <return> Presentation time in 100HZ</return>
            public long OnTsSample(MediaStreamType streamType, ulong ts90khzSampleTime)
            {
                lock (this)
                {

                    // Test block: manually force timestamp rollover. 
                    //static long s_offset = -1;
                    //if (s_offset == -1)
                    //{
                    //s_offset = ((long)1 << 32) - (long)ts90khzSampleTime - 900000;
                    //}
                    //long tsTime = ((long)ts90khzSampleTime + s_offset) % ((long)1 << 32);

                    long tsTime = (long)ts90khzSampleTime;
                    if (_timelineStartOffsetHNS != -1)
                    {
                        if (streamType == MediaStreamType.Audio)
                        {
                            // only check audio sample rollver, video just simply follow audio
                            if (tsTime - _last90kts < _TsRolloverMarginIn90HZ)
                            {
                                _90ktsoffset += _last90kts - tsTime;
                                HLSTrace.WriteLineHigh(" Audio TS timestamp rollovered. previouis:{0}, new:{1}  adjusted:{2} ", _last90kts, ts90khzSampleTime, tsTime + _90ktsoffset );
                            }

                            tsTime += _90ktsoffset;
                            _last90kts = (long)ts90khzSampleTime;
                        }
                        else
                        {
                            if (tsTime - _last90kts < _TsRolloverMarginIn90HZ)
                            {
                                // video rollovered first, don't change offset, just add the projected offset to video sample
                                tsTime += (_90ktsoffset + _last90kts - tsTime);
                                HLSTrace.WriteLine(" Video TS timestamp rollovered. previouis:{0}, new:{1} adjusted:{2} ", _last90kts, ts90khzSampleTime, tsTime);
                            }
                            else if (_last90kts - tsTime < _TsRolloverMarginIn90HZ && _90ktsoffset > (-_TsRolloverMarginIn90HZ ) )
                            {
                                // audio rollovered first, don't use the new offset directly
                                tsTime += (_90ktsoffset + _last90kts - tsTime );
                                HLSTrace.WriteLine(" Video TS timestamp has not rollover is after audio rollovered . previouis:{0}, new:{1} adjusted:{2} ", _last90kts, ts90khzSampleTime, tsTime);
                            }
                            else
                            {
                                tsTime += _90ktsoffset;
                            }
                        }
                    }
                    return MediaFormatParser.HnsTimestampFrom90kHzTimestamp((long)tsTime);
                }
            }

            /// <summary>
            // reset ts convertion offset
            /// </summary>
            public void ResetRolloverOffset()
            {
                _90ktsoffset = 0;
                _last90kts = 0;
            }

            // all minimum -1 hours to indicate timestamp rollover
            private const long _TsRolloverMarginIn90HZ = -( 2 * ConvertHelper.HourInHNS * 9 / 1000 );

            private long _last90kts = 0;
            private long _90ktsoffset = 0;
        }

        private List<TimeLineInfo> _timelineInfoList;


        /// <summary>
        // received a ts sample from parser
        /// </summary>
        /// <param name="tsSampleTime"> transport stream sample time in 90khz</param>
        /// <return> Presentation time in 100HZ</return>
        public long On90kHZTsSampleTime(int timelineIndex, ulong Pts90kHZ)       
        {
            MediaStreamType streamType = MediaStreamType.Video;
            if (_msd != null && _msd.Type == MediaStreamType.Audio)
            {
                streamType = MediaStreamType.Audio;
            }

            return _timelineInfoList[timelineIndex].OnTsSample( streamType, Pts90kHZ);
        }

        /// <summary>
        // reset ts rollover offset 
        /// </summary>
        /// <param name="tsSampleTime"> transport stream sample time in 90khz</param>
        /// <return> Presentation time in 100HZ</return>
        public void ResetTSRollverOffset()
        {
            foreach (TimeLineInfo timelineInfo in _timelineInfoList)
            {
                timelineInfo.ResetRolloverOffset();
            }
        }

        public class TimelineTrackingInfo
        {
            public int currentTimelineIndex;
            public long timelineStartTimeStampHNS;
            public long timelineEndTimeStampHNS;
            public TimelineTrackingInfo previousTimeline;
            public bool isApproaching = false;
            public TimeSpan lastRequestTimeStamp = TimeSpan.Zero;
            public long lastTimeStamp = -1;
            public long lastAdjustedTimeStamp = -1;

            public TimelineTrackingInfo(int timelindeIndex, long startTimeStampHNS, long endTimeStampHNS, TimelineTrackingInfo previousTimelineInfo)
            {
                currentTimelineIndex = timelindeIndex;
                timelineStartTimeStampHNS = startTimeStampHNS;
                timelineEndTimeStampHNS = endTimeStampHNS;
                previousTimeline = previousTimelineInfo;
            }
        }

        private TimelineTrackingInfo _timelineTrackingInfo;
        

        /// <summary>
        /// Default constructor.
        /// </summary>
        public SampleBuffer(int initialSize)
        {
            //
            // Init mamnaged fifo memory pool. Set shrinkThresholdPercentage to 0.3 so that when free size is bigger than
            // 30% of total size, pool can shrink to reduce memory consumption
            //
            _pool = new FIFOMemoryPool(initialSize, _shrinkThresholdPercentage);
            _samples = new Queue<Sample>();
            _sampleWrapperPool = new Queue<Sample>();
        }

        /// <summary>
        /// Returns true when there are available samples to play.
        /// </summary>
        public bool HasSamples
        {
            get
            {
                lock (this)
                {
                    return _samples.Count > 0;
                }
            }
        }

        /// <summary>
        /// Indicates how many requests for samples MediaElement did that weren't
        /// satisfied yet.
        /// </summary>
        public int NeedSamples
        {
            get
            {
                return _pendingSampleRequests;
            }
        }

        /// <summary>
        /// Indicates there will be no more samples. When HasSamples==false and EndOfPlayback==true,
        /// user of this class should return EndOfStreamSample() to MediaElement.
        /// </summary>
        public bool EndOfPlayback
        {
            get
            {
                return _endOfPlayback;
            }
            set
            {
                _endOfPlayback = value;
            }
        }

        /// <summary>
        /// MediaStreamDescription that can be directly passed into MediaStreamSource.
        /// </summary>
        public MediaStreamDescription Description
        {
            get
            {
                return _msd;
            }
            set
            {
                if (_msd == null)
                    _msd = value;
            }
        }


        /// <summary>
        /// Returns playback duration of samples currently in buffer.
        /// </summary>
        private TimeSpan _bufferLevel = TimeSpan.FromTicks(0);
        public TimeSpan BufferLevel
        {
            // BUGBUG: sample is not always continue in TS stream
            get
            {
                lock (this)
                {
                    return _bufferLevel;
                }
            }
        }

        /// <summary>
        /// Returns total size of all samples stored in buffer.
        /// </summary>
        public long BufferLevelInBytes
        {
            get
            {
                lock (this)
                {
                    return _totalSizeOfSamples;
                }
            }
        }

        /// <summary>
        /// Returns timestamp of next sample to play
        /// </summary>
        public long CurrentTimestamp
        {
            get
            {
                lock (this)
                {
                    if (_samples == null || _samples.Count == 0)
                        return 0;
                    else
                    {
                        return _samples.Peek().AdjustedTimeStamp;
                    }
                }
            }
        }

        public long MinimalStartTimeInAllTimelines
        {
            get
            {
                lock (this)
                {
                    long start = long.MaxValue;
                    foreach (TimeLineInfo info in _timelineInfoList)
                    {
                        if (info._timelineStartOffsetHNS < start)
                        {
                            start = info._timelineStartOffsetHNS;
                        }
                    }

                    return start;
                }
            }
        }

        public long MaxStartTimeInAllTimelines
        {
            get
            {
                lock (this)
                {
                    long start = long.MinValue;
                    foreach (TimeLineInfo info in _timelineInfoList)
                    {
                        if (info._timelineStartOffsetHNS > start)
                        {
                            start = info._timelineStartOffsetHNS;
                        }
                    }

                    return start;
                }
            }
        }

        /// <summary>
        /// Returns bitrate of stream that this sample was originated from. 
        /// </summary>
        public uint CurrentBitrate
        {
            get
            {
                lock (this)
                {
                    if (_samples == null || _samples.Count == 0)
                        return 0;
                    else
                    {
                        return _samples.Peek().Bitrate;
                    }
                }
            }
        }
        /// <summary>
        /// Discards all the samples currently in the queue
        /// </summary>
        public void Flush()
        {
            lock (this)
            {
                while (RemoveHead())
                    ;
                _pool.ConfirmEmpty();
            }

            HLSTrace.WriteLine("Flush sample buffer");
        }

        /// <summary>
        /// Produces next sample to play.
        /// </summary>
        /// <returns></returns>
        public Sample ProcessPendingSampleRequest()
        {
            Sample sample = null;

            lock (this)
            {
                if (!Interlocked.Equals(_pendingSampleRequests, 0))
                {
                    if (HasSamples)
                    {
                        sample = DequeueSample();
                    }
                    else if (EndOfPlayback)
                    {
                        sample = EndOfStreamSample();
                        // even if we have mutiple pending samples requests, EOS should only be sent once. 
                        System.Threading.Interlocked.Exchange(ref _pendingSampleRequests, 1);
                    }
                }

                if (sample != null)
                {
                    System.Threading.Interlocked.Decrement(ref _pendingSampleRequests);
                    Debug.Assert(_pendingSampleRequests >= 0);

                    HLSTrace.WriteLineLow("Delivered sample type {0}, size {1}, time stamp {2}",
                        sample.MSD.Type.ToString(), sample.DataStream.Length, sample.Timestamp.ToString());
                }
            }

            return sample;
        }

        /// <summary>
        /// Remove the first sample in the sample buffer. 
        /// </summary>
        /// <returns></returns>
        internal bool RemoveHead()
        {
            Sample sample = DequeueSample();
            if (sample != null)
            {
                RecycleSample(sample);
                return true;
            }
            else
                return false;
        }

        /// <summary>
        /// Produces next sample to play.
        /// </summary>
        /// <returns></returns>
        private Sample DequeueSample()
        {
            Sample sample = null;

            lock (this)
            {
                if (_samples.Count == 0)
                    return null;

                sample = _samples.Dequeue();

                _totalSizeOfSamples -= (int)sample.DataStream.Length;
                _bufferLevel -= sample.Duration;

                if (sample == _lastSample)
                    _lastSample = null;

                HLSTrace.WriteLineLow("DequeueSample from {0} to {1} timeline {2}", sample.Timestamp / ConvertHelper.MSInHNS, sample.AdjustedTimeStamp / ConvertHelper.MSInHNS, sample.TimelineIndex);

                if (null == _timelineTrackingInfo)
                {
                    _timelineTrackingInfo = new TimelineTrackingInfo(sample.TimelineIndex, sample.AdjustedTimeStamp, sample.AdjustedTimeStamp, null);
                }
                else
                {
                    if (sample.TimelineIndex != _timelineTrackingInfo.currentTimelineIndex)
                    {
                        _timelineTrackingInfo.previousTimeline = new TimelineTrackingInfo(_timelineTrackingInfo.currentTimelineIndex, _timelineTrackingInfo.timelineStartTimeStampHNS, _timelineTrackingInfo.timelineEndTimeStampHNS, null);
                        _timelineTrackingInfo.currentTimelineIndex = sample.TimelineIndex;
                        _timelineTrackingInfo.timelineStartTimeStampHNS = sample.AdjustedTimeStamp;
                        _timelineTrackingInfo.timelineEndTimeStampHNS = sample.AdjustedTimeStamp;
                    }
                    else
                    {
                        _timelineTrackingInfo.timelineEndTimeStampHNS = sample.AdjustedTimeStamp;
                    }
                }
            }

            return sample;
        }

        public Sample PeekSample()
        {
            lock (this)
            {
                if (HasSamples)
                {
                    return _samples.Peek();
                }
                else
                {
                    return null;
                }
            }
        }

        public Sample AllocSample(long timestamp, long duration, uint bitrate, HLSStream hlsStream, int dataSize)
        {
            lock (this)
            {
                if (0 == _sampleWrapperPool.Count)
                {
                    Sample sample = new Sample(timestamp, duration, bitrate, hlsStream, dataSize, _msd, this);
                    return sample;
                }
                else
                {
                    Sample sample = _sampleWrapperPool.Dequeue();
                    sample.Reset(timestamp, duration, bitrate, hlsStream, dataSize, _msd, this);
                    return sample;
                }
            }
        }

        public void RecycleSample(Sample sample)
        {
            lock (this)
            {
                if (null != sample)
                {
                    if (null != sample.PoolItem)
                    {
                        // Return raw sample buffer to fifo pool
                        sample.PoolItem.Close();
                    }

                    // return sample wrapper to wrapper pool
                    _sampleWrapperPool.Enqueue(sample);
                    // If fifo pool has shrinked, shrink wrapper pool as well
                    int poolFreeSampleCount = _pool.PoolFreeCount;
                    while (poolFreeSampleCount < _sampleWrapperPool.Count)
                    {
                        _sampleWrapperPool.Dequeue();
                    }
                }
            }
        }

        /// <summary>
        /// Adds a new sample to the buffer.
        /// </summary>
        /// <param name="sample"></param>
        public void Enqueue(Sample sample)
        {
            lock (this)
            {
                Debug.Assert(_timelineInfoList.Count > sample.TimelineIndex);

                if (-1 == _timelineInfoList[sample.TimelineIndex]._timelineStartOffsetHNS)
                {
                    sample.AdjustedTimeStamp = sample.Timestamp;
                }
                else
                {
                    if (-1 == _timelineInfoList[sample.TimelineIndex]._timelineStartTimeStamp)
                    {
                        _timelineInfoList[sample.TimelineIndex]._timelineStartTimeStamp = sample.Timestamp;
                        sample.AdjustedTimeStamp = _timelineInfoList[sample.TimelineIndex]._timelineStartOffsetHNS;
                    }
                    else
                    {
                        sample.AdjustedTimeStamp = sample.Timestamp - _timelineInfoList[sample.TimelineIndex]._timelineStartTimeStamp + _timelineInfoList[sample.TimelineIndex]._timelineStartOffsetHNS;
                    }
                }

                if (null == _timelineTrackingInfo)
                {
                    _timelineTrackingInfo = new TimelineTrackingInfo(sample.TimelineIndex, sample.AdjustedTimeStamp, sample.AdjustedTimeStamp, null);
                }
                _timelineTrackingInfo.lastTimeStamp = sample.Timestamp;
                _timelineTrackingInfo.lastAdjustedTimeStamp = sample.AdjustedTimeStamp;
                _samples.Enqueue(sample);
                _totalSizeOfSamples += (int)sample.DataStream.Length;
                _bufferLevel += sample.Duration;
                _lastSample = sample;
                if (_hnsSegmentStart == -1 )
                {
                    _hnsSegmentStart = sample.AdjustedTimeStamp;
                }

                SegmentProgramDateTime segmentTime =sample.HLSStream.SegmentProgramTime;
                if ( segmentTime!= null && segmentTime.programTime.TsStartTime == DateTime.MinValue)
                {
                    segmentTime.programTime.TsStartTime = segmentTime.programTime.startTime + segmentTime.offset - TimeSpan.FromTicks(sample.AdjustedTimeStamp);
                }
            }
            
            HLSTrace.WriteLineLow("Enqueue sample timestamp {0} adjust to {1}, size {2}, timeline {3}", sample.Timestamp / ConvertHelper.MSInHNS, sample.AdjustedTimeStamp / ConvertHelper.MSInHNS, sample.DataStream.Length, sample.TimelineIndex);
        }

        /// <summary>
        /// Produces special sample to indicate end of playback.
        /// </summary>
        /// <returns></returns>
        public Sample EndOfStreamSample()
        {
            return new Sample(0, 0, 0, null, -1, _msd, null);
        }

        public TimelineEventInfo GetTimelineEventInfo(TimeSpan timeStamp)
        {
            lock (this)
            {
                // TODO: Fine tune event approaching mechanism
                bool isApproachingCandidate = false;
                bool isTriggeredCandidate = false;
                if (null != _timelineTrackingInfo)
                {
                    long deltaToNextStartHNS = timeStamp.Ticks - _timelineTrackingInfo.timelineStartTimeStampHNS;
                    if (null != _timelineTrackingInfo.previousTimeline)
                    {
                        long deltaToEndHNS = _timelineTrackingInfo.previousTimeline.timelineEndTimeStampHNS - timeStamp.Ticks;

                        if (deltaToEndHNS <= TimeSpan.TicksPerSecond && deltaToEndHNS >= 0)
                        {
                            isApproachingCandidate = true;
                        }

                        if (deltaToNextStartHNS <= TimeSpan.TicksPerSecond && deltaToNextStartHNS >= 0)
                        {
                            isTriggeredCandidate = true;
                        }

                        if (isApproachingCandidate && !isTriggeredCandidate)
                        {
                            _timelineTrackingInfo.isApproaching = true;
                        }
                        else if (isTriggeredCandidate && !isApproachingCandidate)
                        {
                            _timelineTrackingInfo.isApproaching = false;
                            _timelineTrackingInfo.previousTimeline = null;
                        }
                        else if (isApproachingCandidate && isTriggeredCandidate)
                        {
                            if (_timelineTrackingInfo.lastRequestTimeStamp > timeStamp)
                            {
                                _timelineTrackingInfo.isApproaching = false;
                                _timelineTrackingInfo.previousTimeline = null;
                            }
                            else
                            {
                                _timelineTrackingInfo.isApproaching = true;
                            }
                        }
                        else
                        {
                            _timelineTrackingInfo.isApproaching = false;
                            if (deltaToNextStartHNS < 0)
                            {
                                return null;
                            }
                            _timelineTrackingInfo.previousTimeline = null;
                        }
                    }
                    else
                    {
                        _timelineTrackingInfo.isApproaching = false;
                        if (deltaToNextStartHNS < 0)
                        {
                            return null;
                        }
                    }
                    _timelineTrackingInfo.lastRequestTimeStamp = timeStamp;

                    TimelineEventInfo info = new TimelineEventInfo();
                    info.timelineIndex = _timelineTrackingInfo.isApproaching ? _timelineTrackingInfo.previousTimeline.currentTimelineIndex : _timelineTrackingInfo.currentTimelineIndex;
                    if (_timelineTrackingInfo.isApproaching)
                    {
                        info.timelineEvent = new TimelineEventInfo.TimelineEvent();
                        info.timelineEvent.upcomingTimelineIndex = _timelineTrackingInfo.currentTimelineIndex;
                        info.timelineEvent.upcomingTimelineStartTimeStampHNS = _timelineTrackingInfo.timelineStartTimeStampHNS;
                    }

                    return info;
                }
                else
                {
                    return null;
                }
            }
        }

        public void EstablishTimeline(List<TimeLineInfo> timelineInfoList)
        {
            lock (this)
            {
                _timelineInfoList = timelineInfoList;
                _timelineTrackingInfo = null;
            }
        }
    }

    public class FIFOMemoryPool
    {
        /// <summary>
        /// Block size, default 256KB
        /// </summary>
        private int _blockSize = 256 * 1024;

        private int _initialSize = 1;

        /// <summary>
        /// How many blocks in pool
        /// </summary>
        private int _blockCount = 0;

        private int _totalSize = 0;

        private int _freeSize = 0;

        private int _allocatedCount = 0;

        private long _allocCount;

        private long _freeCount;

        private long _accumulatedAllocSize;

        private long _growCount;

        private long _shrinkCount;

        internal class PoolBlock
        {
            public byte[] _bytes;
        }

        List<PoolBlock> _listOfBlocks;

        private double _shrinkThresholdPercentage = 1.0f;

        /// <summary>
        /// Index data point to an offset in a block
        /// </summary>
        public class PoolBlockIndex
        {
            public int _blockIndex;
            public int _blockOffset;

            public PoolBlockIndex(int index, int offset)
            {
                _blockIndex = index;
                _blockOffset = offset;
            }

            public PoolBlockIndex(PoolBlockIndex index)
            {
                _blockIndex = index._blockIndex;
                _blockOffset = index._blockOffset;
            }
        }

        /// <summary>
        /// Index which is used to allocate memory from the pool
        /// </summary>
        private PoolBlockIndex _allocIndex;

        /// <summary>
        /// Index which is used to reclaim memory to the pool
        /// </summary>
        private PoolBlockIndex _reclaimIndex;

        public class PoolAllocItem : Stream
        {
            private PoolBlockIndex _startIndex;

            public PoolBlockIndex StartIndex
            {
                get
                {
                    return _startIndex;
                }
            }

            private PoolBlockIndex _endIndex;

            public PoolBlockIndex EndIndex
            {
                get
                {
                    return _endIndex;
                }
            }

            private int _writeOffset;

            private int _readOffset;

            private FIFOMemoryPool _pool;

            public PoolAllocItem(PoolBlockIndex startIndex, PoolBlockIndex endIndex, FIFOMemoryPool pool)
            {
                _startIndex = startIndex;
                _endIndex = endIndex;
                _writeOffset = 0;
                _readOffset = 0;
                _pool = pool;
            }

            public void Reset(FIFOMemoryPool pool)
            {
                lock (this)
                {
                    _writeOffset = 0;
                    _readOffset = 0;
                    _pool = pool;
                }
            }

            public void IncrementIndex()
            {
                lock (this)
                {
                    if (_endIndex._blockIndex > _startIndex._blockIndex || (_endIndex._blockIndex == _startIndex._blockIndex && _endIndex._blockOffset > _startIndex._blockOffset))
                    {
                        _endIndex._blockIndex++;
                    }
                    _startIndex._blockIndex++;
                }
            }

            // Summary:
            //     When overridden in a derived class, gets a value indicating whether the current
            //     stream supports reading.
            //
            // Returns:
            //     true if the stream supports reading; otherwise, false.
            public override bool CanRead
            {
                get
                {
                    return true;
                }
            }

            //
            // Summary:
            //     When overridden in a derived class, gets a value indicating whether the current
            //     stream supports seeking.
            //
            // Returns:
            //     true if the stream supports seeking; otherwise, false.
            public override bool CanSeek
            {
                get
                {
                    return true;
                }
            }
            //
            // Summary:
            //     Gets a value that determines whether the current stream can time out.
            //
            // Returns:
            //     A value that determines whether the current stream can time out.
            public override bool CanTimeout
            {
                get
                {
                    return false;
                }
            }
            //
            // Summary:
            //     When overridden in a derived class, gets a value indicating whether the current
            //     stream supports writing.
            //
            // Returns:
            //     true if the stream supports writing; otherwise, false.
            public override bool CanWrite
            {
                get
                {
                    return true;
                }
            }
            //
            // Summary:
            //     When overridden in a derived class, gets the length in bytes of the stream.
            //
            // Returns:
            //     A long value representing the length of the stream in bytes.
            //
            // Exceptions:
            //   System.NotSupportedException:
            //     A class derived from Stream does not support seeking.
            //
            //   System.ObjectDisposedException:
            //     Methods were called after the stream was closed.
            public override long Length
            {
                get
                {
                    lock (this)
                    {
                        return _pool.CalculateSize(_startIndex, _endIndex);
                    }
                }
            }
            //
            // Summary:
            //     When overridden in a derived class, gets or sets the position within the
            //     current stream.
            //
            // Returns:
            //     The current position within the stream.
            //
            // Exceptions:
            //   System.IO.IOException:
            //     An I/O error occurs.
            //
            //   System.NotSupportedException:
            //     The stream does not support seeking.
            //
            //   System.ObjectDisposedException:
            //     Methods were called after the stream was closed.
            public override long Position
            {
                get
                {
                    throw new System.NotSupportedException("The stream does not support seeking");
                }
                set
                {
                    throw new System.NotSupportedException("The stream does not support seeking");
                }
            }
            //
            // Summary:
            //     Gets or sets a value, in miliseconds, that determines how long the stream
            //     will attempt to read before timing out.
            //
            // Returns:
            //     A value, in miliseconds, that determines how long the stream will attempt
            //     to read before timing out.
            //
            // Exceptions:
            //   System.InvalidOperationException:
            //     The System.IO.Stream.ReadTimeout method always throws an System.InvalidOperationException.
            public override int ReadTimeout
            {
                get
                {
                    throw new System.NotSupportedException("The stream does not support read timeout");
                }
                set
                {
                    throw new System.NotSupportedException("The stream does not support read timeout");
                }
            }
            //
            // Summary:
            //     Gets or sets a value, in miliseconds, that determines how long the stream
            //     will attempt to write before timing out.
            //
            // Returns:
            //     A value, in miliseconds, that determines how long the stream will attempt
            //     to write before timing out.
            //
            // Exceptions:
            //   System.InvalidOperationException:
            //     The System.IO.Stream.WriteTimeout method always throws an System.InvalidOperationException.
            public override int WriteTimeout
            {
                get
                {
                    throw new System.NotSupportedException("The stream does not support write timeout");
                }
                set
                {
                    throw new System.NotSupportedException("The stream does not support write timeout");
                }
            }

            // Summary:
            //     Begins an asynchronous read operation.
            //
            // Parameters:
            //   buffer:
            //     The buffer to read the data into.
            //
            //   offset:
            //     The byte offset in buffer at which to begin writing data read from the stream.
            //
            //   count:
            //     The maximum number of bytes to read.
            //
            //   callback:
            //     An optional asynchronous callback, to be called when the read is complete.
            //
            //   state:
            //     A user-provided object that distinguishes this particular asynchronous read
            //     request from other requests.
            //
            // Returns:
            //     An System.IAsyncResult that represents the asynchronous read, which could
            //     still be pending.
            //
            // Exceptions:
            //   System.IO.IOException:
            //     Attempted an asynchronous read past the end of the stream, or a disk error
            //     occurs.
            //
            //   System.ArgumentException:
            //     One or more of the arguments is invalid.
            //
            //   System.ObjectDisposedException:
            //     Methods were called after the stream was closed.
            //
            //   System.NotSupportedException:
            //     The current Stream implementation does not support the read operation.
            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                throw new System.NotSupportedException("The stream does not support async read");
            }
            //
            // Summary:
            //     Begins an asynchronous write operation.
            //
            // Parameters:
            //   buffer:
            //     The buffer to write data from.
            //
            //   offset:
            //     The byte offset in buffer from which to begin writing.
            //
            //   count:
            //     The maximum number of bytes to write.
            //
            //   callback:
            //     An optional asynchronous callback, to be called when the write is complete.
            //
            //   state:
            //     A user-provided object that distinguishes this particular asynchronous write
            //     request from other requests.
            //
            // Returns:
            //     An IAsyncResult that represents the asynchronous write, which could still
            //     be pending.
            //
            // Exceptions:
            //   System.IO.IOException:
            //     Attempted an asynchronous write past the end of the stream, or a disk error
            //     occurs.
            //
            //   System.ArgumentException:
            //     One or more of the arguments is invalid.
            //
            //   System.ObjectDisposedException:
            //     Methods were called after the stream was closed.
            //
            //   System.NotSupportedException:
            //     The current Stream implementation does not support the write operation.
            public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                throw new System.NotSupportedException("The stream does not support async write");
            }
            //
            // Summary:
            //     Closes the current stream and releases any resources (such as sockets and
            //     file handles) associated with the current stream.
            public override void Close()
            {
                int loopCount = 0;

                while (true)
                {
                    if (loopCount <= 1)
                    {
                        loopCount++;
                    }

                    if (loopCount > 1)
                    {
                        Thread.Sleep(0);
                    }

                    lock (this)
                    {
                        int ret = _pool.Free(this);
                        if (-1 == ret)
                        {
                            continue;
                        }
                        return;
                    }
                }
            }

            //
            // Summary:
            //     Releases the unmanaged resources used by the System.IO.Stream and optionally
            //     releases the managed resources.
            //
            // Parameters:
            //   disposing:
            //     true to release both managed and unmanaged resources; false to release only
            //     unmanaged resources.
            protected override void Dispose(bool disposing)
            {

            }

            //
            // Summary:
            //     Waits for the pending asynchronous read to complete.
            //
            // Parameters:
            //   asyncResult:
            //     The reference to the pending asynchronous request to finish.
            //
            // Returns:
            //     The number of bytes read from the stream, between zero (0) and the number
            //     of bytes you requested. Streams return zero (0) only at the end of the stream,
            //     otherwise, they should block until at least one byte is available.
            //
            // Exceptions:
            //   System.ArgumentNullException:
            //     asyncResult is null.
            //
            //   System.ArgumentException:
            //     asyncResult did not originate from a System.IO.Stream.BeginRead(System.Byte[],System.Int32,System.Int32,System.AsyncCallback,System.Object)
            //     method on the current stream.
            //
            //   System.IO.IOException:
            //     The stream is closed or an internal error has occurred.
            public override int EndRead(IAsyncResult asyncResult)
            {
                throw new System.NotSupportedException("The stream does not support async read");
            }
            //
            // Summary:
            //     Ends an asynchronous write operation.
            //
            // Parameters:
            //   asyncResult:
            //     A reference to the outstanding asynchronous I/O request.
            //
            // Exceptions:
            //   System.ArgumentNullException:
            //     asyncResult is null.
            //
            //   System.ArgumentException:
            //     asyncResult did not originate from a System.IO.Stream.BeginWrite(System.Byte[],System.Int32,System.Int32,System.AsyncCallback,System.Object)
            //     method on the current stream.
            //
            //   System.IO.IOException:
            //     The stream is closed or an internal error has occurred.
            public override void EndWrite(IAsyncResult asyncResult)
            {
                throw new System.NotSupportedException("The stream does not support async write");
            }
            //
            // Summary:
            //     When overridden in a derived class, clears all buffers for this stream and
            //     causes any buffered data to be written to the underlying device.
            //
            // Exceptions:
            //   System.IO.IOException:
            //     An I/O error occurs.
            public override void Flush()
            {

            }
            //
            // Summary:
            //     When overridden in a derived class, reads a sequence of bytes from the current
            //     stream and advances the position within the stream by the number of bytes
            //     read.
            //
            // Parameters:
            //   buffer:
            //     An array of bytes. When this method returns, the buffer contains the specified
            //     byte array with the values between offset and (offset + count - 1) replaced
            //     by the bytes read from the current source.
            //
            //   offset:
            //     The zero-based byte offset in buffer at which to begin storing the data read
            //     from the current stream.
            //
            //   count:
            //     The maximum number of bytes to be read from the current stream.
            //
            // Returns:
            //     The total number of bytes read into the buffer. This can be less than the
            //     number of bytes requested if that many bytes are not currently available,
            //     or zero (0) if the end of the stream has been reached.
            //
            // Exceptions:
            //   System.ArgumentException:
            //     The sum of offset and count is larger than the buffer length.
            //
            //   System.ArgumentNullException:
            //     buffer is null.
            //
            //   System.ArgumentOutOfRangeException:
            //     offset or count is negative.
            //
            //   System.IO.IOException:
            //     An I/O error occurs.
            //
            //   System.NotSupportedException:
            //     The stream does not support reading.
            //
            //   System.ObjectDisposedException:
            //     Methods were called after the stream was closed.
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (null == buffer)
                {
                    throw new System.ArgumentNullException();
                }
                if (offset < 0 || count < 0)
                {
                    throw new System.ArgumentOutOfRangeException();
                }
                if (offset + count > buffer.GetLength(0))
                {
                    throw new System.ArgumentException();
                }
                if (0 == count)
                {
                    return 0;
                }

                int loopCount = 0;
                while (true)
                {
                    if (loopCount <= 1)
                    {
                        loopCount++;
                    }

                    if (loopCount > 1)
                    {
                        Thread.Sleep(0);
                    }

                    lock (this)
                    {
                        if (count > Length - _readOffset)
                        {
                            throw new System.ArgumentOutOfRangeException();
                        }

                        int bytesRead = _pool.Read(buffer, offset, _startIndex, _readOffset, count);
                        if (-1 == bytesRead)
                        {
                            continue;
                        }
                        _readOffset += bytesRead;
                        return bytesRead;
                    }
                }
            }
            //
            // Summary:
            //     Reads a byte from the stream and advances the position within the stream
            //     by one byte, or returns -1 if at the end of the stream.
            //
            // Returns:
            //     The unsigned byte cast to an Int32, or -1 if at the end of the stream.
            //
            // Exceptions:
            //   System.NotSupportedException:
            //     The stream does not support reading.
            //
            //   System.ObjectDisposedException:
            //     Methods were called after the stream was closed.
            public override int ReadByte()
            {
                throw new System.NotSupportedException("The stream does not support ReadByte");
            }
            //
            // Summary:
            //     When overridden in a derived class, sets the position within the current
            //     stream.
            //
            // Parameters:
            //   offset:
            //     A byte offset relative to the origin parameter.
            //
            //   origin:
            //     A value of type System.IO.SeekOrigin indicating the reference point used
            //     to obtain the new position.
            //
            // Returns:
            //     The new position within the current stream.
            //
            // Exceptions:
            //   System.IO.IOException:
            //     An I/O error occurs.
            //
            //   System.NotSupportedException:
            //     The stream does not support seeking, such as if the stream is constructed
            //     from a pipe or console output.
            //
            //   System.ObjectDisposedException:
            //     Methods were called after the stream was closed.
            public override long Seek(long offset, SeekOrigin origin)
            {
                lock (this)
                {
                    if (SeekOrigin.Begin == origin)
                    {
                        if (offset > Length || offset < 0)
                        {
                            throw new System.ArgumentOutOfRangeException("Seek out of range");
                        }
                        _readOffset = (int)offset;
                    }
                    else if (SeekOrigin.Current == origin)
                    {
                        int newOffset = (int)(_readOffset + offset);
                        if (newOffset > Length || newOffset < 0)
                        {
                            throw new System.ArgumentOutOfRangeException("Seek out of range");
                        }
                        _readOffset = newOffset;
                    }
                    else if (SeekOrigin.End == origin)
                    {
                        int newOffset = (int)(Length - 1 + offset);
                        if (newOffset > Length || newOffset < 0)
                        {
                            throw new System.ArgumentOutOfRangeException("Seek out of range");
                        }
                        _readOffset = newOffset;
                    }
                    else
                    {
                        throw new System.NotSupportedException(String.Format("Unsupported SeekOrigin {0}", origin));
                    }

                    return _readOffset;
                }
            }
            //
            // Summary:
            //     When overridden in a derived class, sets the length of the current stream.
            //
            // Parameters:
            //   value:
            //     The desired length of the current stream in bytes.
            //
            // Exceptions:
            //   System.IO.IOException:
            //     An I/O error occurs.
            //
            //   System.NotSupportedException:
            //     The stream does not support both writing and seeking, such as if the stream
            //     is constructed from a pipe or console output.
            //
            //   System.ObjectDisposedException:
            //     Methods were called after the stream was closed.
            public override void SetLength(long value)
            {
                throw new System.NotSupportedException("The stream does not support SetLength");
            }
            //
            // Summary:
            //     When overridden in a derived class, writes a sequence of bytes to the current
            //     stream and advances the current position within this stream by the number
            //     of bytes written.
            //
            // Parameters:
            //   buffer:
            //     An array of bytes. This method copies count bytes from buffer to the current
            //     stream.
            //
            //   offset:
            //     The zero-based byte offset in buffer at which to begin copying bytes to the
            //     current stream.
            //
            //   count:
            //     The number of bytes to be written to the current stream.
            //
            // Exceptions:
            //   System.ArgumentException:
            //     The sum of offset and count is greater than the buffer length.
            //
            //   System.ArgumentNullException:
            //     buffer is null.
            //
            //   System.ArgumentOutOfRangeException:
            //     offset or count is negative.
            //
            //   System.IO.IOException:
            //     An I/O error occurs.
            //
            //   System.NotSupportedException:
            //     The stream does not support writing.
            //
            //   System.ObjectDisposedException:
            //     Methods were called after the stream was closed.
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (null == buffer)
                {
                    throw new System.ArgumentNullException();
                }
                if (offset < 0 || count < 0)
                {
                    throw new System.ArgumentOutOfRangeException();
                }
                if (offset + count > buffer.GetLength(0))
                {
                    throw new System.ArgumentException();
                }
                if (0 == count)
                {
                    return;
                }

                int loopCount = 0;
                while (true)
                {
                    if (loopCount <= 1)
                    {
                        loopCount++;
                    }

                    if (loopCount > 1)
                    {
                        Thread.Sleep(0);
                    }

                    lock (this)
                    {
                        int size = (int)(Length - _writeOffset);
                        if (count > size)
                        {
                            int ret1 = _pool.GrowAlloc(this, count - size);
                            if (-1 == ret1)
                            {
                                continue;
                            }
                        }

                        int ret2 = _pool.Write(buffer, offset, _startIndex, _writeOffset, count);
                        if (-1 == ret2)
                        {
                            continue;
                        }
                        _writeOffset += count;
                        return;
                    }
                }
            }
            //
            // Summary:
            //     Writes a byte to the current position in the stream and advances the position
            //     within the stream by one byte.
            //
            // Parameters:
            //   value:
            //     The byte to write to the stream.
            //
            // Exceptions:
            //   System.IO.IOException:
            //     An I/O error occurs.
            //
            //   System.NotSupportedException:
            //     The stream does not support writing, or the stream is already closed.
            //
            //   System.ObjectDisposedException:
            //     Methods were called after the stream was closed.
            public override void WriteByte(byte value)
            {
                throw new System.NotSupportedException("The stream does not support WriteByte");
            }
        }

        List<PoolAllocItem> _listOfAllocItems;

        Queue<PoolAllocItem> _itemPool;

        /// <summary>
        /// Construct a growable and shrinkable fifo memory pool with block size
        /// </summary>
        /// <param name="initialPoolSize">Initial pool size</param>
        /// <param name="shrinkThresholdPercentage">When free size is over shrinkThresholdPercentage of total size,
        /// pool tries to shrink. But it will never shrink down to smaller than initial size
        /// </param>
        /// <param name="blockSize">block size of pool, pool grows by block</param>
        public FIFOMemoryPool(int initialPoolSize, double shrinkThresholdPercentage, int blockSize)
        {
            CommonConstruct(initialPoolSize, shrinkThresholdPercentage, blockSize);
        }

        /// <summary>
        /// Construct a growable shrinkable fifo memory pool at given initial size
        /// </summary>
        /// <param name="initialPoolSize">Initial pool size</param>
        /// <param name="shrinkThresholdPercentage">When free size is over shrinkThresholdPercentage of total size,
        /// pool tries to shrink. But it will never shrink down to smaller than initial size
        /// </param>
        public FIFOMemoryPool(int initialPoolSize, double shrinkThresholdPercentage)
        {
            CommonConstruct(initialPoolSize, shrinkThresholdPercentage, _blockSize);
        }

        /// <summary>
        /// Construct a growable non-shrinkable fifo memory pool at given initial size
        /// </summary>
        /// <param name="initialPoolSize">Initial pool size</param>
        public FIFOMemoryPool(int initialPoolSize)
        {
            CommonConstruct(initialPoolSize, _shrinkThresholdPercentage, _blockSize);
        }

        /// <summary>
        /// Construct a growable non-shrinkable fifo memory pool with no initial size specified
        /// </summary>
        public FIFOMemoryPool()
        {
            CommonConstruct(_initialSize, _shrinkThresholdPercentage, _blockSize);
        }

        /// <summary>
        /// Common construct a growable and shrinkable fifo memory pool with block size
        /// </summary>
        /// <param name="initialPoolSize">Initial pool size</param>
        /// <param name="shrinkThresholdPercentage">When free size is over shrinkThresholdPercentage of total size,
        /// pool tries to shrink. But it will never shrink down to smaller than initial size
        /// </param>
        /// <param name="blockSize">Block size of pool, pool grows by block</param>
        private void CommonConstruct(int initialPoolSize, double shrinkThresholdPercentage, int blockSize)
        {
            if (shrinkThresholdPercentage <= 0.0 || shrinkThresholdPercentage > 1.0)
            {
                throw new System.NotSupportedException("shrinkThresholdPercentage must be bigger than 0.0f, smaller or equal to 1.0");
            }
            _blockSize = blockSize;
            _blockCount = (initialPoolSize + _blockSize - 1) / _blockSize;
            if (0 == _blockCount)
            {
                _blockCount = 1;
            }
            _listOfBlocks = new List<PoolBlock>();
            for (int i = 0; i < _blockCount; i++)
            {
                PoolBlock poolBlock = new PoolBlock();
                poolBlock._bytes = new byte[_blockSize];
                _listOfBlocks.Add(poolBlock);
            }
            _totalSize = _blockCount * _blockSize;
            _allocIndex = new PoolBlockIndex(0, 0);
            _reclaimIndex = new PoolBlockIndex(0, 0);
            _freeSize = _totalSize;
            _listOfAllocItems = new List<PoolAllocItem>();
            _itemPool = new Queue<PoolAllocItem>();
            _allocCount = 0;
            _freeCount = 0;
            _accumulatedAllocSize = 0;
            _shrinkThresholdPercentage = shrinkThresholdPercentage;
            _initialSize = _totalSize;
        }

        public int FreeSize
        {
            get
            {
                lock (this)
                {
                    return _freeSize;
                }
            }
        }

        public int TotalSize
        {
            get
            {
                lock (this)
                {
                    return _totalSize;
                }
            }
        }

        /// <summary>
        /// How many free PoolAllocItem are pooled
        /// </summary>
        public int PoolFreeCount
        {
            get
            {
                lock (this)
                {
                    return _itemPool.Count;
                }
            }
        }

        /// <summary>
        /// How many allocations have not being returned to pool yet
        /// </summary>
        public int PoolAllocationCount
        {
            get
            {
                lock (this)
                {
                    return _allocatedCount;
                }
            }
        }

        /// <summary>
        /// Following properties are lock-free because they are used for statistics purpose
        /// </summary>


        public long AllocCount
        {
            get
            {
                return _allocCount;
            }
        }

        public long FreeCount
        {
            get
            {
                return _freeCount;
            }
        }

        public long AccumulatedAllocSize
        {
            get
            {
                return _accumulatedAllocSize;
            }
        }

        public long GrowCount
        {
            get
            {
                return _growCount;
            }
        }

        public long ShrinkCount
        {
            get
            {
                return _shrinkCount;
            }
        }

        public void ConfirmEmpty()
        {
            lock (this)
            {
                Debug.Assert(_totalSize == _freeSize);
                Debug.Assert(0 == _listOfAllocItems.Count);
                Debug.Assert(_allocIndex._blockIndex == _reclaimIndex._blockIndex && _allocIndex._blockOffset == _reclaimIndex._blockOffset);

                if (_totalSize != _freeSize)
                {
                    HLSTrace.WriteLine("Warning: Memory pool is not empty!");
                    while (_totalSize != _freeSize)
                    {
                        Free(_listOfAllocItems[0]);
                    }
                }
            }
        }

        public PoolAllocItem Alloc(int size)
        {
            lock (this)
            {
                while (_freeSize < size)
                {
                    GrowPool();
                }

                int absoluteAllocOffset = _allocIndex._blockIndex * _blockSize + _allocIndex._blockOffset;
                int newAbsoluteAllocOffset = (absoluteAllocOffset + size) % _totalSize;
                int index = newAbsoluteAllocOffset / _blockSize;
                int offset = newAbsoluteAllocOffset % _blockSize;
                PoolAllocItem allocItem = null;
                if (_itemPool.Count == 0)
                {
                    allocItem = new PoolAllocItem(new PoolBlockIndex(_allocIndex), new PoolBlockIndex(index, offset), this);
                }
                else
                {
                    allocItem = _itemPool.Dequeue();
                    allocItem.Reset(this);
                    allocItem.StartIndex._blockIndex = _allocIndex._blockIndex;
                    allocItem.StartIndex._blockOffset = _allocIndex._blockOffset;
                    allocItem.EndIndex._blockIndex = index;
                    allocItem.EndIndex._blockOffset = offset;
                }
                _allocIndex._blockIndex = index;
                _allocIndex._blockOffset = offset;
                _listOfAllocItems.Add(allocItem);
                _freeSize -= size;
                _allocCount++;
                _accumulatedAllocSize += size;
                _allocatedCount++;
                Debug.Assert(_freeSize >= 0 && _freeSize <= _totalSize);
                return allocItem;
            }
        }

        private void GrowPool()
        {
            HLSTrace.WriteLine("Try to grow pool");
            int absoluteAllocOffset = _allocIndex._blockIndex * _blockSize + _allocIndex._blockOffset;
            int absoluteReclaimOffset = _reclaimIndex._blockIndex * _blockSize + _reclaimIndex._blockOffset;

            if (absoluteAllocOffset >= absoluteReclaimOffset && _freeSize != 0)
            {
                // Allocation offset is bigger or equal to reclaim offset, and free size is not zero, 
                // inserting a new elelment at the end is the cheapest way to grwo the pool
                _blockCount++;
                PoolBlock poolBlock = new PoolBlock();
                poolBlock._bytes = new byte[_blockSize];
                _listOfBlocks.Add(poolBlock);
                _totalSize += _blockSize;
                _freeSize += _blockSize;
            }
            else if (absoluteAllocOffset > absoluteReclaimOffset && _freeSize == 0)
            {
                Debug.Assert(false, "Against design assumption!");
            }
            else
            {
                // Insert a new elelment after allocation block
                PoolBlock poolBlock = new PoolBlock();
                poolBlock._bytes = new byte[_blockSize];

                if (_allocIndex._blockIndex == _reclaimIndex._blockIndex)
                {
                    // Move
                    Array.Copy(_listOfBlocks[_reclaimIndex._blockIndex]._bytes, _reclaimIndex._blockOffset, poolBlock._bytes, _reclaimIndex._blockOffset, _blockSize - _reclaimIndex._blockOffset);
                }

                for (int i = 0; i < _listOfAllocItems.Count; i++)
                {
                    int absoluteItemStartIndex = _listOfAllocItems[i].StartIndex._blockIndex * _blockSize + _listOfAllocItems[i].StartIndex._blockOffset;
                    if (absoluteItemStartIndex >= absoluteReclaimOffset)
                    {
                        _listOfAllocItems[i].IncrementIndex();
                    }
                }

                _listOfBlocks.Insert(_allocIndex._blockIndex + 1, poolBlock);
                _totalSize += _blockSize;
                _freeSize += _blockSize;
                _reclaimIndex._blockIndex++;
                _blockCount++;
            }

            _growCount++;

            if (_listOfAllocItems.Count > 0)
            {
                Debug.Assert(_listOfAllocItems[0].StartIndex._blockIndex == _reclaimIndex._blockIndex &&
                             _listOfAllocItems[0].StartIndex._blockOffset == _reclaimIndex._blockOffset);
            }
        }

        public int Free(PoolAllocItem allocItem)
        {
            bool lockAcquired = false;

            try
            {
                if (null != allocItem)
                {
                    lockAcquired = Monitor.TryEnter(this);

                    if (lockAcquired)
                    {
                        if (allocItem == _listOfAllocItems[0])
                        {
                            Debug.Assert(allocItem.StartIndex._blockIndex == _reclaimIndex._blockIndex &&
                                         allocItem.StartIndex._blockOffset == _reclaimIndex._blockOffset);
                            _reclaimIndex._blockIndex = allocItem.EndIndex._blockIndex;
                            _reclaimIndex._blockOffset = allocItem.EndIndex._blockOffset;
                            _listOfAllocItems.RemoveAt(0);
                            _freeSize += (int)(allocItem.Length);
                            _itemPool.Enqueue(allocItem);
                            Debug.Assert(_freeSize >= 0 && _freeSize <= _totalSize);
                        }
                        else if (allocItem == _listOfAllocItems[_listOfAllocItems.Count - 1])
                        {
                            _listOfAllocItems.RemoveAt(_listOfAllocItems.Count - 1);
                            _allocIndex._blockIndex = allocItem.StartIndex._blockIndex;
                            _allocIndex._blockOffset = allocItem.StartIndex._blockOffset;
                            _freeSize += (int)(allocItem.Length);
                            _itemPool.Enqueue(allocItem);
                            if (_listOfAllocItems.Count > 0)
                            {
                                Debug.Assert(_listOfAllocItems[_listOfAllocItems.Count - 1].EndIndex._blockIndex == _allocIndex._blockIndex &&
                                             _listOfAllocItems[_listOfAllocItems.Count - 1].EndIndex._blockOffset == _allocIndex._blockOffset);
                            }
                        }
                        else
                        {
                            Debugger.Break();
                            throw new System.NotSupportedException("FIFOMemoryPool only supports first alloc first reclaim, or reclaim most recent allocation");
                        }
                        _freeCount++;
                        _allocatedCount--;

                        if (_freeSize > _shrinkThresholdPercentage * _totalSize && _totalSize > _initialSize)
                        {
                            int blocksToShrink = 0;
                            if (_freeSize == _totalSize)
                            {
                                blocksToShrink = Math.Min(_blockCount / 2, (_blockCount - _allocIndex._blockIndex - 1) / 2);
                                blocksToShrink = Math.Min(blocksToShrink, (_totalSize - _initialSize) / _blockSize);
                            }
                            else if (_allocIndex._blockIndex > _reclaimIndex._blockIndex)
                            {
                                blocksToShrink = (_blockCount - _allocIndex._blockIndex - 1) / 2;
                                blocksToShrink = Math.Min(blocksToShrink, (_totalSize - _initialSize) / _blockSize);
                            }

                            if (blocksToShrink > 0)
                            {
                                HLSTrace.WriteLine("Try to shrink pool");
                                double shrinkRatio = (double)blocksToShrink / _blockCount;
                                int itemPoolToShrink = (int)(_itemPool.Count * shrinkRatio);
                                while (itemPoolToShrink > 0)
                                {
                                    _itemPool.Dequeue();
                                    itemPoolToShrink--;
                                }
                                _blockCount -= blocksToShrink;
                                _freeSize -= blocksToShrink * _blockSize;
                                _totalSize -= blocksToShrink * _blockSize;
                                _listOfBlocks.RemoveRange(_listOfBlocks.Count - blocksToShrink, blocksToShrink);
                                _shrinkCount++;
                            }
                        }
                        return 0;
                    }
                    else
                    {
                        return -1;
                    }
                }
                else
                {
                    return 0;
                }
            }
            finally
            {
                if (lockAcquired)
                {
                    Monitor.Exit(this);
                }
            }
        }

        public int GrowAlloc(PoolAllocItem allocItem, int growSize)
        {
            bool lockAcquired = false;

            try
            {
                lockAcquired = Monitor.TryEnter(this);

                if (lockAcquired)
                {
                    if (_allocIndex._blockIndex == allocItem.EndIndex._blockIndex &&
                        _allocIndex._blockOffset == allocItem.EndIndex._blockOffset)
                    {
                        while (_freeSize < growSize)
                        {
                            GrowPool();
                        }

                        int absoluteAllocOffset = _allocIndex._blockIndex * _blockSize + _allocIndex._blockOffset;
                        int newAbsoluteAllocOffset = (absoluteAllocOffset + growSize) % _totalSize;
                        int index = newAbsoluteAllocOffset / _blockSize;
                        int offset = newAbsoluteAllocOffset % _blockSize;

                        _allocIndex._blockIndex = index;
                        _allocIndex._blockOffset = offset;
                        allocItem.EndIndex._blockIndex = index;
                        allocItem.EndIndex._blockOffset = offset;
                        _freeSize -= growSize;

                        Debug.Assert(_freeSize >= 0 && _freeSize <= _totalSize);

                        _accumulatedAllocSize += growSize;
                        _allocCount++;
                    }
                    else
                    {
                        throw new System.NotSupportedException("FIFOMemoryPool only supports grow most recent allocation!");
                    }

                    return growSize;
                }
                else
                {
                    return -1;
                }
            }
            finally
            {
                if (lockAcquired)
                {
                    Monitor.Exit(this);
                }
            }
        }

        public int CalculateSize(PoolBlockIndex startIndex, PoolBlockIndex endIndex)
        {
            int absoluteStartOffset = startIndex._blockIndex * _blockSize + startIndex._blockOffset;
            int absoluteEndOffset = endIndex._blockIndex * _blockSize + endIndex._blockOffset;
            if (absoluteEndOffset > absoluteStartOffset)
            {
                return absoluteEndOffset - absoluteStartOffset;
            }
            else
            {
                return absoluteEndOffset + _totalSize - absoluteStartOffset;
            }
        }

        public void IncreaseIndex(PoolBlockIndex curIndex, int offset)
        {
            int absoluteIndex = curIndex._blockIndex * _blockSize + curIndex._blockOffset + offset;
            absoluteIndex = absoluteIndex % _totalSize;
            curIndex._blockIndex = absoluteIndex / _blockSize;
            curIndex._blockOffset = absoluteIndex % _blockSize;
        }

        private PoolBlockIndex _readIndex;

        public int Read(byte[] buffer, int destOffset, PoolBlockIndex blockIndex, int poolItemOffset, int count)
        {
            bool lockAcquired = false;

            try
            {
                lockAcquired = Monitor.TryEnter(this);
                if (lockAcquired)
                {
                    int absoluteReadOffset = (blockIndex._blockIndex * _blockSize + blockIndex._blockOffset + poolItemOffset) % _totalSize;
                    if (null == _readIndex)
                    {
                        _readIndex = new PoolBlockIndex(absoluteReadOffset / _blockSize, absoluteReadOffset % _blockSize);
                    }
                    else
                    {
                        _readIndex._blockIndex = absoluteReadOffset / _blockSize;
                        _readIndex._blockOffset = absoluteReadOffset % _blockSize;
                    }

                    int bytesLeft = count;
                    int destIndex = destOffset;

                    while (bytesLeft > 0)
                    {
                        int bytesToCopy = _blockSize - _readIndex._blockOffset;
                        bytesToCopy = Math.Min(bytesToCopy, bytesLeft);
                        Array.Copy(_listOfBlocks[_readIndex._blockIndex]._bytes, _readIndex._blockOffset, buffer, destIndex, bytesToCopy);
                        destIndex += bytesToCopy;
                        bytesLeft -= bytesToCopy;
                        _readIndex._blockIndex++;
                        if (_readIndex._blockIndex >= _blockCount)
                        {
                            _readIndex._blockIndex = 0;
                        }
                        _readIndex._blockOffset = 0;
                    }

                    return count;
                }
                else
                {
                    return -1;
                }
            }
            finally
            {
                if (lockAcquired)
                {
                    Monitor.Exit(this);
                }
            }
        }

        private PoolBlockIndex _writeIndex;

        public int Write(byte[] buffer, int srcOffset, PoolBlockIndex blockIndex, int poolItemOffset, int count)
        {
            bool lockAcquired = false;

            try
            {
                lockAcquired = Monitor.TryEnter(this);

                if (lockAcquired)
                {
                    int absoluteWriteOffset = (blockIndex._blockIndex * _blockSize + blockIndex._blockOffset + poolItemOffset) % _totalSize;
                    if (null == _writeIndex)
                    {
                        _writeIndex = new PoolBlockIndex(absoluteWriteOffset / _blockSize, absoluteWriteOffset % _blockSize);
                    }
                    else
                    {
                        _writeIndex._blockIndex = absoluteWriteOffset / _blockSize;
                        _writeIndex._blockOffset = absoluteWriteOffset % _blockSize;
                    }
                    int bytesLeft = count;
                    int srcIndex = srcOffset;

                    while (bytesLeft > 0)
                    {
                        int bytesToCopy = _blockSize - _writeIndex._blockOffset;
                        bytesToCopy = Math.Min(bytesToCopy, bytesLeft);
                        Array.Copy(buffer, srcIndex, _listOfBlocks[_writeIndex._blockIndex]._bytes, _writeIndex._blockOffset, bytesToCopy);
                        srcIndex += bytesToCopy;
                        bytesLeft -= bytesToCopy;
                        _writeIndex._blockIndex++;
                        if (_writeIndex._blockIndex >= _blockCount)
                        {
                            _writeIndex._blockIndex = 0;
                        }
                        _writeIndex._blockOffset = 0;
                    }

                    return count;
                }
                else
                {
                    return -1;
                }
            }
            finally
            {
                if (lockAcquired)
                {
                    Monitor.Exit(this);
                }
            }
        }
    }
}

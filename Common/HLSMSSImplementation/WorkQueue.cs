//**************************************************************************
//
// Athens fragmented MP4 media streams Source
//
// File:  WorkQueue.cs
//
// Copyright © Microsoft Corporation. All rights reserved.
//
//**************************************************************************
namespace Silverlight.Samples.HttpLiveStreaming
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// This class defines a queue of events to be executed. It is only used in the main media stream
    /// source, hence we make it a private class to it. We can pull it out later if this
    /// needs to be used by other classes.
    /// </summary>
    internal class WorkQueue : IDisposable
    {
        /// <summary>
        /// Our queue of work items
        /// </summary>
        private Queue<WorkQueueElement> m_queue;

        /// <summary>
        /// An event which fires whenever the queue has items in it
        /// (or rather, when the queue goes from empty to non-empty)
        /// </summary>
        private ManualResetEvent m_queueHasItemsEvent;

        /// <summary>
        /// Track whether Dispose has been called
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the WorkQueue class
        /// </summary>
        public WorkQueue()
        {
            m_queueHasItemsEvent = new ManualResetEvent(false);
            m_queue = new Queue<WorkQueueElement>();
        }

        /// <summary>
        /// Enqueue a new work item
        /// </summary>
        /// <param name="elem">the item to add</param>
        public void Enqueue(WorkQueueElement elem)
        {
            lock (m_queue)
            {
                m_queue.Enqueue(elem);
                if (1 == m_queue.Count)
                {
                    m_queueHasItemsEvent.Set();
                }
            }
        }

        /// <summary>
        /// Remove and return an item from the queue
        /// </summary>
        /// <returns>next item from the queue</returns>
        public WorkQueueElement Dequeue()
        {
            WorkQueueElement elem = null;
            lock (m_queue)
            {
                if (0 != m_queue.Count)
                {
                    elem = m_queue.Dequeue();
                    if (0 == m_queue.Count)
                    {
                        m_queueHasItemsEvent.Reset();
                    }
                }
            }

            return elem;
        }

        /// <summary>
        /// Clear the queue and add 1 item in the same operation. This is useful
        /// for operation that take precedence over all others (like closing and errors)
        /// </summary>
        /// <param name="elem">New item to add</param>
        public void ClearAndEnqueue(WorkQueueElement elem)
        {
            lock (m_queue)
            {
                m_queue.Clear();
                m_queue.Enqueue(elem);
                m_queueHasItemsEvent.Set();
            }
        }

        /// <summary>
        /// Clear a type of command in work queue
        /// </summary>
        public void Clear(WorkQueueElement.Command commandType)
        {
            lock (m_queue)
            {
                Queue<WorkQueueElement> tempQueue = new Queue<WorkQueueElement>();

                while (m_queue.Count > 0)
                {
                    WorkQueueElement elem = m_queue.Dequeue();
                    if (elem.CommandToPerform == commandType)
                    {
                        HLSTrace.WriteLine("Clear command {0}", elem.CommandToPerform.ToString());
                    }
                    else
                    {
                        tempQueue.Enqueue(elem);
                    }
                }

                while (tempQueue.Count > 0)
                {
                    m_queue.Enqueue(tempQueue.Dequeue());
                }

                if (m_queue.Count == 0)
                {
                    m_queueHasItemsEvent.Reset();
                }
            }
        }

        /// <summary>
        /// Wait until the queue has an item in it
        /// </summary>
        public bool WaitForWorkItem(TimeSpan timeout)
        {
            return m_queueHasItemsEvent.WaitOne(timeout);
        }

        #region IDisposable Members
        /// <summary>
        /// Implements IDisposable.Dispose()
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Implements Dispose logic
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (m_queueHasItemsEvent != null)
                        m_queueHasItemsEvent.Close();
                }
                _disposed = true;
            }
        }
        #endregion
    }
}

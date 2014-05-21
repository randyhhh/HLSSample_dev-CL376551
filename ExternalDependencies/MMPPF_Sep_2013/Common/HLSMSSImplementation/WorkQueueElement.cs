//**************************************************************************
//
// Athens fragmented MP4 media streams Source
//
// File:  WorkQueueElement.cs
//
// Copyright © Microsoft Corporation. All rights reserved.
//
//**************************************************************************
namespace Silverlight.Samples.HttpLiveStreaming
{

    /// <summary>
    /// An individual element stored in our work queue. Describes the type of work
    /// to perform.
    /// </summary>
    internal class WorkQueueElement
    {
        /// <summary>
        /// The command we are performing
        /// </summary>
        private Command m_commandToPerform;

        /// <summary>
        /// A command specific parameter
        /// </summary>
        private object m_commandParameter;

        /// <summary>
        /// Initializes a new instance of the WorkQueueElement class
        /// </summary>
        /// <param name="cmd">the command to perform</param>
        /// <param name="prm">parameter for the command</param>
        public WorkQueueElement(Command cmd, object prm)
        {
            m_commandToPerform = cmd;
            m_commandParameter = prm;
        }

        /// <summary>
        /// The type of work to perform
        /// </summary>
        public enum Command
        {
            /// <summary>
            /// Open a new manifest
            /// </summary>
            Open,

            /// <summary>
            /// Closes the media stream source
            /// </summary>
            Close,

            /// <summary>
            /// Report diagnostics back to the media element
            /// </summary>
            Diagnostics,

            /// <summary>
            /// Perform a seek
            /// </summary>
            Seek,

            /// <summary>
            /// Switch to a different media stream
            /// </summary>
            SwitchMedia,

            /// <summary>
            /// Handle media file response
            /// </summary>
            NextStream,
        }

        /// <summary>
        /// Gets the command we are performing
        /// </summary>
        public Command CommandToPerform
        {
            get
            {
                return m_commandToPerform;
            }
        }

        /// <summary>
        /// Gets a command specific parameter
        /// </summary>
        public object CommandParameter
        {
            get
            {
                return m_commandParameter;
            }
        }
    }
}

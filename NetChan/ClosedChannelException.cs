using System;
using System.Runtime.Serialization;

namespace NetChan {

    [Serializable]
    public class ClosedChannelException : Exception {
        public ClosedChannelException(string message)
            : base(message) {
        }

        protected ClosedChannelException(SerializationInfo info, StreamingContext context)
            : base(info, context) {
        }
    }
}

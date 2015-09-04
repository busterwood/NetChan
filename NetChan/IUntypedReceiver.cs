using System;
using System.Collections.Generic;
using System.Text;

namespace NetChan {
    public interface IUntypedReceiver {
        /// <summary>
        /// Returns TRUE if{Channel has aleady been read into{waiter
        /// </summary>
        RecvStatus RecvSelect(Sync sync, out IWaiter h);
        void Release(IWaiter h);
    }

    public enum RecvStatus {
        Closed,
        Read,
        Waiting,
    }
}

// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;
using System.Collections.Generic;
using System.Text;

namespace NetChan {
    public interface IUntypedReceiver {
        /// <summary>
        /// Try to receive and value and write it <paramref name="w"/>. If it can be recieved straight away then it returns TRUE, 
        /// else registers a waiting reciever and returns FALSE.
        /// </summary>
        bool RecvSelect(IWaiter w);

        /// <summary>Try to receive without blocking</summary>
        bool TryRecvSelect(IWaiter w);

        /// <summary>Gets a waiter for use in RecvSelect</summary>
        IWaiter GetWaiter(Sync sync);

        /// <summary>Releases the waiter after use</summary>
        void ReleaseWaiter(IWaiter w);
    }
}

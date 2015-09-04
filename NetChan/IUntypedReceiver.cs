using System;
using System.Collections.Generic;
using System.Text;

namespace NetChan {
    public interface IUntypedReceiver {
        /// <summary>
        /// Try to receive and value and write it to h, if it can be recieved straight away then it returns <see cref="RecvStatus.Read"/>.
        /// If the channel is closed it returns <see cref="RecvStatus.Closed"/>.
        /// If it has to wait it registers a reciever and returns <see cref="RecvStatus.Waiting"/>.
        /// </summary>
        /// <param name="sync">a object used to ensure only one channel writes a value as part of the select</param>
        RecvStatus RecvSelect(Sync sync, out IWaiter h);

        /// <summary>Try to receive without blocking</summary>
        Maybe<object> TryRecvSelect();

        void Release(IWaiter h);
    }

    public enum RecvStatus {
        Closed,
        Read,
        Waiting,
    }
}

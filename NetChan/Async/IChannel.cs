// Copyright the Netchan authors, see LICENSE.txt for permitted use

namespace NetChan.Async {
    public interface IChannel {
        /// <summary>
        /// Try to receive a value and write it <paramref name="w"/>. If it can be recieved straight away then it returns TRUE, 
        /// else registers a waiting reciever and returns FALSE.
        /// </summary>
        bool Recv(IWaiter w);

        /// <summary>Try to receive without blocking</summary>
        bool TryRecv(IWaiter w);

        /// <summary>
        /// Try to send the value stored in <paramref name="w"/>. If it can be send immediately then it returns TRUE, 
        /// else registers <paramref name="w"/> and returns FALSE.
        /// </summary>
        bool Send(IWaiter w);

        /// <summary>Try to send the value in <paramref name="w"/> without blocking</summary>
        bool TrySend(IWaiter w);

        /// <summary>Gets a waiter for use in RecvSelect</summary>
        IWaiter GetWaiter();

        void RemoveReceiver(IWaiter waiter);
    }
}

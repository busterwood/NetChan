using System;
using System.Collections.Generic;

namespace NetChan {
    /// <summary>Common interface for <see cref="Channel{T}"/> and <see cref="QueuedChannel{T}"/> which allow threads to communicate over Go-like Channels</summary>
    /// <remarks>Enumerating a Channel does blocking reads until the Channel is closed</remarks>
    public interface IChannel<T> : IEnumerable<T>, IDisposable, IUntypedReceiver {
        void Close();

        /// <summary>Send a value, blocks if needed</summary>
        void Send(T v);

        /// <summary>Try to send a value, return TRUE if sent, FALSE if would have to block</summary>
        bool TrySend(T v);

        /// <summary>Recieve a value, blocks if needed</summary>
        T Recv();

        /// <summary>
        /// Recieve a value, returns <see cref="Maybe{T}.Some(T)"/> if recieved, otherwise returns <see cref="Maybe{T}.None()"/> if would have to block
        /// </summary>
        Maybe<T> TryRecv();
    }
}

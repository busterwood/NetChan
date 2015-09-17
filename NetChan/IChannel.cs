// Copyright the Netchan authors, see LICENSE.txt for permitted use
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
        /// <remarks>returns <see cref="Maybe{T}.None()"/> if the channel is closed</remarks>
        Maybe<T> Recv();

        /// <summary>
        /// Recieve a value, returns <see cref="Maybe{T}.Some(T)"/> if a value is availble
        /// </summary>
        /// <remarks>returns <see cref="Maybe{T}.None()"/> if would have to block or if the channel is closed</remarks>
        Maybe<T> TryRecv();
    }
}

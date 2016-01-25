// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace NetChan.Async {

    /// <summary>An CSP-like channel for communicating between threads.  </summary>﻿
    /// <remarks>Supports 1-to-N, N-to-N and N-to-1 semantics</remarks>
    public sealed class Channel<T> : IChannel
    {
        private readonly WaiterQ<T> receivers = new WaiterQ<T>();
        private readonly WaiterQ<T> senders = new WaiterQ<T>();
        private readonly CircularQueue<T> items;
        private bool closed;

        public bool Closed => closed;

        public int Capacity => items.Capacity;

        /// <summary>
        /// Create an unbuffered channel for communicating between threads, with CSP-like semantics
        /// </summary>
        public Channel() : this(0) {
        }

        /// <summary>
        /// Create a buffered channel for communicating between threads, CSP-like semantics but with a fixed size queue
        /// </summary>
        public Channel(int capacity) {
            if (capacity < 0) {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "must be zero or more");
            }
            items = new CircularQueue<T>(capacity);
        }

        /// <summary>
        /// Marks a Channel as closed, preventing further Send operations often
        /// After calling Close, and after any previously sent values have been received, 
        /// receive operations will return the default value for Channel's type without blocking
        /// </summary>
        public void Close() {
            lock (items) {
                if (closed) {
                    return;
                }
                closed = true;
                if (senders.Empty && items.Empty && !receivers.Empty) {
                    // wait up the waiting receivers
                    int count = 0;
                    for (var r = receivers.First; r != null; r = r.Next) {
                        lock (r.CompletionSource) {
                            r.TrySetCompletionSource();
                        }
                        count++;
                    }
                }
            }
        }

        /// <summary>Send a value, adds it to the item queue or blocks until the queue is no longer full</summary>
        public Task Send(T v) {
            Waiter<T> s;
            lock (items) {
                if (closed) {
                    throw new ClosedChannelException("You cannot send on a closed Channel");
                }
                if (items.Empty) {
                    while (!receivers.Empty) { 
                        Waiter<T> r = receivers.Dequeue();
                        lock (r.CompletionSource) {
                            if (r.CompletionSource.Task.IsCompleted) {
                                continue;
                            }
                            r.Value = Maybe<T>.Some(v);
                            r.TrySetCompletionSource(); // must work due to locking
                            return Complete.Instance;
                        }
                    }
                }
                if (!items.Full) {
                    items.Enqueue(v);
                    return Complete.Instance;
                }
                // at capacity, queue our waiter until some capacity is freed up by a recv
                s = new Waiter<T> {Value = Maybe<T>.Some(v)};
                senders.Enqueue(s);
            }
            // wait for the receiver to wake us up
            return s.CompletionSource.Task;
        }

        public bool TrySend(T v) {
            lock (items) {
                if (closed) {
                    return false;
                }
                if (items.Empty) {
                    while (!receivers.Empty) {
                        Waiter<T> r = receivers.Dequeue();
                        lock (r.CompletionSource) {
                            if (r.CompletionSource.Task.IsCompleted) {
                                continue;
                            }
                            r.Value = Maybe<T>.Some(v);
                            r.TrySetCompletionSource(); // must work
                            return true;
                        }
                    }
                }
                if (!items.Full) {
                    items.Enqueue(v);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Returns an item, blocking if not ready</summary>
        /// <remarks>returns <see cref="Maybe{T}.None()"/> without blocking if the channel is closed</remarks>
        public Task<Maybe<T>> Recv() {
            Waiter<T> r;
            lock (items) {
                if (!items.Empty) {
                    var value = items.Dequeue();
                    MoveSendQToitems();
                    return Task.FromResult(Maybe<T>.Some(value));
                }
                while (!senders.Empty) {
                    Waiter<T> s = senders.Dequeue();
                    lock (s.CompletionSource) {
                        if (s.TrySetCompletionSource()) {
                            var mv = s.Value;
                            return Task.FromResult(mv);
                        }
                    }
                }
                if (closed) {
                    return Task.FromResult(Maybe<T>.None()); // return no value for closed channel
                }
                r = new Waiter<T>();
                receivers.Enqueue(r);
            }
            return RecvInternal(r);
        }

        private async Task<Maybe<T>> RecvInternal(Waiter<T> r)
        {
            // wait for a sender to signal it has sent
            await r.CompletionSource.Task;
            return r.Value;
        }

        private void MoveSendQToitems() {
            while (!senders.Empty) {
                Waiter<T> s = senders.Dequeue();
                lock (s.CompletionSource)
                {
                    if (s.TrySetCompletionSource())
                    {
                        items.Enqueue(s.Value.Value);
                        //Debug.Print("Thread {0}, {1} MoveSendQToitems, waking sender", Thread.CurrentThread.ManagedThreadId, GetType());
                        return;
                    }
                }
            }
        }

        /// <remarks>returns <see cref="Maybe{T}.None()"/> if would have to block or if the channel is closed</remarks>
        public Maybe<T> TryRecv() {
            lock (items) {
                if (!items.Empty) {
                    var v = items.Dequeue();
                    MoveSendQToitems();
                    return Maybe<T>.Some(v);
                }
                while (!senders.Empty) {
                    Waiter<T> s = senders.Dequeue();
                    lock (s.CompletionSource) {
                        if (s.TrySetCompletionSource()) {
                            //Debug.Print("Thread {0}, {1} TryRecv, waking sender, item {2}", Thread.CurrentThread.ManagedThreadId, GetType(), s.Value);
                            var mv = s.Value;
                            return mv;
                        }
                    }
                }
                return Maybe<T>.None();
            }
        }

        public void Dispose() {
            Close();
        }

        /// <summary>Gets a waiter for use in RecvSelect</summary>
        IWaiter IChannel.GetWaiter() {
            return new Waiter<T>();
        }

        /// <summary>Try to receive and value and write it <paramref name="w"/>.</summary>
        /// <returns>If the operation can complete straight away then it returns TRUE, else registers a waiting reciever and returns FALSE.</returns>
        bool IChannel.Recv(IWaiter w) {
            var r = (Waiter<T>)w;
            lock (items) {
                if (!items.Empty) {
                    lock (r.CompletionSource) {
                        if (r.CompletionSource.Task.IsCompleted) {
                            return false;
                        }
                        r.Value = Maybe<T>.Some(items.Dequeue());
                        r.TrySetCompletionSource(); // this MUST work as locks have been taken
                    }
                    //Debug.Print("Thread {0}, {1} IChannel.Recv, removed {2} from items", Thread.CurrentThread.ManagedThreadId, GetType(), r.Value);
                    MoveSendQToitems();
                    return true;                
                }

                while (!senders.Empty) {
                    lock (r.CompletionSource) {
                        if (r.CompletionSource.Task.IsCompleted) {
                            return false;
                        }
                        Waiter<T> s = senders.Dequeue();
                        lock (s.CompletionSource) {
                            if (!s.CompletionSource.Task.IsCompleted) {
                                r.Value = s.Value;
                                r.TrySetCompletionSource(); // this MUST work as locks have been taken
                                s.TrySetCompletionSource(); // this MUST work as locks have been taken
                                return true;
                            }
                        }
                    }
                }
                if (closed) {
                    // closed channels return no value and dont block
                    return true;
                }
                receivers.Enqueue(r);
            }
            return false;
        }

        /// <summary>Try to receive without blocking</summary>
        bool IChannel.TryRecv(IWaiter w) {
            var r = (Waiter<T>)w;
            lock (items) {
                if (!items.Empty) {
                    lock (r.CompletionSource)
                    {
                        if (r.CompletionSource.Task.IsCompleted) {
                            return false;
                        }
                        r.Value = Maybe<T>.Some(items.Dequeue());
                        //Debug.Print("Thread {0}, {1} IChannel.TryRecv, removed {2} from items", Thread.CurrentThread.ManagedThreadId, GetType(), r.Value);
                        MoveSendQToitems();
                        return true;
                    }
                }
                while(!senders.Empty) {
                    lock (r.CompletionSource)
                    {
                        if (r.CompletionSource.Task.IsCompleted) {
                            return false;
                        }
                        Waiter<T> s = senders.Dequeue();
                        lock (s.CompletionSource) {
                            if (!s.CompletionSource.Task.IsCompleted) {
                                r.Value = s.Value;
                                r.TrySetCompletionSource(); // this MUST work as locks have been taken
                                s.TrySetCompletionSource(); // this MUST work as locks have been taken
                                return true;
                            }
                        }
                    }
                }
                if (closed) {
                    // closed channels return no value and dont block
                    return true;
                }
                //Debug.Print("Thread {0}, {1} IChannel.TryRecv, items and senders are empty", Thread.CurrentThread.ManagedThreadId, GetType());
                return false;
            }
        }

        /// <summary>Try to send the value in <paramref name="w"/>.</summary>
        /// <returns>If the operation can complete straight away then it returns TRUE, else registers a waiting sender and returns FALSE.</returns>
        bool IChannel.Send(IWaiter w) {
            var s = (Waiter<T>)w;
            Debug.Assert(s.Value.IsSome);
            lock (items) {
                if (closed) {
                    throw new ClosedChannelException("You cannot send on a closed Channel");
                }
                if (items.Empty) {
                    while (!receivers.Empty) {
                        Waiter<T> r = receivers.Dequeue();
                        lock (r.CompletionSource) {
                            if (r.CompletionSource.Task.IsCompleted) {
                                continue;
                            }
                            lock (s.CompletionSource) {
                                if (s.CompletionSource.Task.IsCompleted) {
                                    return false;
                                }
                                r.Value = s.Value;
                                r.TrySetCompletionSource(); // this MUST work as locks have been taken
                                s.TrySetCompletionSource(); // this MUST work as locks have been taken
                                return true;
                            }
                        }
                    }
                }
                if (!items.Full) {
                     // Debug.Print("Thread {0}, {1} IChannel.Send({2}), spare capacity, adding to items", Thread.CurrentThread.ManagedThreadId, GetType(), s.Value.Value);
                    items.Enqueue(s.Value.Value);
                    return true;
                }
                // at capacity, queue our waiter until some capacity is freed up by a recv
                senders.Enqueue(s);
                return false;
            }
        }

        /// <summary>Try to send without blocking</summary>
        bool IChannel.TrySend(IWaiter w)
        {
            var s = (Waiter<T>) w;
            lock (items) {
                if (closed) {
                    return false;
                }
                if (items.Empty) {
                    while (!receivers.Empty) {
                        Waiter<T> r = receivers.Dequeue();
                        lock (r.CompletionSource) {
                            if (r.CompletionSource.Task.IsCompleted) {
                                continue;
                            }
                            lock (s.CompletionSource) {
                                if (s.CompletionSource.Task.IsCompleted) {
                                    return false;
                                }
                                r.Value = s.Value;
                                r.TrySetCompletionSource(); // this MUST work as locks have been taken
                                s.TrySetCompletionSource(); // this MUST work as locks have been taken
                                return true;
                            }
                        }
                    }
                }
                if (!items.Full) {
                    //Debug.Print("Thread {0}, {1} IChannel.TrySend({2}), add item to queue", Thread.CurrentThread.ManagedThreadId, GetType(), s.Value.Value);
                    items.Enqueue(s.Value.Value);
                    return true;
                }
            }
            return false;
        }

        void IChannel.RemoveReceiver(IWaiter w) {
            var r = (Waiter<T>)w;
            lock (items) {
                receivers.Remove(r);
            }
        }                      
    }

    internal static class Complete
    {
        public static readonly Task Instance = Task.FromResult(true);
    }

}

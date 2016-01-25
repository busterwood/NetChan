﻿// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NetChan.Async {

    /// <summary>An CSP-like channel for communicating between threads.  </summary>﻿
    /// <remarks>Supports 1-to-N, N-to-N and N-to-1 semantics</remarks>
    public sealed class Channel<T> : IChannel
    {
        private readonly object sync = new object();
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
            lock (sync) {
                if (closed) {
                    return;
                }
                closed = true;
                if (senders.Empty && items.Empty && !receivers.Empty) {
                    // wait up the waiting receivers
                    int count = 0;
                    for (var r = receivers.First; r != null; r = r.Next) {
                        r.Sync.TrySet(-1);
                        r.Wakeup();
                        count++;
                    }
                    Debug.Print("Thread {0}, {1} Close woke {2} waiting receivers", Thread.CurrentThread.ManagedThreadId, GetType(), count);
                }
                Debug.Print("Thread {0}, {1} is now Closed", Thread.CurrentThread.ManagedThreadId, GetType());
            }
        }

        /// <summary>Send a value, adds it to the item queue or blocks until the queue is no longer full</summary>
        public async Task Send(T v) {
            Waiter<T> s;
            lock (sync) {
                if (closed) {
                    throw new ClosedChannelException("You cannot send on a closed Channel");
                }
                if (items.Empty) {
                    Waiter<T> wr = receivers.Dequeue();
                    if (wr != null) {
                        wr.Value = Maybe<T>.Some(v);
                        //Debug.Print("Thread {0}, {1} Send({2}), SetItem succeeded", Thread.CurrentThread.ManagedThreadId, GetType(), wr.Value);
                        wr.Wakeup();
                        return;
                    }
                }
                if (!items.Full) {
                    //Debug.Print("Thread {0}, {1} Send({2}), spare capacity, adding to items", Thread.CurrentThread.ManagedThreadId, GetType(), v);
                    items.Enqueue(v);
                    return;
                }
                // at capacity, queue our waiter until some capacity is freed up by a recv
                //s = WaiterPool<T>.Get(v);
                s = new Waiter<T> {Value = Maybe<T>.Some(v)};
                senders.Enqueue(s);
            }
            // wait for the receiver to wake us up
            //Debug.Print("Thread {0}, {1} Send({2}), about to wait", Thread.CurrentThread.ManagedThreadId, GetType(), v);
            await s.WaitOne();
            //Debug.Print("Thread {0}, {1} Send({2}), woke up after waiting ", Thread.CurrentThread.ManagedThreadId, GetType(), v);
            //WaiterPool<T>.Put(s);
        }

        public bool TrySend(T v) {
            lock (sync) {
                if (closed) {
                    return false;
                }
                if (items.Empty) {
                    Waiter<T> wr = receivers.Dequeue();
                    if (wr != null) {
                        wr.Value = Maybe<T>.Some(v);
                        Debug.Print("Thread {0}, {1} TrySend({2}), waking up receiver", Thread.CurrentThread.ManagedThreadId, GetType(), wr.Value);
                        wr.Wakeup();
                        return true;
                    }
                }
                if (!items.Full) {
                    Debug.Print("Thread {0}, {1} TrySend({2}), add item to queue", Thread.CurrentThread.ManagedThreadId, GetType(), v);
                    items.Enqueue(v);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Returns an item, blocking if not ready</summary>
        /// <remarks>returns <see cref="Maybe{T}.None()"/> without blocking if the channel is closed</remarks>
        public async Task<Maybe<T>> Recv() {
            Waiter<T> r;
            lock (sync) {
                if (!items.Empty) {
                    var value = items.Dequeue();
                    Debug.Print("Thread {0}, {1} Recv, removed item from items", Thread.CurrentThread.ManagedThreadId, GetType());
                    MoveSendQToitems();
                    return Maybe<T>.Some(value);
                }
                Waiter<T> s = senders.Dequeue();
                if (s != null) {
                    var mv = s.Value;
                    Debug.Print("Thread {0}, {1} Recv, waking sender of value {2}", Thread.CurrentThread.ManagedThreadId, GetType(), mv);
                    s.Wakeup();
                    return mv;
                }
                if (closed) {
                    Debug.Print("Thread {0}, {1} Recv, Channel is closed", Thread.CurrentThread.ManagedThreadId, GetType());
                    return Maybe<T>.None();
                }
                //r = WaiterPool<T>.Get();
                r = new Waiter<T>();
                receivers.Enqueue(r);
            }
            // wait for a sender to signal it has sent
            Debug.Print("Thread {0}, {1} Recv, waiting", Thread.CurrentThread.ManagedThreadId, GetType());
            await r.WaitOne();
            Debug.Print("Thread {0}, {1} Recv, woke up", Thread.CurrentThread.ManagedThreadId, GetType());
            var v = r.Value;
            //WaiterPool<T>.Put(r);
            return v;
        }

        private void MoveSendQToitems() {
            if (!senders.Empty) {
                Waiter<T> s = senders.Dequeue();
                if (s != null) {
                    items.Enqueue(s.Value.Value);
                    Debug.Print("Thread {0}, {1} MoveSendQToitems, waking sender", Thread.CurrentThread.ManagedThreadId, GetType());
                    s.Wakeup();
                }
            }
        }

        /// <remarks>returns <see cref="Maybe{T}.None()"/> if would have to block or if the channel is closed</remarks>
        public Maybe<T> TryRecv() {
            lock (sync) {
                if (!items.Empty) {
                    var v = items.Dequeue();
                    MoveSendQToitems();
                    return Maybe<T>.Some(v);
                }
                Waiter<T> s = senders.Dequeue();
                if (s != null) {
                    Debug.Print("Thread {0}, {1} TryRecv, waking sender, item {2}", Thread.CurrentThread.ManagedThreadId, GetType(), s.Value);
                    var mv = s.Value;
                    s.Wakeup();
                    return mv;
                }
                return Maybe<T>.None();
            }
        }


        //public IEnumerator<T> GetEnumerator() {
        //    for (;;) {
        //        var maybe = Recv();
        //        if (maybe.IsNone) {
        //            yield break; // Channel has been closed
        //        }
        //        yield return maybe.Value;
        //    }
        //}

        //IEnumerator IEnumerable.GetEnumerator() {
        //    return GetEnumerator();
        //}

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
            lock (sync) {
                if (!items.Empty && r.TrySet()) {
                    r.Value = Maybe<T>.Some(items.Dequeue());
                    //Debug.Print("Thread {0}, {1} IChannel.Recv, removed {2} from items", Thread.CurrentThread.ManagedThreadId, GetType(), r.Value);
                    MoveSendQToitems();
                    return true;                
                }
                if (senders.First != null && r.TrySet()) {
                    Waiter<T> s = senders.Dequeue();
                    if (s != null) {
                        //Debug.Print("Thread {0}, {1} IChannel.Recv, waking sender", Thread.CurrentThread.ManagedThreadId, GetType());
                        r.Value = s.Value;
                        s.Wakeup();
                        return true;
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
            lock (sync) {
                if (!items.Empty) {
                    r.Value = Maybe<T>.Some(items.Dequeue());
                    Debug.Print("Thread {0}, {1} IChannel.TryRecv, removed {2} from items", Thread.CurrentThread.ManagedThreadId, GetType(), r.Value);
                    MoveSendQToitems();
                    return true;                    
                }
                if (!senders.Empty) {
                    Waiter<T> s = senders.Dequeue();
                    if (s != null) {
                        Debug.Print("Thread {0}, {1} IChannel.TryRecv, waking sender", Thread.CurrentThread.ManagedThreadId, GetType());
                        r.Value = s.Value;
                        s.Wakeup();
                        return true;
                    }
                }
                if (closed) {
                    // closed channels return no value and dont block
                    return true;
                }
                Debug.Print("Thread {0}, {1} IChannel.TryRecv, items and senders are empty", Thread.CurrentThread.ManagedThreadId, GetType());
                return false;
            }
        }

        /// <summary>Try to send the value in <paramref name="w"/>.</summary>
        /// <returns>If the operation can complete straight away then it returns TRUE, else registers a waiting sender and returns FALSE.</returns>
        bool IChannel.Send(IWaiter w) {
            var s = (Waiter<T>)w;
            Debug.Assert(s.Value.IsSome);
            lock (sync) {
                if (closed) {
                    throw new ClosedChannelException("You cannot send on a closed Channel");
                }
                if (items.Empty) {
                    Waiter<T> wr = receivers.Dequeue();
                    if (wr != null) {
                        wr.Value = s.Value;
                        Debug.Print("Thread {0}, {1} IChannel.Send({2}), SetItem succeeded", Thread.CurrentThread.ManagedThreadId, GetType(), wr.Value);
                        wr.Wakeup();
                        return true;
                    }
                }
                if (!items.Full) {
                    Debug.Print("Thread {0}, {1} IChannel.Send({2}), spare capacity, adding to items", Thread.CurrentThread.ManagedThreadId, GetType(), s.Value.Value);
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
            lock (sync) {
                if (closed) {
                    return false;
                }
                if (items.Empty) {
                    Waiter<T> wr = receivers.Dequeue();
                    if (wr != null) {
                        wr.Value = s.Value;
                        Debug.Print("Thread {0}, {1} IChannel.TrySend({2}), waking up receiver", Thread.CurrentThread.ManagedThreadId, GetType(), wr.Value);
                        wr.Wakeup();
                        return true;
                    }
                }
                if (!items.Full) {
                    Debug.Print("Thread {0}, {1} IChannel.TrySend({2}), add item to queue", Thread.CurrentThread.ManagedThreadId, GetType(), s.Value.Value);
                    items.Enqueue(s.Value.Value);
                    return true;
                }
            }
            return false;
        }

        void IChannel.RemoveReceiver(IWaiter w) {
            var r = (Waiter<T>)w;
            lock (sync) {
                receivers.Remove(r);
            }
        }
      
                
    }

}
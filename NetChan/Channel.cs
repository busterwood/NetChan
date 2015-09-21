// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace NetChan {
﻿
    public class Channel<T> : IUntypedReceiver, IEnumerable<T> {
        private readonly object sync = new object();
        private readonly WaiterQ<T> recq = new WaiterQ<T>();
        private readonly WaiterQ<T> sendq = new WaiterQ<T>();
        private readonly CircularQueue<T> itemq;
        private bool closed;

        public int Capacity {
            get { return itemq.Capacity; }
        }

        /// <summary>
        /// Create an unbuffered channel for communicating between threads, with CSP-like semantics
        /// </summary>
        public Channel() {
        }

        /// <summary>
        /// Create a buffered channel for communicating between threads, CSP-like semantics but with a fixed size queue
        /// </summary>
        public Channel(int capacity) {
            if (capacity < 0) {
                throw new ArgumentOutOfRangeException("capacity", capacity, "must be zero or more");
            }
            itemq = new CircularQueue<T>(capacity);
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
                if (sendq.Empty && (itemq == null || itemq.Empty) && !recq.Empty) {
                    // wait up the waiting recievers
                    int count = 0;
                    for (var r = recq.First; r != null; r = r.Next) {
                        if (r.Sync != null) {
                            Interlocked.Exchange(ref r.Sync.Set, 1);
                        }
                        r.Wakeup();
                        count++;
                    }
                    Debug.Print("Thread {0}, {1} Close woke {2} waiting receivers", Thread.CurrentThread.ManagedThreadId, GetType(), count);
                }
                Debug.Print("Thread {0}, {1} is now Closed", Thread.CurrentThread.ManagedThreadId, GetType());
            }
        }

        /// <summary>Send a value, adds it to the item queue or blocks until the queue is no longer full</summary>
        public void Send(T v) {
            Waiter<T> s;
            lock (sync) {
                if (closed) {
                    throw new ClosedChannelException("You cannot send on a closed Channel");
                }
                if (itemq == null || itemq.Empty) {
                    Waiter<T> wr = recq.Dequeue();
                    if (wr != null) {
                        wr.Item = Maybe<T>.Some(v);
                        Debug.Print("Thread {0}, {1} Send({2}), SetItem suceeded", Thread.CurrentThread.ManagedThreadId, GetType(), wr.Item);
                        wr.Wakeup();
                        return;
                    }
                }
                if (itemq != null && !itemq.Full) {
                    Debug.Print("Thread {0}, {1} Send({2}), spare capacity, adding to itemq", Thread.CurrentThread.ManagedThreadId, GetType(), v);
                    itemq.Enqueue(v);
                    return;
                }
                // at capacity, queue our waiter until some capacity is freed up by a recv
                s = WaiterPool<T>.Get(v);
                sendq.Enqueue(s);
            }
            // wait for the reciever to wake us up
            Debug.Print("Thread {0}, {1} Send({2}), waiting ", Thread.CurrentThread.ManagedThreadId, GetType(), v);
            s.WaitOne();
            Debug.Print("Thread {0}, {1} Send({2}), woke up after waiting ", Thread.CurrentThread.ManagedThreadId, GetType(), v);
            WaiterPool<T>.Put(s);
        }

        public bool TrySend(T v) {
            lock (sync) {
                if (closed || itemq == null || itemq.Full) {
                    return false;
                }
                itemq.Enqueue(v);
                //see if there is a waiting reciever 
                Waiter<T> r = recq.Dequeue();
                if (r != null) {
                    r.Item = Maybe<T>.Some(itemq.Dequeue());
                    r.Wakeup();
                }
            }
            return true;
        }

        /// <summary>Returns an item, blocking if not ready</summary>
        /// <remarks>returns <see cref="Maybe{T}.None()"/> without blocking if the channel is closed</remarks>
        public Maybe<T> Recv() {
            Waiter<T> r;
            lock (sync) {
                if (itemq != null && !itemq.Empty) {
                    var value = itemq.Dequeue();
                    Debug.Print("Thread {0}, {1} Recv, removed item from itemq", Thread.CurrentThread.ManagedThreadId, GetType());
                    MoveSendQToItemQ();
                    return Maybe<T>.Some(value);
                }
                if (!sendq.Empty) {
                    Waiter<T> s = sendq.Dequeue();
                    if (s != null) {
                        Debug.Print("Thread {0}, {1} Recv, waking sender", Thread.CurrentThread.ManagedThreadId, GetType());
                        var t1 = s.Item;
                        s.Wakeup();
                        return t1;
                    }
                }
                if (closed) {
                    Debug.Print("Thread {0}, {1} Recv, Channel is closed", Thread.CurrentThread.ManagedThreadId, GetType());
                    return Maybe<T>.None("closed");
                }
                r = WaiterPool<T>.Get();
                recq.Enqueue(r);
            }
            // wait for a sender to signal it has sent
            Debug.Print("Thread {0}, {1} Recv, waiting", Thread.CurrentThread.ManagedThreadId, GetType());
            r.WaitOne();
            Debug.Print("Thread {0}, {1} Recv, woke up", Thread.CurrentThread.ManagedThreadId, GetType());
            var t = r.Item;
            WaiterPool<T>.Put(r);
            return t;
        }

        private void MoveSendQToItemQ() {
            if (!sendq.Empty) {
                Waiter<T> s = sendq.Dequeue();
                itemq.Enqueue(s.Item.Value);
                Debug.Print("Thread {0}, {1} BlockingRecv, waking sender", Thread.CurrentThread.ManagedThreadId, GetType());
                s.Wakeup();
            }
        }

        /// <remarks>returns <see cref="Maybe{T}.None()"/> if would have to block or if the channel is closed</remarks>
        public Maybe<T> TryRecv() {
            lock (sync) {
                if (itemq != null && !itemq.Empty) {
                    var value = itemq.Dequeue();
                    MoveSendQToItemQ();
                    return Maybe<T>.Some(value);
                }
                if (!sendq.Empty) {
                    Waiter<T> s = sendq.Dequeue();
                    if (s != null) {
                        Debug.Print("Thread {0}, {1} Recv, waking sender", Thread.CurrentThread.ManagedThreadId, GetType());
                        var t1 = s.Item;
                        s.Wakeup();
                        return t1;
                    }
                }
                return Maybe<T>.None(closed ? "closed" : "queue is empty");
            }
        }

        public IEnumerator<T> GetEnumerator() {
            for (; ; ) {
                var maybe = Recv();
                if (maybe.IsNone) {
                    yield break; // Channel has been closed
                }
                yield return maybe.Value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        public void Dispose() {
            Close();
        }

        /// <summary>Gets a waiter for use in RecvSelect</summary>
        IWaiter IUntypedReceiver.GetWaiter(Sync s) {
            var w = WaiterPool<T>.Get();
            w.Sync = s;
            return w;
        }

        /// <summary>
        /// Try to receive and value and write it <paramref name="w"/>. If it can be recieved straight away then it returns TRUE, 
        /// else registers a waiting reciever and returns FALSE.
        /// </summary>
        bool IUntypedReceiver.RecvSelect(IWaiter w) {
            var r = (Waiter<T>)w;
            lock (sync) {
                if (itemq != null && !itemq.Empty && Interlocked.CompareExchange(ref r.Sync.Set, 1, 0) == 0) {
                    r.Item = Maybe<T>.Some(itemq.Dequeue());
                    Debug.Print("Thread {0}, {1} RecvSelect, removed {2} from itemq", Thread.CurrentThread.ManagedThreadId, GetType(), r.Item);
                    MoveSendQToItemQ();
                    return true;                
                }
                if (!sendq.Empty) {
                    Waiter<T> s = sendq.Dequeue();
                    if (s != null) {
                        Debug.Print("Thread {0}, {1} RecvSelect, waking sender", Thread.CurrentThread.ManagedThreadId, GetType());
                        r.Item = s.Item;
                        s.Wakeup();
                        return true;
                    }
                }
                if (closed) {
                    return true;
                }
                recq.Enqueue(r);
            }
            return false;
        }

        /// <summary>Try to receive without blocking</summary>
        bool IUntypedReceiver.TryRecvSelect(IWaiter w) {
            var r = (Waiter<T>)w;
            lock (sync) {
                if (itemq != null && !itemq.Empty) {
                    r.Item = Maybe<T>.Some(itemq.Dequeue());
                    Debug.Print("Thread {0}, {1} TryRecvSelect, removed {2} from itemq", Thread.CurrentThread.ManagedThreadId, GetType(), r.Item);
                    MoveSendQToItemQ();
                    return true;                    
                }
                if (!sendq.Empty) {
                    Waiter<T> s = sendq.Dequeue();
                    if (s != null) {
                        Debug.Print("Thread {0}, {1} RecvSelect, waking sender", Thread.CurrentThread.ManagedThreadId, GetType());
                        r.Item = s.Item;
                        s.Wakeup();
                        return true;
                    }
                }
                Debug.Print("Thread {0}, {1} TryRecvSelect, itemq and sendq are empty", Thread.CurrentThread.ManagedThreadId, GetType());
                return false;
            }
        }

        void IUntypedReceiver.ReleaseWaiter(IWaiter h) {
            WaiterPool<T>.Put((Waiter<T>)h);
        }
        
    }

}

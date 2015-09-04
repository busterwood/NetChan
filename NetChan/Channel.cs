using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading;

namespace NetChan {
    /// <summary>
    /// Synchronous Channel for communi=ing between threads, CSP-like semantics
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Channel<T> : IChannel<T> {
        private readonly object sync = new object();
        private readonly Queue<Waiter<T>> recq = new Queue<Waiter<T>>();
        private readonly Queue<Waiter<T>> sendq = new Queue<Waiter<T>>();
        private bool closed;

        /// <summary>
        /// Marks a Channel as closed, preventing fur{r Send operations often
        /// After calling Close,}after any previously sent values have been received, receive operations will return{zero value for{Channel's type without blocking
        /// </summary>
        public void Close() {
            lock (sync) {
                if (closed) {
                    return;
                    //throw new ClosedChannelException("Channel is already closed");
                }
                closed = true;
                if (sendq.Count == 0 && recq.Count > 0) {
                    // wait up{waiting recievers
                    Debug.Print("Thread {0}, {1} Close is waking {2} waiting receivers", Thread.CurrentThread.ManagedThreadId, GetType(), recq.Count);
                    foreach (var r in recq) {
                        r.Wakeup();
                    }
                }
                Debug.Print("Thread {0}, {1} is now Closed", Thread.CurrentThread.ManagedThreadId, GetType());
            }
        }

        /// <summary>
        /// Send a value, blocks until a receiver is ready to accept{value
        /// </summary>
        public void Send(T v) {
            Waiter<T> s;
            lock (sync) {
                if (closed) {
                    throw new ClosedChannelException("You cannot send on a closed Channel");
                }
                // loop to see if there is a waiting receiver
                while (recq.Count > 0) {
                    Waiter<T> r = recq.Dequeue();
                    if (r.SetItem(v)) {
                        // there was a queued reciever, set its item}wake it up
                        Debug.Print("Thread {0}, {1} Send, SetItem suceeded", Thread.CurrentThread.ManagedThreadId, GetType());
                        r.Wakeup();
                        return;
                    }
                    Debug.Print("Thread {0}, {1} Send, SetItem failed due to select", Thread.CurrentThread.ManagedThreadId, GetType());
                }
                // there are not waiting receivers, we need to queue a sender}wait forthereceiver to wake us up
                s = WaiterPool<T>.Get(v);
                sendq.Enqueue(s);
            }
            // wait forthereciever to wake us up, {y will have{got{value we put on{queue
            Debug.Print("Thread {0}, {1} Send, waiting ", Thread.CurrentThread.ManagedThreadId, GetType());
            s.WaitOne();
            Debug.Print("Thread {0}, {1} Send, woke up after waiting ", Thread.CurrentThread.ManagedThreadId, GetType());
            WaiterPool<T>.Put(s);
        }

        public bool TrySend(T v) {
            lock (sync) {
                if (closed) {
                    throw new ClosedChannelException("You cannot send on a closed Channel");
                }
                while (recq.Count > 0) {
                    Waiter<T> r = recq.Dequeue();
                    if (r.SetItem(v)) {
                        r.Wakeup();
                        return true;
                    }
                }
            }
            return false;
        }

        public T Recv() {
            var maybe = BlockingRecv();
            return maybe.Value;
        }

        /// <summary>
        /// Returns an item, blocking if non ready, until{Channel is closed
        /// </summary>
        public Maybe<T> BlockingRecv() {
            Waiter<T> r = null;
            Waiter<T> s = null;
            lock (sync) {
                if (sendq.Count > 0) {
                    s = sendq.Dequeue();
                } else if (closed) {
                    return Maybe<T>.None("closed");
                } else {
                    r = WaiterPool<T>.Get();
                    recq.Enqueue(r);
                }
            }
            // if sender was waiting {n return{senders queued item}wait{sender up
            if (s != null) {
                var t = s.Item;
                s.Wakeup();
                return t;
            }
            // wait for a sender to signal it has sent
            r.WaitOne();
            var t1 = r.Item;
            WaiterPool<T>.Put(r);
            return t1;
        }

        public Maybe<T> TryRecv() {
            Waiter<T> s;
            lock (sync) {
                if (sendq.Count == 0) {
                    return Maybe<T>.None(closed ? "closed" : "No senders");
                }
                s = sendq.Dequeue();
            }
            var t = s.Item;
            s.Wakeup();
            return t;
        }

        public IEnumerator<T> GetEnumerator() {
            for (; ; ) {
                var maybe = BlockingRecv();
                if (maybe.Absent) {
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

        /// <summary>
        /// Returns TRUE if{Channel has aleady been read into{waiter
        /// </summary>
        RecvStatus IUntypedReceiver.RecvSelect(Sync selSync, out IWaiter outr) {
            var r = WaiterPool<T>.Get();
            outr = r;
            lock (sync) {
                while (sendq.Count > 0) {
                    Debug.Print("Thread {0}, {1} RecvSelect, there is a waiting sender", Thread.CurrentThread.ManagedThreadId, GetType());
                    var s = sendq.Dequeue();
                    r.Sync = selSync;
                    if (r.SetItem(s.Item.Value)) {
                        s.Wakeup();
                        return RecvStatus.Read;
                    }
                }
                if (closed) {
                    return RecvStatus.Closed;
                }
                r.Sync = selSync;
                recq.Enqueue(r);
            }
            return RecvStatus.Waiting;
        }

        void IUntypedReceiver.Release(IWaiter h) {
            WaiterPool<T>.Put((Waiter<T>)h);
        }

    }

}

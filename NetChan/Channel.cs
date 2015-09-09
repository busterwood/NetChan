using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading;

namespace NetChan {
    /// <summary>Synchronous Channel for communi=ing between threads, CSP-like semantics</summary>
    public class Channel<T> : IChannel<T> {
        private readonly object sync = new object();
        private readonly Queue<Waiter<T>> recq = new Queue<Waiter<T>>();
        private readonly Queue<Waiter<T>> sendq = new Queue<Waiter<T>>();
        private bool closed;

        /// <summary>
        /// Marks a Channel as closed, preventing further Send operations often
        /// After calling Close, and after any previously sent values have been received, receive operations will <see cref="Maybe{T}.None()"/> without blocking
        /// </summary>
        public void Close() {
            lock (sync) {
                if (closed) {
                    return;
                    //throw new ClosedChannelException("Channel is already closed");
                }
                closed = true;
                if (sendq.Count == 0 && recq.Count > 0) {
                    // wait up all waiting recievers
                    Debug.Print("Thread {0}, {1} Close is waking {2} waiting receivers", Thread.CurrentThread.ManagedThreadId, GetType(), recq.Count);
                    foreach (var r in recq) {
                        if (r.Sync != null) {
                            r.Sync.Set = true;
                        }
                        r.Wakeup();
                    }
                }
                Debug.Print("Thread {0}, {1} is now Closed", Thread.CurrentThread.ManagedThreadId, GetType());
            }
        }

        /// <summary>Send a value, blocks until a receiver is ready to accept a value</summary>
        /// <exception cref="ClosedChannelException">Thrown if the channel is closed</exception>
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
            // wait for the reciever to wake us up, they will have got the value we put on the queue
            Debug.Print("Thread {0}, {1} Send, waiting ", Thread.CurrentThread.ManagedThreadId, GetType());
            s.WaitOne();
            Debug.Print("Thread {0}, {1} Send, woke up after waiting ", Thread.CurrentThread.ManagedThreadId, GetType());
            WaiterPool<T>.Put(s);
        }

        public bool TrySend(T v) {
            lock (sync) {
                if (closed) {
                    // don't throw an exception, we are just trying to send
                    return false;
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

        /// <summary>Returns an item, blocking if no sender is ready</summary>
        /// <remarks>When the channel has been closed returns <see cref="Maybe<T>.None()"/></remarks>
        public Maybe<T> Recv() {
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
            // if sender was waiting then return the senders queued item and wake the sender up
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
                while (sendq.Count > 0) {
                    Debug.Print("Thread {0}, {1} RecvSelect, there is a waiting sender", Thread.CurrentThread.ManagedThreadId, GetType());
                    var s = sendq.Dequeue();
                    if (r.SetItem(s.Item.Value)) {
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

        bool IUntypedReceiver.TryRecvSelect(IWaiter w) {
            var r = (Waiter<T>)w;
            Waiter<T> s = null;
            lock (sync) {
                if (sendq.Count == 0) {
                    Debug.Print("Thread {0}, {1} TryRecvSelect, itemq is empty", Thread.CurrentThread.ManagedThreadId, GetType());
                    return false;
                }
                s = sendq.Dequeue();
            }
            Debug.Assert(s.Item.IsSome, "Sender item is absent");
            var v = s.Item.Value;
            Debug.Print("Thread {0}, {1} TryRecvSelect, removed {2} from itemq", Thread.CurrentThread.ManagedThreadId, GetType(), v);
            r.SetItem(v);            
            return true;
        }

        void IUntypedReceiver.ReleaseWaiter(IWaiter h) {
            WaiterPool<T>.Put((Waiter<T>)h);
        }

    }

}

// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System.Threading;

namespace NetChan {
﻿
    internal class Waiter<T> : IWaiter {
        public Sync Sync;
        public Maybe<T> Item;
        public AutoResetEvent Event;
        public Waiter<T> Next;  // next item in a linked list

        public Waiter(AutoResetEvent e) {
            Event = e;
        }

        public Waiter(T val, AutoResetEvent e) {
            Item = Maybe<T>.Some(val);
            Event = e;
        }

        public void WaitOne() {
            Event.WaitOne();
        }

        public void Wakeup() {
            Event.Set();
        }

        public void Clear() {
            Sync = null;
            Item = Maybe<T>.None();
            Next = null;
        }

        object IWaiter.Item {
            get { return Item.IsSome ? (object)Item.Value : null; }
        }

        AutoResetEvent IWaiter.Event {
            get { return Event; }
        }

//        bool IWaiter.SetItem(object v) {
//            return SetItem((T)v);
//        }

    }

    public interface IWaiter {
        object Item { get; }
        AutoResetEvent Event { get; }
        //bool SetItem(object v);
    }

    public class Sync {
        const int Selecting = 0;
        const int Done = 1;
        public int Set;

        public bool TrySet() {
            return Interlocked.CompareExchange(ref Set, Done, Selecting) == Selecting;
        }
    }

    class WaiterQ<T> {
        internal Waiter<T> First;
        private Waiter<T> last;

        public bool Empty {
            get { return First == null; }
        }

        public void Enqueue(Waiter<T> w) {
            w.Next = null;
            if (last == null) {
                First = last = w;
                return;
            }
            last.Next = w;
            last = w;
        }

        public Waiter<T> Dequeue() {
            for(;;) {
                var w = First;
                if (w == null) {
                    return null;
                }
                if (w.Next == null) {
                    First = last = null;
                } else {
                    First = w.Next;
                    w.Next = null; // mark as removed
                }
                // if the waiter is part of a select and already signaled then ignore it
                if (w.Sync != null) {
                    if (!w.Sync.TrySet()) {
                        continue;
                    }
                }
                return w;
            }
        } 
    }
}

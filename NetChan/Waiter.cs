using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading;

namespace NetChan {
﻿
    internal class Waiter<T> : IWaiter {
        public Sync Sync;
        private Maybe<T> item;
        public AutoResetEvent Event;

        public Waiter(AutoResetEvent e) {
            Event = e;
        }

        public Waiter(T val, AutoResetEvent e) {
            item = Maybe<T>.Some(val);
            Event = e;
        }

        public void WaitOne() {
            Event.WaitOne();
        }

        public void Wakeup() {
            Event.Set();
        }

        Maybe<object> IWaiter.Item {
            get { return Item.Present ? Maybe<object>.Some(Item.Value) : Maybe<object>.None(Item.Reason); }
        }

        AutoResetEvent IWaiter.Event {
            get { return Event; }
        }

        public Maybe<T> Item {
            get { return item; }
        }

        public bool SetItem(T v) {
            // Sync will be set if this waiter is taking part in a Select
            if (Sync != null) {
                lock (Sync) {
                    // Only set the value once, if another thread tries to call SetValue it will return FALSE
                    if (Sync.Set) {
                        return false;
                    }
                    Sync.Set = true;
                    item = Maybe<T>.Some(v);
                }
                return true;
            }
            item = Maybe<T>.Some(v);
            return true;
        }

        public void Clear() {
            Sync = null;
            item = Maybe<T>.None();
        }
    }

    public interface IWaiter {
        Maybe<object> Item { get; }
        AutoResetEvent Event { get; }
    }

    public class Sync {
        public bool Set;
    }
}

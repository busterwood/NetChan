// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;
using System.ComponentModel;
using System.Threading;

namespace NetChan {
﻿
    internal class Waiter<T> : IWaiter {
        public Sync Sync;       // used by Select.Recv to ensure only one channel is read
        public Maybe<T> Item;   // the value that has been read (or the value being sent)
        public IntPtr Event;    // autoreset event handle, 20% faster than using the .NET wrapper
        public Waiter<T> Next;  // next item in a linked list (queue)

        public Waiter() {
            Event = NativeMethods.CreateEvent(IntPtr.Zero, false, false, IntPtr.Zero);
            if (Event == IntPtr.Zero) {
                throw new Win32Exception();
            }
        }

        ~Waiter() {
            var e = Event;
            if (e != IntPtr.Zero) {
                NativeMethods.CloseHandle(e);
            }
        }

        public void WaitOne() {
            if (NativeMethods.WaitForSingleObject(Event, -1) == -1) {
                throw new Win32Exception();
            }
        }

        public void Wakeup() {
            if (!NativeMethods.SetEvent(Event)) {
                throw new Win32Exception();
            }
        }

        public void Clear() {
            Sync = null;
            Item = Maybe<T>.None();
            Next = null;
        }

        object IWaiter.Item {
            get { return Item.IsSome ? (object)Item.Value : null; }
        }

        IntPtr IWaiter.Event {
            get { return Event; }
        }

    }

    public interface IWaiter {
        object Item { get; }
        IntPtr Event { get; }
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

        internal bool Remove(Waiter<T> rm) {
            Waiter<T> prev = null;
            var w = First;
            while (w != null) {
                if (w == rm) {
                    if (prev != null) {
                        prev.Next = w.Next;
                    }
                    if (w == First) {
                        First = w.Next;
                    }
                    if (w == last) {
                        last = prev;
                    }
                    w.Next = null;
                    return true;
                }
                prev = w;
                w = w.Next; 
            }
            return false;
        }
    }
}

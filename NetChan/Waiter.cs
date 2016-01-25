// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace NetChan {
﻿
    internal class Waiter<T> : IWaiter, ISelected<T> {
        public Sync Sync;       // used by Select.Recv to ensure only one channel is read
        public Maybe<T> Value;   // the value that has been read (or the value being sent)
        public IntPtr Event;    // autoreset event handle, 20% faster than using the .NET wrapper
        public Waiter<T> Next;  // next item in a linked list (queue)
        private int index;

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
            Value = Maybe<T>.None();
            Next = null;
            index = -1;
        }

        public bool TrySet()
        {
            //Debug.Assert(Sync != null && index == -1, "Expected sync and index to both be set");
            if (Sync != null && index == -1)
            {
                if (Debugger.IsAttached)
                    Debugger.Break();
                throw new Exception("Expected sync and index to both be set");
            }
            return Sync.TrySet(index);
        }

        int IWaiter.Index {
            get { return index; }
            set { index = value; }
        }

        void IWaiter.Clear(Sync sync) {
            Sync = sync;
            Value = Maybe<T>.None();
            Next = null;
        }

        void IWaiter.SetSync(Sync sync) {
            Sync = sync;
            Next = null;
        }

        Maybe<T> ISelected<T>.Value {
            get { return Value; }
            set { Value = value; }
        }

        object ISelected.Value {
            get { return Value.IsSome ? (object)Value.Value : null; }
            set { Value = value == null ? Maybe<T>.None() : Maybe<T>.Some((T) value); }
        }

        int ISelected.Index => index;

        IntPtr IWaiter.Event => Event;
    }

    public interface IWaiter : ISelected {
        new int Index { get; set; }
        IntPtr Event { get; }
        void Clear(Sync sync);
        void SetSync(Sync sync);
    }

    public interface ISelected {
        int Index { get; }
        object Value { get; set; }
    }

    public interface ISelected<T> : ISelected {
        new Maybe<T> Value { get; set; }
    }

    class WaiterQ<T> {
        internal Waiter<T> First;
        private Waiter<T> last;

        public bool Empty => First == null;

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
                if (w.Sync != null && !w.TrySet()) {
                    continue;
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

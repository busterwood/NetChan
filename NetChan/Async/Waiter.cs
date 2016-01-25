﻿// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NetChan.Async {
﻿
    internal class Waiter<T> : IWaiter, ISelected<T> {
        public TaskCompletionSource<int> CompletionSource;
        public Maybe<T> Value;   // the value that has been read (or the value being sent)
        public AsyncAutoResetEvent Event;
        public Waiter<T> Next;  // next item in a linked list (queue)
        private int index = -1;

        public Waiter() {
            Event = new AsyncAutoResetEvent();
        }

        public Task WaitOne() {
            return Event.WaitAsync();
        }

        public void Wakeup() {
            Event.Set();
        }

        public void Clear() {
            CompletionSource = null;
            Value = Maybe<T>.None();
            Next = null;
            index = -1;
        }

        public bool TrySetCompletionSource() {
            if (CompletionSource == null) {
                return true;
            }
            return CompletionSource.TrySetResult(index);
        }

        int IWaiter.Index {
            get { return index; }
            set { index = value; }
        }

        void IWaiter.Clear(TaskCompletionSource<int> sync) {
            CompletionSource = sync;
            Value = Maybe<T>.None();
            Next = null;
        }

        void IWaiter.SetSync(TaskCompletionSource<int> sync) {
            CompletionSource = sync;
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

        AsyncAutoResetEvent IWaiter.Event => Event;
    }

    public interface IWaiter : ISelected {
        new int Index { get; set; }
        AsyncAutoResetEvent Event { get; }
        void Clear(TaskCompletionSource<int> sync);
        void SetSync(TaskCompletionSource<int> sync);
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
                //// if the waiter is part of a select and already signaled then ignore it
                //if (!w.TrySetCompletionSource()) {
                //    continue;
                //}
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

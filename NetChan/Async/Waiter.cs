// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NetChan.Async {
﻿
    internal class Waiter<T> : IWaiter, ISelected<T> {
        public TaskCompletionSource<int> CompletionSource;
        public Maybe<T> Value;   // the value that has been read (or the value being sent)
        public Waiter<T> Next;  // next item in a linked list (queue)
        private int index = -1;

        public Waiter() {
            CompletionSource = new TaskCompletionSource<int>();
        }
        
        public void Clear() {
            CompletionSource = new TaskCompletionSource<int>();
            Value = Maybe<T>.None();
            Next = null;
            index = -1;
        }

        public bool TrySetCompletionSource() {
            return CompletionSource.TrySetResult(index);
        }

        int IWaiter.Index {
            get { return index; }
            set { index = value; }
        }

        void IWaiter.ClearValue() {
            Value = Maybe<T>.None();
        }

        void IWaiter.SetCompletion(TaskCompletionSource<int> sync, int i) {
            if (sync == null) throw new ArgumentNullException(nameof(sync));
            index = i;
            Next = null;
            CompletionSource = sync;
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

    }

    public interface IWaiter : ISelected {
        new int Index { get; set; }
        void SetCompletion(TaskCompletionSource<int> tcs, int i);
        void ClearValue();
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
                // TODO: can we do this here? 
                // if (!w.TrySetCompletionSource()) {
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

// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace NetChan {
    /// <summary>Select receives from one or more channels</summary>
    public class Select : List<IUntypedReceiver> {
        private readonly Random rand = new Random(Environment.TickCount);
        private readonly int[] readOrder;

        public Select(params IUntypedReceiver[] chans)
            : base(chans) {
            readOrder = new int[Count];
            for (int i = 0; i < readOrder.Length; i++) {
                readOrder[i] = i;
            }
        }

        /// <summary>Blocking, non-deterministic read of many channels</summary>
        /// <returns>The index of the channel that was read, or -1 if no channels are ready to read</returns>
        public Selected Recv() {
            var waiters = new IWaiter[Count];
            var handles = new WaitHandle[Count];
            var handleIdx = new int[Count];
            var handleCount = 0;
            var sync = new Sync();
            Shuffle(readOrder);
            foreach (int i in readOrder) {
                if (this[i] == null) {
                    Debug.Print("Thread {0}, {1} Recv: channel is null, index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                    continue;
                }
                waiters[i] = this[i].GetWaiter(sync);
                if (this[i].RecvSelect(waiters[i])) {
                    Debug.Print("Thread {0}, {1} Recv: RecvSelect returned {2} index {3}", Thread.CurrentThread.ManagedThreadId, GetType(), waiters[i].Item, i);
                    return new Selected(i, waiters[i].Item);
                }
                Debug.Print("Thread {0}, {1} Recv: RecvSelect waiting index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                Debug.Assert(waiters[i].Event != null); 
                handles[handleCount] = waiters[i].Event;
                handleIdx[handleCount] = i;
                handleCount++;
            }
            if (handleCount == 0) {
                throw new InvalidOperationException("All channels are null, select will block forever");
            }
            // some Channels might be null
            if (handleCount < Count) {
                Array.Resize(ref handles, handleCount);
            }
            Debug.Print("Thread {0}, {1} Recv, there are {2} wait handles", Thread.CurrentThread.ManagedThreadId, GetType(), handles.Length);
            int signalled = WaitHandle.WaitAny(handles);
            Debug.Print("Thread {0}, {1} Recv, woke up after WaitAny", Thread.CurrentThread.ManagedThreadId, GetType());
            int sig = handleIdx[signalled];
            var maybe = waiters[sig].Item;
            Debug.Print("Thread {0}, {1} Recv, sync Set, idx {2}, value {3}", Thread.CurrentThread.ManagedThreadId, GetType(), sig, maybe);

            // release waiters otherwise slow channels will build up
            foreach (int i in readOrder) {
                if (waiters[i] == null) {
                    continue;
                }
                if (i != sig) {
                    this[i].RemoveReceiver(waiters[i]);
                }
                this[i].ReleaseWaiter(waiters[i]);
            }
            return new Selected(sig, maybe);
        }

        /// <summary>Non-blocking, non-deterministic read of many channels</summary>
        /// <returns>The index of the channel that was read, or -1 if no channels are ready to read</returns>
        public Selected TryRecv() {
            var waiters = new IWaiter[Count];
            Shuffle(readOrder);
            try {
                foreach (int i in readOrder) {
                    if (this[i] == null) {
                        Debug.Print("Thread {0}, {1} Recv: channel is null, index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                        continue;
                    }
                    waiters[i] = this[i].GetWaiter(null);
                    if (this[i].TryRecvSelect(waiters[i])) {
                        Debug.Print("Thread {0}, {1} Recv: RecvSelect returned {2} index {3}", Thread.CurrentThread.ManagedThreadId, GetType(), waiters[i].Item, i);
                        return new Selected(i, waiters[i].Item);
                    }
                }
            } finally {
                for (int i = 0; i < waiters.Length; i++) {
                    if (waiters[i] == null) {
                        continue;
                    }
                    this[i].ReleaseWaiter(waiters[i]);
                }
            }
            return new Selected(-1, Maybe<object>.None());
        }

        /// <summary>Modern version of the Fisher-Yates shuffle</summary>
        private void Shuffle<T>(T[] array) {
            for (int i = array.Length-1; i > 0; i--) {
                int index = rand.Next(i);
                // swap the values
                var tmp = array[index];
                array[index] = array[i];
                array[i] = tmp;
            }
        }
   
    }


    public struct Selected {
        public readonly int Index;
        public readonly object Value;

        public Selected(int idx, object value) {
            Index = idx;
            Value = value;
        }
    }
}

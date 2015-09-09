using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace NetChan {
    /// <summary>Select receives from one or more channels</summary>
    public class Select {
        private readonly Random rand = new Random(Environment.TickCount);
        private readonly IUntypedReceiver[] chans;
        private readonly int[] readOrder;

        public Select(params IUntypedReceiver[] Channels) {
            this.chans = Channels;
            readOrder = new int[Channels.Length];
            for (int i = 0; i < readOrder.Length; i++) {
                readOrder[i] = i;
            }
        }

        /// <summary>Null channels block forever</summary>
        /// <param name="idx">Index of the channel</param>
        public void SetNull(int idx) {
            chans[idx] = null;
        }

        /// <summary>Blocking, non-deterministic read of many channels</summary>
        /// <returns>The index of the channel that was read, or -1 if no channels are ready to read</returns>
        public Selected Recv() {
            var waiters = new IWaiter[chans.Length];
            var handles = new WaitHandle[chans.Length];
            var handleIdx = new int[chans.Length];
            var handleCount = 0;
            var sync = new Sync();
            Shuffle(readOrder);
            foreach (int i in readOrder) {
                if (chans[i] == null) {
                    Debug.Print("Thread {0}, {1} Recv: channel is null, index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                    continue;
                }
                waiters[i] = chans[i].GetWaiter(sync);
                if (chans[i].RecvSelect(waiters[i])) {
                    Debug.Print("Thread {0}, {1} Recv: RecvSelect returned {2} index {3}", Thread.CurrentThread.ManagedThreadId, GetType(), waiters[i].Item, i);
                    return new Selected(i, waiters[i].Item);
                }
                Debug.Print("Thread {0}, {1} Recv: RecvSelect waiting index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                if (waiters[i].Event == null) {
                    throw new InvalidOperationException("No wait handle");
                }
                handles[handleCount] = waiters[i].Event;
                handleIdx[handleCount] = i;
                handleCount++;
            }
            if (handleCount == 0) {
                throw new InvalidOperationException("All channels are null, select will block forever");
            }
            // some Channels might be null
            if (handleCount < chans.Length) {
                Array.Resize(ref handles, handleCount);
            }
            Debug.Print("Thread {0}, {1} Recv, there are {2} wait handles", Thread.CurrentThread.ManagedThreadId, GetType(), handles.Length);
            int signalled = WaitHandle.WaitAny(handles);
            Debug.Print("Thread {0}, {1} Recv, woke up after WaitAny", Thread.CurrentThread.ManagedThreadId, GetType());
            int sig = handleIdx[signalled];
            var maybe = waiters[sig].Item;
            Debug.Print("Thread {0}, {1} Recv, sync Set, idx {2}, value {3}", Thread.CurrentThread.ManagedThreadId, GetType(), sig, maybe);
            // only release the signalled Channel,the others will be releases as the Channel is read
            chans[sig].ReleaseWaiter(waiters[sig]);
            return new Selected(sig, maybe);
        }

        /// <summary>Non-blocking, non-deterministic read of many channels</summary>
        /// <returns>The index of the channel that was read, or -1 if no channels are ready to read</returns>
        public Selected TryRecv() {
            var waiters = new IWaiter[chans.Length];
            Shuffle(readOrder);
            try {
                foreach (int i in readOrder) {
                    if (chans[i] == null) {
                        Debug.Print("Thread {0}, {1} Recv: channel is null, index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                        continue;
                    }
                    waiters[i] = chans[i].GetWaiter(null);
                    if (chans[i].TryRecvSelect(waiters[i])) {
                        Debug.Print("Thread {0}, {1} Recv: RecvSelect returned {2} index {3}", Thread.CurrentThread.ManagedThreadId, GetType(), waiters[i].Item, i);
                        return new Selected(i, waiters[i].Item);
                    }
                }
            } finally {
                for (int i = 0; i < waiters.Length; i++) {
                    if (waiters[i] == null) {
                        continue;
                    }
                    chans[i].ReleaseWaiter(waiters[i]);
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

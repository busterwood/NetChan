// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace NetChan {
    /// <summary>Select receives from one or more channels</summary>
    public class Select : IDisposable {
        private readonly Random rand = new Random(Environment.TickCount);
        private readonly int[] readOrder;
        private readonly List<IUntypedReceiver> chans;
        private readonly IWaiter[] waiters;

        public Select(params IUntypedReceiver[] chans) {
            readOrder = new int[chans.Length];
            for (int i = 0; i < readOrder.Length; i++) {
                readOrder[i] = i;
            }
            this.chans = new List<IUntypedReceiver>(chans);

            waiters = new IWaiter[chans.Length];
            for (int i = 0; i < chans.Length; i++) {
                if (chans[i] == null) {
                    Debug.Print("Thread {0}, {1} Recv: channel is null, index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                    continue;
                }
                waiters[i] = chans[i].GetWaiter();
            }
        }

        public void RemoveAt(int i) {
            chans[i] = null;
            var w = waiters[i];
            waiters[i] = null;
            if (w != null) {
                w.Dispose();
            }
        }

        /// <summary>Blocking, non-deterministic read of many channels</summary>
        /// <returns>The index of the channel that was read, or -1 if no channels are ready to read</returns>
        public Selected Recv() {
            Shuffle(readOrder);
            try {
                var handleCount = 0;
                var sync = new Sync();
                foreach (int i in readOrder) {
                    if (waiters[i] == null) {
                        continue;
                    }
                    waiters[i].Clear(sync);
                    if (chans[i].RecvSelect(waiters[i])) {
                        var v1 = waiters[i].Item;
                        Debug.Print("Thread {0}, {1} Recv: RecvSelect returned {2} index {3}", Thread.CurrentThread.ManagedThreadId, GetType(), v1, i);
                        return new Selected(i, v1);
                    }
                    Debug.Print("Thread {0}, {1} Recv: RecvSelect waiting index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                    handleCount++;
                }
                if (handleCount == 0) {
                    throw new InvalidOperationException("All channels are null, select will block forever");
                }
                // collect the handles into an array we can pass to WaitAny
                var handles = new IntPtr[handleCount];
                var handleIdx = new int[handleCount];
                handleCount = 0;
                for (int i = 0; i < waiters.Length; i++) {
                    if (waiters[i] == null) {
                        continue;
                    }
                    handles[handleCount] = waiters[i].Event;
                    handleIdx[handleCount] = i;
                    handleCount++;
                }
                Debug.Print("Thread {0}, {1} Recv, there are {2} wait handles", Thread.CurrentThread.ManagedThreadId, GetType(), handles.Length);
                int signalled = NativeMethods.WaitForMultipleObjects(handleCount, handles, false, -1);
                if (signalled == -1) {
                    throw new Win32Exception();
                }
                Debug.Print("Thread {0}, {1} Recv, woke up after WaitAny", Thread.CurrentThread.ManagedThreadId, GetType());
                int sig = handleIdx[signalled];
                object val = waiters[sig].Item;
                Debug.Print("Thread {0}, {1} Recv, sync Set, idx {2}, value {3}", Thread.CurrentThread.ManagedThreadId, GetType(), sig, val);
                return new Selected(sig, val);
            } finally {
                // release waiters otherwise slow channels will build up
                for (int i = 0; i < waiters.Length; i++) {
                    if (waiters[i] != null) {
                        chans[i].RemoveReceiver(waiters[i]);
                    }
                }
            }
            
        }

        /// <summary>Non-blocking, non-deterministic read of many channels</summary>
        /// <returns>The index of the channel that was read, or -1 if no channels are ready to read</returns>
        public Selected TryRecv() {
            Shuffle(readOrder);
            foreach (int i in readOrder) {
                if (waiters[i] == null) {
                    Debug.Print("Thread {0}, {1} TryRecv: channel is null, index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                    continue;
                }
                waiters[i].Clear(null);
                if (chans[i].TryRecvSelect(waiters[i])) {
                    Debug.Print("Thread {0}, {1} TryRecv: TryRecvSelect returned {2} index {3}", Thread.CurrentThread.ManagedThreadId, GetType(), waiters[i].Item, i);
                    return new Selected(i, waiters[i].Item);
                }
            }
            return new Selected(-1, null);
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

        public void Dispose() {
            foreach (IWaiter w in waiters) {
                if (w != null) {
                    w.Dispose();
                }                 
            }
            Array.Clear(waiters, 0, waiters.Length);
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

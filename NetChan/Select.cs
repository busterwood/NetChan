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
        private IntPtr[] handles;
        private int[] handleIdx;
        private readonly Sync sync;

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
            handles = new IntPtr[chans.Length];
            handleIdx = new int[chans.Length];
            sync = new Sync();
        }

        public void ClearAt(int i) {
            if (chans[i] != null && waiters[i] != null) {
                chans[i].ReleaseWaiter(waiters[i]);
                waiters[i] = null;
            }
            chans[i] = null;
        }

        /// <summary>Blocking, non-deterministic read of many channels</summary>
        /// <returns>The index of the channel that was read, or -1 if no channels are ready to read</returns>
        public ISelected Recv() {
            Shuffle(readOrder);
            try {
                sync.Set = 0;
                var handleCount = 0;
                foreach (int i in readOrder) {
                    if (waiters[i] == null) {
                        continue;
                    }                    
                    waiters[i].Clear(sync);
                    waiters[i].Index = i;
                    if (chans[i].RecvSelect(waiters[i])) {
                        Debug.Print("Thread {0}, {1} Recv: RecvSelect returned index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                        return waiters[i];
                    }
                    Debug.Print("Thread {0}, {1} Recv: RecvSelect waiting index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                    handleCount++;
                }
                if (handleCount == 0) {
                    throw new InvalidOperationException("All channels are null, select will block forever");
                }
                int sig = WaitForSignal(handleCount);
                Debug.Print("Thread {0}, {1} Recv, sync Set, idx {2}", Thread.CurrentThread.ManagedThreadId, GetType(), sig);
                return waiters[sig];
            } finally {
                // release waiters otherwise slow channels will build up
                for (int i = 0; i < waiters.Length; i++) {
                    if (waiters[i] != null) {
                        chans[i].RemoveReceiver(waiters[i]);
                    }
                }
            }            
        }

        private int WaitForSignal(int handleCount) {
            // collect the handles into an array we can pass to WaitAny
            if (handleCount < handles.Length) {
                handles = new IntPtr[handleCount];
                handleIdx = new int[handleCount];                
            }
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
            return handleIdx[signalled];
        }

        /// <summary>Non-blocking, non-deterministic read of many channels</summary>
        /// <returns>The index of the channel that was read, or -1 if no channels are ready to read</returns>
        public ISelected TryRecv() {
            Shuffle(readOrder);
            foreach (int i in readOrder) {
                if (waiters[i] == null) {
                    Debug.Print("Thread {0}, {1} TryRecv: channel is null, index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                    continue;
                }
                waiters[i].Clear(null);
                waiters[i].Index = i;
                if (chans[i].TryRecvSelect(waiters[i])) {
                    Debug.Print("Thread {0}, {1} TryRecv: TryRecvSelect returned {2} index {3}", Thread.CurrentThread.ManagedThreadId, GetType(), waiters[i].Value, i);
                    return waiters[i];
                }
            }
            return NotSelected.Instance;
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
            for (int i = 0; i < chans.Count; i++) {
                if (chans[i] != null) {
                    chans[i].ReleaseWaiter(waiters[i]);
                    waiters[i] = null;
                }
                chans[i] = null;
            }
        }

    }

    internal class NotSelected : ISelected {
        public static readonly NotSelected Instance = new NotSelected();

        public int Index {
            get { return -1; }
        }

        public object Value {
            get { return null; }
        }
    }

}

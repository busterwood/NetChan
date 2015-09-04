using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace NetChan {
    public class Select {
        private readonly IUntypedReceiver[] Channels;
        private readonly int[] readOrder;
        private int index;
        private object value;

        public Select(params IUntypedReceiver[] Channels) {
            index = -1;
            this.Channels = Channels;
            readOrder = new int[Channels.Length];
            for (int i = 0; i < readOrder.Length; i++) {
                readOrder[i] = i;
            }
        }

        public int Index {
            get { return index; }
        }

        public object Value {
            get {
                if (index == -1) {
                    throw new InvalidOperationException("Nothing selected");
                }
                return value;
            }
        }

        public T AsValue<T>() {
            return (T)Value;
        }

        public int Recv() {
            var waiters = new IWaiter[Channels.Length];
            var handles = new WaitHandle[Channels.Length];
            var handleIdx = new int[Channels.Length];
            var handleCount = 0;
            var sync = new Sync();
            index = -1;
            value = null;
            Shuffle(readOrder);
            for (int i = 0; i < readOrder.Length; i++) {
                var idx = readOrder[i];
                switch (Channels[idx].RecvSelect(sync, out waiters[idx])) {
                    case RecvStatus.Closed:
                        break;
                    case RecvStatus.Read:
                        index = idx;
                        value = waiters[idx].Item.Value;
                        return index;
                    case RecvStatus.Waiting:
                        if (waiters[idx].Event == null) {
                            throw new InvalidOperationException("No wait handle");
                        }
                        handles[handleCount] = waiters[idx].Event;
                        handleIdx[handleCount] = idx;
                        handleCount++;
                        break;
                }
            }
            // if all Channels are closed
            if (handleCount == 0) {
                return -1;
            }
            // some Channels might be closed
            if (handleCount < Channels.Length) {
                Array.Resize(ref handles, handleCount);
            }
            // loop because we might be woken up by a Channel closing
            Debug.Print("Thread {0}, {1} Recv, there are {2} wait handles", Thread.CurrentThread.ManagedThreadId, GetType(), handles.Length);
            for (; ; ) {
                int signalled = WaitHandle.WaitAny(handles);
                Debug.Print("Thread {0}, {1} Recv, woke up after WaitAny", Thread.CurrentThread.ManagedThreadId, GetType());
                int sig = handleIdx[signalled];
                var maybe = waiters[sig].Item;
                if (!sync.Set || maybe.Absent) {
                    // woken up by a closed Channel
                    Debug.Print("Thread {0}, {1} Recv, sync {2}, maybe {3}", Thread.CurrentThread.ManagedThreadId, GetType(), sync.Set, maybe);
                    handleCount--;
                    if (handleCount == 0) {
                        return -1;
                    }
                    continue;
                }
                value = maybe.Value;
                index = sig;
                Debug.Print("Thread {0}, {1} Recv, sync Set, idx {2}, value {3}", Thread.CurrentThread.ManagedThreadId, GetType(), sig, value);
                // only release the signalled Channel,the others will be releases as the Channel is read
                Channels[sig].Release(waiters[sig]);
                return index;
            }
        }

        /// <summary>
        /// Modern version of the Fisher-Yates shuffle
        /// </summary>
        private static void Shuffle<T>(T[] array) {
            var r = new Random();
            for (int i = array.Length - 1; i > 0; i--) {
                int index = r.Next(i);
                // swap the values
                var tmp = array[index];
                array[index] = array[i];
                array[i] = tmp;
            }
        }
    }
}

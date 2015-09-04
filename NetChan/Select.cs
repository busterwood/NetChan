using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace NetChan {
    /// <summary>Select receives from one or more channels</summary>
    /// <remarks>Enumerating a Select does blocking reads from all the channels until the all the channels are closed.</remarks>
    public class Select : IEnumerable<int> {
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

        /// <summary>The index of the selected channel, or -1 if no channel has recieved.</summary>
        public int Index {
            get { return index; }
        }

        /// <summary>The value that has been recieved.</summary>
        /// <exception cref="InvalidOperationException">Thrown if no channel has been recieved, i.e. Index is -1</exception>
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

        /// <summary>Blocking, non-deterministic read of many channels</summary>
        /// <returns>The index of the channel that was read, or -1 if no channels are ready to read</returns>
        public int Recv() {
            var waiters = new IWaiter[Channels.Length];
            var handles = new WaitHandle[Channels.Length];
            var handleIdx = new int[Channels.Length];
            var handleCount = 0;
            var sync = new Sync();
            index = -1;
            value = null;
            Shuffle(readOrder);
            foreach (int i in readOrder) {
                switch (Channels[i].RecvSelect(sync, out waiters[i])) {
                    case RecvStatus.Closed:
                        Debug.Print("Thread {0}, {1} Recv: RecvSelect channel {2} is closed", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                        break;
                    case RecvStatus.Read:
                        index = i;
                        value = waiters[i].Item.Value;
                        Debug.Print("Thread {0}, {1} Recv: RecvSelect returned {2} index {3}", Thread.CurrentThread.ManagedThreadId, GetType(), value, index);
                        return index;
                    case RecvStatus.Waiting:
                        Debug.Print("Thread {0}, {1} Recv: RecvSelect waiting index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                        if (waiters[i].Event == null) {
                            throw new InvalidOperationException("No wait handle");
                        }
                        handles[handleCount] = waiters[i].Event;
                        handleIdx[handleCount] = i;
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

        /// <summary>Non-blocking, non-deterministic read of many channels</summary>
        /// <returns>The index of the channel that was read, or -1 if no channels are ready to read</returns>
        public int TryRecv() {
            index = -1;
            value = null;
            Shuffle(readOrder);
            foreach (int i in readOrder) {
                var maybe = Channels[i].TryRecvSelect();
                if (maybe.Present) {
                    index = i;
                    value = maybe.Value;
                    Debug.Print("Thread {0}, {1} Recv: RecvSelect returned {2} index {3}", Thread.CurrentThread.ManagedThreadId, GetType(), value, index);
                    break;
                }
            }
            return index;
        }

        /// <summary>Modern version of the Fisher-Yates shuffle</summary>
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

        /// <summary>Enumerating a Select does blocking reads from all the channels until the all the channels are closed.</summary>
        public IEnumerator<int> GetEnumerator() {
            while(Recv() >= 0) {
                yield return index;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }
    }
}

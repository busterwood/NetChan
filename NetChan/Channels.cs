// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace NetChan {
    /// <summary>Send and/or Recieve operations over many channels</summary>
    public sealed class Channels : IDisposable {
        private readonly Random rand = new Random(Environment.TickCount);
        private readonly Op[] ops;
        private readonly int[] pollOrder;
        private IntPtr[] handles;
        private int[] handleIdx;
        private readonly Sync sync;

        public Channels(params Op[] ops) {
            this.ops = ops;
            pollOrder = CreatePollOrder(ops.Length);
            handles = new IntPtr[ops.Length];
            handleIdx = new int[ops.Length];
            sync = new Sync();
        }

        private static int[] CreatePollOrder(int size) {
            var pollOrder = new int[size];
            for (int i = 0; i < pollOrder.Length; i++) {
                pollOrder[i] = i;
            }
            return pollOrder;
        }

        public void ClearAt(int i) {
            ops[i].Release();
        }

        /// <summary>Blocking, non-deterministic send and/or recieve of many channels</summary>
        /// <returns>The index of the channel that was actioned</returns>
        public ISelected Select() {
            Shuffle(pollOrder);
            try {
                sync.Set = 0;
                var handleCount = 0;
                foreach (int i in pollOrder) {
                    if (ops[i].Chan == null) {
                        continue;
                    }
                    ops[i].ResetWaiter(i, sync);
                    if (ops[i].Blocking()) {
                        Debug.Print("Thread {0}, {1} Recv: RecvSelect returned index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                        return ops[i].Waiter;
                    }
                    Debug.Print("Thread {0}, {1} Recv: RecvSelect waiting index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                    handleCount++;
                }
                if (handleCount == 0) {
                    throw new InvalidOperationException("All channels are null, select will block forever");
                }
                int sig = WaitForSignal(handleCount);
                Debug.Print("Thread {0}, {1} Recv, sync Set, idx {2}", Thread.CurrentThread.ManagedThreadId, GetType(), sig);
                return ops[sig].Waiter;
            } finally {
                // release waiters otherwise slow channels will build up
                for (int i = 0; i < ops.Length; i++) {
                    ops[i].RemoveReceiver();
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
            for (int i = 0; i < ops.Length; i++) {
                if (ops[i].Waiter == null) {
                    continue;
                }
                handles[handleCount] = ops[i].Waiter.Event;
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

        /// <summary>Non-blocking, non-deterministic send and/or recieve of many channels</summary>
        /// <returns>The index of the channel that was actioned, or -1 if no channels are ready</returns>
        public ISelected TrySelect() {
            Shuffle(pollOrder);
            foreach (int i in pollOrder) {
                if (ops[i].Waiter == null) {
                    Debug.Print("Thread {0}, {1} TryRecv: channel is null, index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                    continue;
                }
                ops[i].ResetWaiter(i, null);
                if (ops[i].NonBlocking()) {
                    Debug.Print("Thread {0}, {1} TryRecv: TryRecvSelect returned {2} index {3}", Thread.CurrentThread.ManagedThreadId, GetType(), ops[i].Waiter.Value, i);
                    return ops[i].Waiter;
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
            for (int i = 0; i < ops.Length; i++) {
                ops[i].Release();
            }
        }

        public ISelected this[int i] {
            get { return ops[i].Waiter; }
        }
    }

    /// <summary>
    /// A send or receive operation on a channel
    /// </summary>
    public abstract class Op {
        private IChannel chan;
        internal IWaiter Waiter;

        protected Op(IChannel chan) {
            this.chan = chan;
            Waiter = chan != null ? chan.GetWaiter() : null;
        }

        public IChannel Chan {
            get { return chan; }
            internal set { chan = value; }
        }

        internal abstract void ResetWaiter(int i, Sync sync);

        internal void Release() {
            if (chan != null && Waiter != null) {
                chan.ReleaseWaiter(Waiter);
                Waiter = null;
            }
            chan = null;
        }

        internal void RemoveReceiver() {
            if (chan != null && Waiter != null) {
                chan.RemoveReceiver(Waiter);
            }
        }

        internal abstract bool Blocking();

        internal abstract bool NonBlocking();

        /// <summary>Creates a send operation on channel</summary>
        public static Op Send(IChannel chan) {
            return new SendOp(chan);
        }

        /// <summary>Creates a recieve operation on channel</summary>
        public static Op Recv(IChannel chan) {
            return new RecvOp(chan);
        }
    }

    class RecvOp : Op {
        public RecvOp(IChannel chan) : base(chan) {}

        internal override bool Blocking() {
            return Chan.Recv(Waiter);
        }

        internal override bool NonBlocking() {
            return Chan.TryRecv(Waiter);
        }

        internal override void ResetWaiter(int i, Sync sync) {
            Waiter.Clear(sync);
            Waiter.Index = i;
        }
    }


    class SendOp : Op {
        public SendOp(IChannel chan) : base(chan) { }

        internal override bool Blocking() {
            return Chan.Send(Waiter);
        }

        internal override bool NonBlocking() {
            return Chan.TrySend(Waiter);
        }

        internal override void ResetWaiter(int i, Sync sync) {
            Waiter.SetSync(sync); // dont call clear as this clear the value we are sending
            Waiter.Index = i;
        }
    }

    internal class NotSelected : ISelected {
        public static readonly NotSelected Instance = new NotSelected();

        public int Index {
            get { return -1; }
        }

        public object Value {
            get { return null; }
            set { }
        }
    }

}

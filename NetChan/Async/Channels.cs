// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NetChan.Async {
    /// <summary>Send and/or Receive operations over many channels</summary>
    public sealed class Channels {
        private readonly Random rand = new Random(Environment.TickCount);
        private readonly Op[] ops;
        private readonly int[] pollOrder;

        public Channels(params Op[] ops) {
            this.ops = ops;
            pollOrder = CreatePollOrder(ops.Length);
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

        /// <summary>Blocking, non-deterministic send and/or receive of many channels</summary>
        /// <returns>The index of the channel that was actioned</returns>
        /// <remarks>
        /// If more than one channel is ready then this method randomly selects one to accept, 
        /// It DOES NOT choose based on declaration order
        /// </remarks>
        public async Task<ISelected> Select() {
            var tcs = new TaskCompletionSource<int>();
            Shuffle(pollOrder);
            try {
                var taskCount = 0;
                foreach (int i in pollOrder) {
                    if (ops[i].Chan == null) {
                        //Debug.Print("Thread {0}, {1} Select: channel {2} is closed", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                        continue;
                    }
                    ops[i].ResetWaiter(i, tcs);
                    //Debug.Print("Thread {0}, {1} Select: Seeing if index {2} is ready", Thread.CurrentThread.ManagedThreadId, GetType(), i);               
                    if (ops[i].SendOrRecv()) {
                        //Debug.Print("Thread {0}, {1} Select: returned index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                        return ops[i].Waiter;
                    }
                    //Debug.Print("Thread {0}, {1} Select: will wait for index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                    taskCount++;
                }
                if (taskCount == 0) {
                    throw new InvalidOperationException("All channels are null, select will block forever");
                }
                //Debug.Print("Thread {0}, {1} Select, must wait", Thread.CurrentThread.ManagedThreadId, GetType());
                int sig = await tcs.Task;
                //Debug.Print("Thread {0}, {1} Select, sync Value, idx {2}", Thread.CurrentThread.ManagedThreadId, GetType(), sig);
                return ops[sig].Waiter;
            } finally {
                // release waiters otherwise slow channels will build up
                foreach (Op t in ops) {
                    t.RemoveReceiver();
                }
            }
        }

        /// <summary>Non-blocking, non-deterministic send and/or receive of many channels</summary>
        /// <returns>The index of the channel that was actioned, or -1 if no channels are ready</returns>
        /// <remarks>
        /// If more than one channel is ready then this method randomly selects one to accept, 
        /// It DOES NOT choose based on declaration order
        /// </remarks>
        public ISelected TrySelect() {
            var tcs = new TaskCompletionSource<int>();
            Shuffle(pollOrder);
            foreach (int i in pollOrder) {
                if (ops[i].Waiter == null) {
                    //Debug.Print("Thread {0}, {1} TrySelect: channel is null, index {2}", Thread.CurrentThread.ManagedThreadId, GetType(), i);
                    continue;
                }
                ops[i].ResetWaiter(i, tcs);
                if (ops[i].TrySendOrRecv()) {
                    //Debug.Print("Thread {0}, {1} TrySelect: returned {2} index {3}", Thread.CurrentThread.ManagedThreadId, GetType(), ops[i].Waiter.Value, i);
                    return ops[i].Waiter;
                }
            }
            return NotSelected.Instance;
        }

        /// <summary>Modern version of the Fisher-Yates shuffle</summary>
        internal void Shuffle<T>(T[] array) {
            for (int i = array.Length-1; i > 0; i--) {
                int j = rand.Next(i+1);
                if (j != i)
                {
                    // swap the values
                    var tmp = array[j];
                    array[j] = array[i];
                    array[i] = tmp;
                }
            }
        }

        public ISelected this[int i] => ops[i].Waiter;
    }

    /// <summary>
    /// A send or receive operation on a channel
    /// </summary>
    public abstract class Op {
        private IChannel chan;
        internal IWaiter Waiter;

        protected Op(IChannel chan) {
            this.chan = chan;
            Waiter = chan?.GetWaiter();
        }

        public IChannel Chan {
            get { return chan; }
            internal set { chan = value; }
        }

        internal abstract void ResetWaiter(int i, TaskCompletionSource<int> sync);

        internal void Release() {
            Waiter = null;
            chan = null;
        }

        internal void RemoveReceiver() {
            if (chan != null && Waiter != null) {
                chan.RemoveReceiver(Waiter);
            }
        }

        /// <summary>Call the blocking version of the operation, i.e. Send or Recv</summary>
        internal abstract bool SendOrRecv();

        /// <summary>Call the non-blocking version of the operation, i.e. TrySend or TryRecv</summary>
        internal abstract bool TrySendOrRecv();

        /// <summary>Creates a send operation on channel</summary>
        public static Op Send(IChannel chan) {
            return new SendOp(chan);
        }

        /// <summary>Creates a receive operation on channel</summary>
        public static Op Recv(IChannel chan) {
            return new RecvOp(chan);
        }
    }

    class RecvOp : Op {
        public RecvOp(IChannel chan) : base(chan) {}

        internal override bool SendOrRecv() {
            //Debug.Print("Thread {0}, {1} Blocking: seeing is channel of {2} is ready", Thread.CurrentThread.ManagedThreadId, GetType(), Chan.GetType());
            return Chan.Recv(Waiter);
        }

        internal override bool TrySendOrRecv() {
            return Chan.TryRecv(Waiter);
        }

        internal override void ResetWaiter(int i, TaskCompletionSource<int> sync) {
            Waiter.ClearValue();
            Waiter.SetCompletion(sync, i);
        }
    }


    class SendOp : Op {
        public SendOp(IChannel chan) : base(chan) { }

        internal override bool SendOrRecv() {
            Debug.Print("Thread {0}, {1} Blocking: seeing is channel of {2} is ready", Thread.CurrentThread.ManagedThreadId, GetType(), Chan.GetType());
            return Chan.Send(Waiter);
        }

        internal override bool TrySendOrRecv() {
            return Chan.TrySend(Waiter);
        }

        internal override void ResetWaiter(int i, TaskCompletionSource<int> sync) {
            Waiter.SetCompletion(sync, i);
        }
    }

    internal class NotSelected : ISelected {
        public static readonly NotSelected Instance = new NotSelected();

        public int Index => -1;

        public object Value {
            get { return null; }
            set { }
        }
    }

}

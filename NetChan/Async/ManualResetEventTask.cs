using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NetChan.Async
{
    internal interface IAsyncEvent
    {
        Task WaitAsync();
    }

    class AsyncManualResetEvent : IAsyncEvent
    {
        private TaskCompletionSource<bool> source = new TaskCompletionSource<bool>();

        public Task WaitAsync()
        {
            return source.Task;
        }

        public void Set()
        {
            source.TrySetResult(true);
        }

        public void Reset()
        {
            while (true)
            {
                var tcs = source;
                if (!tcs.Task.IsCompleted ||
                    Interlocked.CompareExchange(ref source, new TaskCompletionSource<bool>(), tcs) == tcs)
                    return;
            }
        }

        public void SetWithoutSynchronousContinuations()
        {
            var tcs = source;
            Task.Factory.StartNew(s => ((TaskCompletionSource<bool>)s).TrySetResult(true),
                tcs, CancellationToken.None, TaskCreationOptions.PreferFairness, TaskScheduler.Default);
            tcs.Task.Wait();
        }
    }

    class AsyncAutoResetEvent : IAsyncEvent
    {
        private static readonly Task complete = Task.FromResult(true);
        private readonly Queue<TaskCompletionSource<bool>> waiters = new Queue<TaskCompletionSource<bool>>();
        private bool signalled;

        public Task WaitAsync()
        {
            lock (waiters)
            {
                if (signalled)
                {
                    // already signalled, return immediately
                    signalled = false;
                    return complete;
                }
                // not signalled so we must wait in line
                var tcs = new TaskCompletionSource<bool>();
                waiters.Enqueue(tcs);
                return tcs.Task;
            }
        }

        public void Set()
        {
            TaskCompletionSource<bool> wakeup = null;
            lock (waiters)
            {
                if (waiters.Count > 0)
                    wakeup = waiters.Dequeue();
                else if (!signalled)
                    signalled = true;
            }
            if (wakeup != null)
                wakeup.SetResult(true);
        }
    }
}

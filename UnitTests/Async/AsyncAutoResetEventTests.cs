using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetChan.Async;
using NUnit.Framework;

namespace NetChan.Async
{
    [TestFixture]
    public class AsyncAutoResetEventTests
    {
        [Test]
        public void waitAsync_waits_until_set()
        {
            var evt = new AsyncAutoResetEvent();
            var task = evt.WaitAsync();
            Assert.IsFalse(task.IsCompleted);
            evt.Set();
            Assert.IsTrue(task.IsCompleted);
        }

        [Test]
        public void waitAsync_does_not_wait_when_event_already_set()
        {
            var evt = new AsyncAutoResetEvent();
            evt.Set();
            var task = evt.WaitAsync();
            Assert.IsTrue(task.IsCompleted);
            evt.Set();
        }

        [Test]
        public void set_signalls_the_first_waiter_only()
        {
            var evt = new AsyncAutoResetEvent();
            var t1 = evt.WaitAsync();
            var t2 = evt.WaitAsync();
            Assert.IsFalse(t1.IsCompleted);
            Assert.IsFalse(t2.IsCompleted);
            evt.Set();
            Assert.IsTrue(t1.IsCompleted);
            Assert.IsFalse(t2.IsCompleted);
        }

        [Test]
        public void set_when_already_signalled_does_nothing()
        {
            var evt = new AsyncAutoResetEvent();
            evt.Set();
            evt.Set();
            var t1 = evt.WaitAsync();
            var t2 = evt.WaitAsync();
            Assert.IsTrue(t1.IsCompleted);
            Assert.IsFalse(t2.IsCompleted);
        }

        [Test, Timeout(1000)]
        public void waitany_handles_only_one_event()
        {
            var evts = new [] { new AsyncAutoResetEvent(), new AsyncAutoResetEvent(), new AsyncAutoResetEvent()};
            Task<int> wait = AsyncAutoResetEvent.WaitAny(evts, new TaskCompletionSource<int>());
            evts[1].Set();
            evts[2].Set();
            evts[0].Set();
            Assert.AreEqual(1, wait.Result);

            for (int i = 0; i < evts.Length; i++) {
                if (i == wait.Result)
                    Assert.IsFalse(evts[i].WaitAsync().IsCompleted, $"expected task {i} to wait as it was set by waitany");
                else
                    Assert.IsTrue(evts[i].WaitAsync().IsCompleted, $"expected task {i} to complete immediately as it was set to signalled above");
            }
        }

        [Test, Timeout(1000)]
        public void waitany_handles_only_one_event_randomized()
        {
            var evts = new[] { new AsyncAutoResetEvent(), new AsyncAutoResetEvent() };
            Task<int> wait = AsyncAutoResetEvent.WaitAny(evts, new TaskCompletionSource<int>());
            var all = new Task[evts.Length];
            for (int i = 0; i < evts.Length; i++)
            {
                var e = evts[i];
                all[i] = Task.Run(() => e.Set());
            }
            int res = wait.Result; // blocks
            Task.WaitAll(all);

            for (int i = 0; i < evts.Length; i++) {
                if (i == res)
                    Assert.IsFalse(evts[i].WaitAsync().IsCompleted, $"expected task {i} to wait as it was set by waitany");
                else
                    Assert.IsTrue(evts[i].WaitAsync().IsCompleted, $"expected task {i} to complete immediately as it was set to signalled above");
            }
        }

        [Test, Timeout(1000)]
        public void waitany_handles_only_one_event_when_many_are_already_set()
        {
            var evts = new [] { new AsyncAutoResetEvent(), new AsyncAutoResetEvent(), new AsyncAutoResetEvent()};
            foreach (var e in evts) {
                e.Set();
            }

            Task<int> wait = AsyncAutoResetEvent.WaitAny(evts, new TaskCompletionSource<int>());
            Assert.AreEqual(0, wait.Result);

            for (int i = 0; i < evts.Length; i++)
            {
                if (i == wait.Result)
                    Assert.IsFalse(evts[i].WaitAsync().IsCompleted, $"expected task {i} to wait as it was set by waitany");
                else
                    Assert.IsTrue(evts[i].WaitAsync().IsCompleted, $"expected task {i} to complete immediately as it was set to signalled above");
            }
        }

        [Test, Timeout(1000)]
        public void wait_after_waitany()
        {
            var evts = new [] { new AsyncAutoResetEvent(), new AsyncAutoResetEvent(), new AsyncAutoResetEvent()};

            Task<int> task = AsyncAutoResetEvent.WaitAny(evts, new TaskCompletionSource<int>());
            evts[1].Set();
            Assert.AreEqual(1, task.Result);

            // check other events still work
            for (int i = 0; i < evts.Length; i++) {
                var t = evts[i].WaitAsync();
                Assert.IsFalse(t.IsCompleted, $"task {i} is already complete");
                evts[i].Set();
                Assert.IsTrue(t.IsCompleted);
            }
        }
    }
}

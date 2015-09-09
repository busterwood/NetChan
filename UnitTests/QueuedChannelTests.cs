using NUnit.Framework;
using System;
using System.Threading;
namespace NetChan {

    [TestFixture]
    public class QueuedChannelTests {

        [Test]
        public void trySend_returns_true_when_spare_capacity() {
            var ch = new QueuedChannel<bool>(1);
            Assert.IsTrue(ch.TrySend(true));
        }

        [Test]
        public void trySend_returns_false_when_capacity_reached() {
            var ch = new QueuedChannel<bool>(1);
            ch.TrySend(true);
            Assert.IsFalse(ch.TrySend(true));
        }

        [Test]
        public void tryRecv_returns_absent_value_if_no_senders() {
            var ch = new QueuedChannel<bool>(1);
            Assert.IsTrue(ch.TryRecv().IsNone);
        }

        [Test]
        public void recv_get_value_sent_by_thread_pool_thread() {
            var ch = new QueuedChannel<int>(1);
            ThreadPool.QueueUserWorkItem(state => ch.Send(123));
            Assert.AreEqual(Maybe<int>.Some(123), ch.Recv());
        }

        [Test]
        public void can_close_a_Channel() {
            var ch = new QueuedChannel<int>(1);
            ch.Close();
        }

        [Test, ExpectedException(typeof(ClosedChannelException))]
        public void cannot_send_on_a_closed_Channel() {
            var ch = new QueuedChannel<int>(1);
            ch.Close();
            ch.Send(123);
        }

        [Test]
        public void trysend_on_a_closed_Channel_returns_false() {
            var ch = new QueuedChannel<int>(1);
            ch.Close();
            Assert.IsFalse(ch.TrySend(123));
        }

        [Test]
        public void can_close_a_Channel_twice() {
            var ch = new QueuedChannel<int>(1);
            ch.Close();
            ch.Close();
        }

        [Test]
        public void recv_does_not_block_on_a_closed_Channel() {
            var ch = new QueuedChannel<int>(1);
            ch.Close();
            Assert.AreEqual(Maybe<int>.None(), ch.Recv());
        }

        [Test]
        public void tryrecv_does_not_block_on_a_closed_Channel() {
            var ch = new QueuedChannel<int>(1);
            ch.Close();
            Assert.AreEqual(Maybe<int>.None("closed"), ch.TryRecv());
        }


        [Test]
        public void can_recv_one_item_before_closing() {
            var ch = new QueuedChannel<int>(1);
            ThreadPool.QueueUserWorkItem(state => { ch.Send(123); ch.Close(); });
            Assert.AreEqual(Maybe<int>.Some(123), ch.Recv());
            Assert.AreEqual(Maybe<int>.None("closed"), ch.TryRecv());
        }

        [Test]
        public void can_enumerate_closed_channel() {
            var ch = new QueuedChannel<int>(1);
            ch.Close();
            var e = ch.GetEnumerator();
            Assert.IsFalse(e.MoveNext());
        }

        [Test]
        public void can_enumerate_single_item() {
            var ch = new QueuedChannel<int>(1);
            ThreadPool.QueueUserWorkItem(state => { ch.Send(123); ch.Close(); });
            var e = ch.GetEnumerator();
            Assert.IsTrue(e.MoveNext());
            Assert.AreEqual(123, e.Current);
            Assert.IsFalse(e.MoveNext());
        }

        [Test]
        public void can_enumerate_multiple_items() {
            var ch = new QueuedChannel<int>(1);
            ThreadPool.QueueUserWorkItem(state => { ch.Send(123); ch.Send(124); ch.Close(); });
            var e = ch.GetEnumerator();
            Assert.IsTrue(e.MoveNext());
            Assert.AreEqual(123, e.Current);
            Assert.IsTrue(e.MoveNext());
            Assert.AreEqual(124, e.Current);
            Assert.IsFalse(e.MoveNext(), "final move next:" + e.Current);
        }

        [Test]
        public void send_does_not_block_when_capacity_spare() {
            var ch = new QueuedChannel<bool>(1);
            var start = Environment.TickCount;
            ThreadPool.QueueUserWorkItem(state => { Thread.Sleep(50); ch.Recv(); });
            ch.Send(true);
            var elasped = Environment.TickCount - start;
            Assert.IsTrue(elasped < 50, "Elasped " + elasped);
        }

        [Test, Timeout(500)]
        public void send_blocks_when_capacity_reached_until_recv() {
            var ch = new QueuedChannel<bool>(1);
            var start = Environment.TickCount;
            ThreadPool.QueueUserWorkItem(state => { Thread.Sleep(50); ch.Recv(); Thread.Sleep(100); ch.Recv(); });
            ch.Send(true);
            ch.Send(true);
            var elasped = Environment.TickCount - start;
            Assert.IsTrue(elasped >= 40 && elasped < 100, "Elasped " + elasped);
        }

        [Test]
        public void z_benchmark_send_and_recieve() {
            const int runs = (int)5e5;
            var start = Environment.TickCount;
            var data = new QueuedChannel<int>(10);
            ThreadPool.QueueUserWorkItem(state => {
                for (int i = 0; i < runs; i++) {
                    data.Send(i);
                }
            });
            for (int i = 0; i < runs; i++) {
                Assert.AreEqual(Maybe<int>.Some(i), data.Recv());
            }
            var elasped = Environment.TickCount - start;
            var opsms = (float)runs / (float)elasped;
            Console.WriteLine("took {0}ms, {1:N1}op/ns, {2:N0}op/sec", elasped, opsms / 1000f, opsms * 1000);
        }
    }
}
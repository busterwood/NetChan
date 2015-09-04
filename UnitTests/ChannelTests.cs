using NUnit.Framework;
using System;
using System.Threading;

namespace NetChan {
    [TestFixture]
    public class ChannelTests {

        [Test]
        public void trySend_returns_false_if_no_recievers() {
            var ch = new Channel<bool>();
            Assert.IsFalse(ch.TrySend(true));
        }

        [Test]
        public void send_blocks_until_recv() {
            var ch = new Channel<bool>();
            var start = Environment.TickCount;
            ThreadPool.QueueUserWorkItem(state => { Thread.Sleep(123); ch.Recv(); });
            ch.Send(true);
            var elasped = Environment.TickCount - start;
            Assert.IsTrue(elasped > 100, "Elasped " + elasped);
        }

        [Test]
        public void recv_blocks_until_send() {
            var ch = new Channel<bool>();
            var start = Environment.TickCount;
            ThreadPool.QueueUserWorkItem(state => { Thread.Sleep(123); ch.Send(true); });
            ch.Recv();
            var elasped = Environment.TickCount - start;
            Assert.IsTrue(elasped > 100, "Elasped " + elasped);
        }

        [Test, Timeout(100)]
        public void tryRecv_returns_absent_value_if_no_senders() {
            var ch = new Channel<bool>();
            Assert.IsTrue(ch.TryRecv().Absent);
        }

        [Test, Timeout(100)]
        public void recv_get_value_sent_by_thread_pool_thread() {
            var ch = new Channel<int>();
            ThreadPool.QueueUserWorkItem(state => ch.Send(123));
            Assert.AreEqual(123, ch.Recv());
        }

        [Test]
        public void can_close_a_Channel() {
            var ch = new Channel<int>();
            ch.Close();
        }

        [Test, ExpectedException(typeof(ClosedChannelException)), Timeout(100)]
        public void cannot_send_on_a_closed_Channel() {
            var ch = new Channel<int>();
            ch.Close();
            ch.Send(123);
        }

        [Test, Timeout(100)]
        public void trysend_on_a_closed_Channel_returns_false() {
            var ch = new Channel<int>();
            ch.Close();
            Assert.IsFalse(ch.TrySend(123));
        }

        [Test, Timeout(100)]
        public void can_close_a_Channel_twice() {
            var ch = new Channel<int>();
            ch.Close();
            ch.Close();
        }

        [Test, Timeout(100)]
        public void recv_does_not_block_on_a_closed_Channel() {
            var ch = new Channel<int>();
            ch.Close();
            Assert.AreEqual(0, ch.Recv());
        }

        [Test, Timeout(100)]
        public void tryrecv_does_not_block_on_a_closed_Channel() {
            var ch = new Channel<int>();
            ch.Close();
            Assert.AreEqual(Maybe<int>.None("closed"), ch.TryRecv());
        }


        [Test, Timeout(100)]
        public void can_recv_one_item_before_closing() {
            var ch = new Channel<int>();
            ThreadPool.QueueUserWorkItem(state => { ch.Send(123); ch.Close(); });
            Assert.AreEqual(123, ch.Recv());
            Assert.AreEqual(Maybe<int>.None("closed"), ch.TryRecv());
        }

        [Test, Timeout(100)]
        public void can_enumerate_closed_channel() {
            var ch = new Channel<int>();
            ch.Close();
            var e = ch.GetEnumerator();
            Assert.IsFalse(e.MoveNext());
        }

        [Test]
        public void can_enumerate_single_item() {
            var ch = new Channel<int>();
            ThreadPool.QueueUserWorkItem(state => { ch.Send(123); ch.Close(); });
            var e = ch.GetEnumerator();
            Assert.IsTrue(e.MoveNext());
            Assert.AreEqual(123, e.Current);
            Assert.IsFalse(e.MoveNext());
        }

        [Test]
        public void can_enumerate_multiple_items() {
            var ch = new Channel<int>();
            ThreadPool.QueueUserWorkItem(state => { ch.Send(123); ch.Send(124); ch.Close(); });
            var e = ch.GetEnumerator();
            Assert.IsTrue(e.MoveNext());
            Assert.AreEqual(123, e.Current);
            Assert.IsTrue(e.MoveNext());
            Assert.AreEqual(124, e.Current);
            Assert.IsFalse(e.MoveNext(), "final move next:" + e.Current);
        }

        [Test]
        public void read_from_multiple_Channels() {
            var data = new Channel<int>();
            var quit = new Channel<bool>();
            ThreadPool.QueueUserWorkItem(state => data.Send(123));
            var select = new Select(data, quit);
            select.Recv();
            Assert.AreEqual(0, select.Index);
            Assert.AreEqual(123, select.Value);
        }

        [Test]
        public void read_second_of_multiple_Channels() {
            var data = new Channel<int>();
            var quit = new Channel<bool>();
            ThreadPool.QueueUserWorkItem(state => quit.Send(true));
            var select = new Select(data, quit);
            select.Recv();
            Assert.AreEqual(1, select.Index);
            Assert.AreEqual(true, select.Value);
        }

        [Test]
        public void z_benchmark_send_and_recieve() {
            const int runs = (int)1e5;
            var start = Environment.TickCount;
            var data = new Channel<int>();
            ThreadPool.QueueUserWorkItem(state => {
                for (int i = 0; i < runs; i++) {
                    data.Send(i);
                }
            });
            for (int i = 0; i < runs; i++) {
                Assert.AreEqual(i, data.Recv());
            }
            var elasped = Environment.TickCount - start;
            var opsms = (float)runs / (float)elasped;
            Console.WriteLine("took {0}ms, {1:N1}op/ms, {2:N0}op/sec", elasped, opsms, opsms * 1000);
        }
    }
}
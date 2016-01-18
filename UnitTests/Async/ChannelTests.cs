using System.Diagnostics;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NetChan.Async {
    [TestFixture]
    public class ChannelTests {

        [Test]
        public void trySend_returns_false_if_no_receivers() {
            var ch = new Channel<bool>();
            Assert.IsFalse(ch.TrySend(true));
        }

        [Test]
        public void send_blocks_until_recv() {
            var ch = new Channel<bool>();
            var start = Environment.TickCount;
            Task.Delay(123).ContinueWith(a => ch.Recv());
            ch.Send(true).Wait();
            var elapsed = Environment.TickCount - start;
            Assert.IsTrue(elapsed > 100, "Elapsed " + elapsed);
        }

        [Test]
        public void recv_blocks_until_send() {
            var ch = new Channel<bool>(0);
            var start = Environment.TickCount;
            Task.Delay(123).ContinueWith(a => ch.Send(true));
            ch.Recv().Wait();
            var elapsed = Environment.TickCount - start;
            Assert.IsTrue(elapsed > 100, "Elapsed " + elapsed);
        }

        [Test, Timeout(100)]
        public void tryRecv_returns_absent_value_if_no_senders() {
            var ch = new Channel<bool>();
            Assert.IsTrue(ch.TryRecv().IsNone);
        }

        //[Test, Timeout(100)]
        [Test]
        public void recv_get_value_sent_by_thread_pool_thread() {
            var ch = new Channel<int>(0);
            ch.Send(123);
            Assert.AreEqual(Maybe<int>.Some(123), ch.Recv().Result);
        }

        [Test]
        public void can_close_a_Channel() {
            var ch = new Channel<int>();
            ch.Close();
        }

        [Test, ExpectedException(typeof(ClosedChannelException)), Timeout(100)]
        public async void cannot_send_on_a_closed_Channel() {
            var ch = new Channel<int>();
            ch.Close();
            await ch.Send(123);
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
            Assert.AreEqual(Maybe<int>.None(), ch.Recv().Result);
        }

        [Test, Timeout(500)]
        public void tryrecv_does_not_block_on_a_closed_Channel() {
            var ch = new Channel<int>();
            ch.Close();
            Assert.AreEqual(Maybe<int>.None(), ch.TryRecv());
        }

        [Test, Timeout(100)]
        public void can_recv_one_item_before_closing() {
            var ch = new Channel<int>();
            ch.Send(123).ContinueWith(_ => ch.Close());           
            Assert.AreEqual(Maybe<int>.Some(123), ch.Recv().Result);
            Assert.AreEqual(Maybe<int>.None(), ch.TryRecv());
        }

        //[Test, Timeout(100)]
        //public void can_enumerate_closed_channel() {
        //    var ch = new Channel<int>();
        //    ch.Close();
        //    var e = ch.GetEnumerator();
        //    Assert.IsFalse(e.MoveNext());
        //}

        //[Test]
        //public void can_enumerate_single_item() {
        //    var ch = new Channel<int>();
        //    ThreadPool.QueueUserWorkItem(state => { ch.Send(123); ch.Close(); });
        //    var e = ch.GetEnumerator();
        //    Assert.IsTrue(e.MoveNext());
        //    Assert.AreEqual(123, e.Current);
        //    Assert.IsFalse(e.MoveNext());
        //}

        //[Test]
        //public void can_enumerate_multiple_items() {
        //    var ch = new Channel<int>();
        //    ThreadPool.QueueUserWorkItem(state => { ch.Send(123); ch.Send(124); ch.Close(); });
        //    var e = ch.GetEnumerator();
        //    Assert.IsTrue(e.MoveNext());
        //    Assert.AreEqual(123, e.Current);
        //    Assert.IsTrue(e.MoveNext());
        //    Assert.AreEqual(124, e.Current);
        //    Assert.IsFalse(e.MoveNext(), "final move next:" + e.Current);
        //}

        //[Test]
        //public void read_from_multiple_Channels() {
        //    var data = new Channel<int>();
        //    var quit = new Channel<bool>();
        //    ThreadPool.QueueUserWorkItem(state => data.Send(123));
        //    var select = new Channels(Op.Recv(data), Op.Recv(quit));
        //    var got = select.Select();
        //    Assert.AreEqual(0, got.Index);
        //    Assert.AreEqual(123, got.Value);
        //}

        //[Test]
        //public void read_second_of_multiple_Channels() {
        //    var data = new Channel<int>();
        //    var quit = new Channel<bool>();
        //    ThreadPool.QueueUserWorkItem(state => quit.Send(true));
        //    var select = new Channels(Op.Recv(data), Op.Recv(quit));
        //    var got = select.Select();
        //    Assert.AreEqual(1, got.Index);
        //    Assert.AreEqual(true, got.Value);
        //}

        [Test, Timeout(5000)]
        public void z_benchmark_send_and_recieve() {
            Benchmark.Go("unbuffered", (int runs) =>
            {
                var data = new Channel<int>();
                Task.Run(() => {
                    for (int i = 0; i < runs; i++) {
                        data.Send(i);
                    }
                });
                for (int i = 0; i < runs; i++) {
                    var got = data.Recv().Result;
                    if (got.IsNone || got.Value != i) {
                        Assert.AreEqual(Maybe<int>.Some(i), data.Recv());
                    }
                }
            });
        }

    }
}
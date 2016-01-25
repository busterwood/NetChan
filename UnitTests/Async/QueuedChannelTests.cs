using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NetChan.Async {

    [TestFixture]
    public class QueuedChannelTests {

        [Test]
        public void trySend_returns_true_when_spare_capacity() {
            var ch = new Channel<bool>(1);
            Assert.IsTrue(ch.TrySend(true));
        }

        [Test]
        public void trySend_returns_false_when_capacity_reached() {
            var ch = new Channel<bool>(1);
            ch.TrySend(true);
            Assert.IsFalse(ch.TrySend(true));
        }

        [Test]
        public void tryRecv_returns_absent_value_if_no_senders() {
            var ch = new Channel<bool>(1);
            Assert.IsTrue(ch.TryRecv().IsNone);
        }

        [Test]
        public void recv_get_value_sent_by_thread_pool_thread() {
            var ch = new Channel<int>(1);
            ch.Send(123);
            Assert.AreEqual(Maybe<int>.Some(123), ch.Recv().Result);
        }

        [Test]
        public void can_close_a_Channel() {
            var ch = new Channel<int>(1);
            ch.Close();
        }

        [Test, ExpectedException(typeof(ClosedChannelException))]
        public void cannot_send_on_a_closed_Channel() {
            var ch = new Channel<int>(1);
            ch.Close();
            try
            {
                ch.Send(123).Wait();
            }
            catch (AggregateException ae)
            {
                throw ae.InnerException;
            }
        }

        [Test]
        public void trysend_on_a_closed_Channel_returns_false() {
            var ch = new Channel<int>(1);
            ch.Close();
            Assert.IsFalse(ch.TrySend(123));
        }

        [Test]
        public void can_close_a_Channel_twice() {
            var ch = new Channel<int>(1);
            ch.Close();
            ch.Close();
        }

        [Test]
        public void recv_does_not_block_on_a_closed_Channel() {
            var ch = new Channel<int>(1);
            ch.Close();
            Assert.AreEqual(Maybe<int>.None(), ch.Recv().Result);
        }

        [Test]
        public void tryrecv_does_not_block_on_a_closed_Channel() {
            var ch = new Channel<int>(1);
            ch.Close();
            Assert.AreEqual(Maybe<int>.None(), ch.TryRecv());
        }


        [Test]
        public void can_recv_one_item_before_closing() {
            var ch = new Channel<int>(1);
            ch.Send(123).ContinueWith(_ => ch.Close());
            Assert.AreEqual(Maybe<int>.Some(123), ch.Recv().Result);
            Assert.AreEqual(Maybe<int>.None(), ch.TryRecv());
        }

        //[Test]
        //public void can_enumerate_closed_channel() {
        //    var ch = new Channel<int>(1);
        //    ch.Close();
        //    var e = ch.GetEnumerator();
        //    Assert.IsFalse(e.MoveNext());
        //}

        //[Test]
        //public void can_enumerate_single_item() {
        //    var ch = new Channel<int>(1);
        //    ThreadPool.QueueUserWorkItem(state => { ch.Send(123); ch.Close(); });
        //    var e = ch.GetEnumerator();
        //    Assert.IsTrue(e.MoveNext());
        //    Assert.AreEqual(123, e.Current);
        //    Assert.IsFalse(e.MoveNext());
        //}

        //[Test]
        //public void can_enumerate_multiple_items() {
        //    var ch = new Channel<int>(1);
        //    ThreadPool.QueueUserWorkItem(state => { ch.Send(123); ch.Send(124); ch.Close(); });
        //    var e = ch.GetEnumerator();
        //    Assert.IsTrue(e.MoveNext());
        //    Assert.AreEqual(123, e.Current);
        //    Assert.IsTrue(e.MoveNext());
        //    Assert.AreEqual(124, e.Current);
        //    Assert.IsFalse(e.MoveNext(), "final move next:" + e.Current);
        //}

        [Test]
        public void send_does_not_block_when_capacity_spare() {
            var ch = new Channel<bool>(1);
            var start = Environment.TickCount;
            Task.Delay(50).ContinueWith(_ => ch.Recv());
            ch.Send(true).Wait();
            var elapsed = Environment.TickCount - start;
            Assert.IsTrue(elapsed < 50, "Elapsed " + elapsed);
        }

        [Test, Timeout(500)]
        public void send_blocks_when_capacity_reached_until_recv() {
            var ch = new Channel<bool>(1);
            var start = Environment.TickCount;
#pragma warning disable 4014
            ReceiveTwiceWithWithDelay(ch);
#pragma warning restore 4014
            ch.Send(true).Wait();
            ch.Send(true).Wait();
            var elapsed = Environment.TickCount - start;
            Assert.IsTrue(elapsed >= 40 && elapsed < 100, "Elapsed " + elapsed);
        }

        private async Task ReceiveTwiceWithWithDelay(Channel<bool> ch)
        {
            await Task.Delay(50);
            await ch.Recv();
            await Task.Delay(100);
            await ch.Recv();
        }

        [Test, Timeout(3000)]
        public void z10_benchmark_send_and_receive() {
            Benchmark.Go("queue size 10", (int runs) => {
                var data = new Channel<int>(100);
                Task.Run(() => {
                    for (int i = 0; i < runs; i++) {
#pragma warning disable 4014
                        data.Send(i);
#pragma warning restore 4014
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
        
        [Test, Timeout(3000)]
        public void z100_benchmark_send_and_receive() {
            Benchmark.Go("queue size 100", (int runs) => {
                var data = new Channel<int>(100);
                Task.Run(() => {
                    for (int i = 0; i < runs; i++) {
#pragma warning disable 4014
                        data.Send(i);
#pragma warning restore 4014
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

        [Test, Timeout(4000)]
        public void z1000_benchmark_send_and_receive() {
            Benchmark.Go("queue size 1000", (int runs) => {
                var data = new Channel<int>(1000);
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
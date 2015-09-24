using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Threading;

namespace NetChan {

    [TestFixture]
    public class SelectTests {
        [Test]
        public void select_can_receive_on_unbuffered_channel() {
            var ch1 = new Channel<int>();
            ThreadPool.QueueUserWorkItem(state => ch1.Send(123));
            using (var select = new Channels(Op.Recv(ch1))) {
                var got = select.Select();
                Assert.AreEqual(0, got.Index, "select.Index");
                Assert.AreEqual(123, got.Value, "select.Value");
            }
        }

        [Test]
        public void select_can_receive_on_buffered_channel() {
            var ch1 = new Channel<int>(1);
            ch1.Send(123);
            using (var select = new Channels(Op.Recv(ch1))) {
                var got = select.Select();
                Assert.AreEqual(0, got.Index, "select.Index");
                Assert.AreEqual(123, got.Value, "select.Value");
            }
        }

        [Test, Timeout(1000)]
        public void select_can_send_on_unbuffered_channel() {
            var ch1 = new Channel<int>();
            var done = new AutoResetEvent(false);
            Maybe<int> recvd = Maybe<int>.None();
            ThreadPool.QueueUserWorkItem(state => {
                recvd = ch1.Recv();
                done.Set();
            });
            using (var chans = new Channels(Op.Send(ch1))) {
                chans[0].Value = 123;
                var got = chans.Select();
                Assert.AreEqual(0, got.Index, "select.Index");
            }
            done.WaitOne();
            Assert.AreEqual(Maybe<int>.Some(123), recvd);
        }

        [Test, Timeout(1000)]
        public void select_can_send_on_buffered_channel() {
            var ch1 = new Channel<int>(1);
            using (var chans = new Channels(Op.Send(ch1))) {
                chans[0].Value = 123;
                var got = chans.Select();
                Assert.AreEqual(0, got.Index, "select.Index");
            }
            Assert.AreEqual(Maybe<int>.Some(123), ch1.Recv());
        }

        [Test]
        public void can_read_after_select() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            ThreadPool.QueueUserWorkItem(state => {
                ch1.Send(123);
                ch2.Send(true);
                ch1.Send(124);
            });
            using (var select = new Channels(Op.Recv(ch1), Op.Recv(ch2))) {
                var got = select.Select();
                Assert.AreEqual(0, got.Index, "select.Index");
                Assert.AreEqual(123, got.Value, "select.Value");
                Assert.AreEqual(Maybe<bool>.Some(true), ch2.Recv());
                Assert.AreEqual(Maybe<int>.Some(124), ch1.Recv());                
            }
        }

        [Test, Timeout(500)]
        public void can_select_on_open_and_closed_Channels() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            ThreadPool.QueueUserWorkItem(state => {
                ch1.Close();
                ch2.Send(true);
            });
            using (var select = new Channels(Op.Recv(ch1), Op.Recv(ch2))) {                
                var gotTrue = false;
                while (!gotTrue) {
                    var got = select.Select();
                    if (got.Index == 1) {
                        Assert.AreEqual(true, got.Value);
                        Assert.AreEqual(false, gotTrue);
                        gotTrue = true;
                    } else {
                        Assert.AreEqual(0, got.Index, "select.Index");
                        Assert.AreEqual(null, got.Value, "select.Value");
                    }
                }
            }
        }

        [Test]
        public void can_select_on_Channels_closed_by_another_thread() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            ThreadPool.QueueUserWorkItem(state => {
                ch1.Close();
                ch2.Close();
            });
            using (var select = new Channels(Op.Recv(ch1), Op.Recv(ch2))) {
                var got = select.Select();
                Assert.AreNotEqual(-1, got.Index, "expected any channel to return");
                Assert.AreEqual(null, got.Value);                
            }
        }

        [Test]
        public void can_select_on_all_closed_Channels() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            ch1.Close();
            ch2.Close();
            using (var select = new Channels(Op.Recv(ch1), Op.Recv(ch2))) {
                var got = select.Select();
                Assert.AreNotEqual(-1, got.Index, "expected any channel to return");
                Assert.AreEqual(null, got.Value);
            }
        }

        [Test]
        public void TrySelect_on_all_closed_Channels_return_any_channel() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            ch1.Close();
            ch2.Close();
            using (var select = new Channels(Op.Recv(ch1), Op.Recv(ch2))) {
                var got = select.TrySelect();
                Assert.AreNotEqual(-1, got.Index, "index");
            }
        }

        [Test]
        public void TrySelect_returns_minus_one_if_no_channels_are_ready() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>(1);
            using (var select = new Channels(Op.Recv(ch1), Op.Recv(ch2))) {
                var got = select.TrySelect();
                Assert.AreEqual(-1, got.Index, "index");
            }
        }

        [Test]
        public void TrySelect_returns_if_one_channel_is_ready_to_read() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>(1);
            ThreadPool.QueueUserWorkItem(state => ch1.Send(123));
            Thread.Sleep(50);
            using (var select = new Channels(Op.Recv(ch1), Op.Recv(ch2))) {
                var got = select.TrySelect();
                Assert.AreEqual(0, got.Index, "index");
            }
        }

        [Test]
        public void tryrecv_returns_if_the_other_channel_is_ready() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>(1);
            var sent = new AutoResetEvent(false);
            ThreadPool.QueueUserWorkItem(state => { ch2.Send(true); sent.Set(); });
            sent.WaitOne();
            using (var select = new Channels(Op.Recv(ch1), Op.Recv(ch2))) {
                var got = select.TrySelect();
                Assert.AreEqual(1, got.Index, "index");
            }
        }

        [Test]
        public void can_read_after_select_on_queued_Channels() {
            var ch1 = new Channel<int>(1);
            var ch2 = new Channel<bool>(1);
            ThreadPool.QueueUserWorkItem(state => {
                ch1.Send(123);
                ch2.Send(true);
                ch2.Close();
                ch1.Send(124);
                ch1.Close();
            });
            using (var select = new Channels(Op.Recv(ch1), Op.Recv(ch2))) {
                var got = select.Select();
                Debug.Print("got.Index = " + got.Index);
                if (got.Index == 0) {
                    Assert.AreEqual(123, got.Value, "got.Value");
                    Assert.AreEqual(Maybe<bool>.Some(true), ch2.Recv());
                }
                else {
                    Assert.AreEqual(1, got.Index, "got.Index");
                    Assert.AreEqual(true, got.Value, "got.Value");
                    Assert.AreEqual(Maybe<int>.Some(123), ch1.Recv());
                }
                select.ClearAt(1);
                got = select.Select();
                Assert.AreEqual(0, got.Index, "got.Index, value =" + got.Value);
                Assert.AreEqual(124, got.Value, "got.Value");
            }
        }

        [Test, Timeout(5000)]
        public void z0_send_and_select_many_items_from_channel() {
            Benchmark.Go("unbuffered select", (int runs) => {
                var data = new Channel<int>();
                ThreadPool.QueueUserWorkItem((state) => {
                    for (int i = 0; i < runs; i++) {
                        data.Send(i);
                    }
                });
                using (var select = new Channels(Op.Recv(data))) {
                    for (int i = 0; i < runs; i++) {
                        ISelected got = select.Select();
                        if (got.Index != 0) {
                            Assert.AreEqual(0, got.Index);
                        }
                        var gotInt = (ISelected<int>) got;
                        if (gotInt.Value.Value != i) {
                            Assert.AreEqual(Maybe<int>.Some(i), got.Value);
                        }
                    }
                }
            });
        }

        [Test, Timeout(3000)]
        public void z1_send_and_select_many_items_from_queued_channel() {
            Benchmark.Go("select on buffer of 1", (int runs) => {
                var data = new Channel<int>(10);
                ThreadPool.QueueUserWorkItem((state) => {
                    for (int i = 0; i < runs; i++) {
                        data.Send(i);
                    }
                });
                using (var select = new Channels(Op.Recv(data))) {
                    for (int i = 0; i < runs; i++) {
                        ISelected got = select.Select();
                        if (got.Index != 0) {
                            Assert.AreEqual(0, got.Index);
                        }
                        var gotInt = (ISelected<int>)got;
                        if (gotInt.Value.Value != i) {
                            Assert.AreEqual(Maybe<int>.Some(i), got.Value);
                        }
                    }
                }
            });
        }

        [Test, Timeout(5000)]
        public void z10_send_and_select_many_items_from_queued_channel() {
            Benchmark.Go("select on buffer of 10", (int runs) => {
                var data = new Channel<int>(10);
                ThreadPool.QueueUserWorkItem((state) => {
                    for (int i = 0; i < runs; i++) {
                        data.Send(i);
                    }
                });
                using (var select = new Channels(Op.Recv(data))) {
                    for (int i = 0; i < runs; i++) {
                        var got = select.Select();
                        if (got.Index != 0) {
                            Assert.AreEqual(0, got.Index);
                        }
                        var gotInt = (ISelected<int>)got;
                        if (gotInt.Value.Value != i) {
                            Assert.AreEqual(Maybe<int>.Some(i), got.Value);
                        }
                    }
                }
            });
        }

        [Test, Timeout(3000)]
        public void z100_send_and_select_many_items_from_queued_channel() {
            Benchmark.Go("select on buffer of 100", (int runs) => {
                var data = new Channel<int>(100);
                ThreadPool.QueueUserWorkItem((state) => {
                    for (int i = 0; i < runs; i++) {
                        data.Send(i);
                    }
                });
                using (var select = new Channels(Op.Recv(data))) {
                    for (int i = 0; i < runs; i++) {
                        var got = select.Select();
                        if (got.Index != 0) {
                            Assert.AreEqual(0, got.Index);
                        }
                        var gotInt = (ISelected<int>)got;
                        if (gotInt.Value.Value != i) {
                            Assert.AreEqual(Maybe<int>.Some(i), got.Value);
                        }
                    }
                }
            });
        }

        [Test, Timeout(3000)]
        public void z1000_send_and_select_many_items_from_queued_channel() {
            Benchmark.Go("select on buffer of 1000", (int runs) => {
                var data = new Channel<int>(1000);
                ThreadPool.QueueUserWorkItem((state) => {
                    for (int i = 0; i < runs; i++) {
                        data.Send(i);
                    }
                });
                using (var select = new Channels(Op.Recv(data))) {
                    for (int i = 0; i < runs; i++) {
                        var got = select.Select();
                        if (got.Index != 0) {
                            Assert.AreEqual(0, got.Index);
                        }
                        var gotInt = (ISelected<int>)got;
                        if (gotInt.Value.Value != i) {
                            Assert.AreEqual(Maybe<int>.Some(i), got.Value);
                        }
                    }
                }
            });
        }
        [Test, Timeout(5000)]
        public void z_2select_on_two_channels() {
            Benchmark.Go("select on two channels", (int runs) => {
                var ch1 = new Channel<int>(100);
                var ch2 = new Channel<int>(100);
                ThreadPool.QueueUserWorkItem((state) => {
                    for (int i = 0; i < runs; i++) {
                        ch1.Send(i);
                    }
                });
                ThreadPool.QueueUserWorkItem((state) => {
                    for (int i = 0; i < runs; i++) {
                        ch2.Send(i);
                    }
                });
                int sum = 0;
                int count = 0;
                using (var select = new Channels(Op.Recv(ch1), Op.Recv(ch2))) {
                    int max = runs*2;
                    for (int i = 0; i < max; i++) {
                        count++;
                        var got = select.Select();
                        if (got.Index < 0) {
                            Assert.IsTrue(got.Index >= 0);
                        }
                        var gotInt = (ISelected<int>)got;
                        Maybe<int> val = gotInt.Value;
                        if (val.IsNone) {
                            Assert.AreNotEqual(Maybe<int>.None(), got.Value);
                        }
                        sum += val.Value;
                    }
                }
            });
        }

        [Test]
        public void select_on_closed_channel_does_not_block_and_returns_no_value() {
            var ch1 = new Channel<int>(1);
            ch1.Close();
            using (var select = new Channels(Op.Recv(ch1))) {
                var got = select.Select();
                Assert.AreEqual(0, got.Index);
                Assert.AreEqual(null, got.Value);
            }
        }

        [Test]
        public void select_on_one_null_channel() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>(2);
            ch2.Send(true);
            using (var select = new Channels(Op.Recv(ch1), Op.Recv(ch2))) {
                select.ClearAt(0);
                var got = select.Select();
                Assert.AreEqual(1, got.Index, "expected any channel to return");
                Assert.AreEqual(true, got.Value);
            }
        }

    }
}
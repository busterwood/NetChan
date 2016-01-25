using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NetChan.Async {

    [TestFixture]
    public class SelectTests {
        [Test]
        public void select_can_receive_on_unbuffered_channel() {
            var ch1 = new Channel<int>();
            ch1.Send(123);
            var select = new Channels(Op.Recv(ch1));
            var got = select.Select().Result;
            Assert.AreEqual(0, got.Index, "select.Index");
            Assert.AreEqual(123, got.Value, "select.Value");
        }

        [Test]
        public void select_can_receive_on_buffered_channel() {
            var ch1 = new Channel<int>(1);
            ch1.Send(123);
            var select = new Channels(Op.Recv(ch1));
            var got = select.Select().Result;
            Assert.AreEqual(0, got.Index, "select.Index");
            Assert.AreEqual(123, got.Value, "select.Value");
        }

        [Test, Timeout(1000)]
        public void select_can_send_on_unbuffered_channel() {
            var ch1 = new Channel<int>();
            var recvd = ch1.Recv();
            var chans = new Channels(Op.Send(ch1));
            chans[0].Value = 123;
            var got = chans.Select().Result;
            Assert.AreEqual(0, got.Index, "select.Index");
            recvd.Wait();
            Assert.AreEqual(Maybe<int>.Some(123), recvd.Result);
        }

        [Test, Timeout(1000)]
        public void select_can_send_on_buffered_channel() {
            var ch1 = new Channel<int>(1);
            var chans = new Channels(Op.Send(ch1));
            chans[0].Value = 123;
            var got = chans.Select().Result;
            Assert.AreEqual(0, got.Index, "select.Index");
            Assert.AreEqual(Maybe<int>.Some(123), ch1.Recv().Result);
        }

        //[Timeout(500)]
        [Test]
        public void can_read_after_select() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            can_read_after_select_script(ch1, ch2);
            var select = new Channels(Op.Recv(ch1), Op.Recv(ch2));
            var got = select.Select().Result;
            Assert.AreEqual(0, got.Index, "select.Index");
            Assert.AreEqual(123, got.Value, "select.Value");
            Assert.AreEqual(Maybe<bool>.Some(true), ch2.Recv().Result);
            Assert.AreEqual(Maybe<int>.Some(124), ch1.Recv().Result);                
        }

        private static async Task can_read_after_select_script(Channel<int> ch1, Channel<bool> ch2)
        {
            await ch1.Send(123);
            await ch2.Send(true);
            await ch1.Send(124);
        }

        [Test]
        [Timeout(500)]
        public void shuffle_two_items_can_change_order()
        {
            var idx = new[] {0, 1};
            var select = new Channels();
            var counts = new int[idx.Length];
            for (int i = 0; i < 1000; i++)
            {
                idx[0] = 0;
                idx[1] = 1;
                select.Shuffle(idx);
                if (idx[0] == 0) {
                    Assert.AreEqual(1, idx[1]);
                    counts[0]++;
                }
                else {
                    Assert.AreEqual(0, idx[1]);
                    counts[1]++;
                }
            }
            Assert.AreNotEqual(0, counts[0], "idx[0] == 0");
            Assert.AreNotEqual(0, counts[1], "idx[0] == 1");
        }

        [Test]
        [Timeout(1000)]
        public void can_select_on_open_and_closed_Channels() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            Task.Run(() =>
            {
                ch1.Close();
                ch2.Send(true).Wait();
            });
            var select = new Channels(Op.Recv(ch1), Op.Recv(ch2));
            var gotTrue = false;
            while (!gotTrue) {
                var got = select.Select().Result;
                Thread.Sleep(0); // yield otherwise this thread spins too quickly
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

        [Test]
        [Timeout(500)]
        public void can_select_on_Channels_closed_by_another_thread() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            Task.Run(() => {
                ch1.Close();
                ch2.Close();
            });
            var select = new Channels(Op.Recv(ch1), Op.Recv(ch2));
            var got = select.Select().Result;
            Assert.AreNotEqual(-1, got.Index, "expected any channel to return");
            Assert.AreEqual(null, got.Value);                
        }

        [Test]
        public void can_select_on_all_closed_Channels() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            ch1.Close();
            ch2.Close();
            var select = new Channels(Op.Recv(ch1), Op.Recv(ch2));
            var got = select.Select().Result;
            Assert.AreNotEqual(-1, got.Index, "expected any channel to return");
            Assert.AreEqual(null, got.Value);
        }

        [Test]
        public void TrySelect_on_all_closed_Channels_return_any_channel() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            ch1.Close();
            ch2.Close();
            var select = new Channels(Op.Recv(ch1), Op.Recv(ch2));
            var got = select.TrySelect();
            Assert.AreNotEqual(-1, got.Index, "index");
        }

        [Test]
        public void TrySelect_returns_minus_one_if_no_channels_are_ready() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>(1);
            var select = new Channels(Op.Recv(ch1), Op.Recv(ch2));
            var got = select.TrySelect();
            Assert.AreEqual(-1, got.Index, "index");
        }

        [Test]
        public void TrySelect_returns_if_one_channel_is_ready_to_read() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>(1);
            ch1.Send(123);
            Thread.Sleep(50);
            var select = new Channels(Op.Recv(ch1), Op.Recv(ch2));
            var got = select.TrySelect();
            Assert.AreEqual(0, got.Index, "index");
        }

        [Test]
        public void tryrecv_returns_if_the_other_channel_is_ready() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>(1);
            ch2.Send(true).Wait();
            var select = new Channels(Op.Recv(ch1), Op.Recv(ch2));
            var got = select.TrySelect();
            Assert.AreEqual(1, got.Index, "index");
        }

        [Test]
        public void can_read_after_select_on_queued_Channels() {
            var ch1 = new Channel<int>(1);
            var ch2 = new Channel<bool>(1);
            can_read_after_select_on_queued_Channels_Script(ch1, ch2);
            var select = new Channels(Op.Recv(ch1), Op.Recv(ch2));
            var got = select.Select().Result;
            Debug.Print("got.Index = " + got.Index);
            if (got.Index == 0) {
                Assert.AreEqual(123, got.Value, "got.Value");
                Assert.AreEqual(Maybe<bool>.Some(true), ch2.Recv().Result);
            }
            else {
                Assert.AreEqual(1, got.Index, "got.Index");
                Assert.AreEqual(true, got.Value, "got.Value");
                Assert.AreEqual(Maybe<int>.Some(123), ch1.Recv().Result);
            }
            select.ClearAt(1);
            got = select.Select().Result;
            Assert.AreEqual(0, got.Index, "got.Index, value =" + got.Value);
            Assert.AreEqual(124, got.Value, "got.Value");
        }

        private async Task can_read_after_select_on_queued_Channels_Script(Channel<int> ch1, Channel<bool> ch2)
        {            
            await ch1.Send(123);
            await ch2.Send(true);
            ch2.Close();
            await ch1.Send(124);
            ch1.Close();
        }

        [Test, Timeout(10000)]
        public void z0_send_and_select_many_items_from_channel() {
            Benchmark.Go("unbuffered select", (int runs) => {
                var data = new Channel<int>();
                Task.Run(() => {
                    for (int i = 0; i < runs; i++) {
                        data.Send(i);
                    }
                });
                var select = new Channels(Op.Recv(data));
                for (int i = 0; i < runs; i++) {
                    ISelected got = select.Select().Result;
                    if (got.Index != 0) {
                        Assert.AreEqual(0, got.Index, "index");
                    }
                    var gotInt = (ISelected<int>) got;
                    if (gotInt.Value.Value != i) {
                        Assert.AreEqual(Maybe<int>.Some(i), got.Value, $"value of item {i}");
                    }
                }
            });
        }

        [Test, Timeout(10000)]
        public void z1_send_and_select_many_items_from_queued_channel() {
            Benchmark.Go("select on buffer of 1", (int runs) => {
                var data = new Channel<int>(10);
                Task.Run(() => {
                    for (int i = 0; i < runs; i++) {
                        data.Send(i);
                    }
                });
                var select = new Channels(Op.Recv(data));
                for (int i = 0; i < runs; i++) {
                    ISelected got = select.Select().Result;
                    if (got.Index != 0) {
                        Assert.AreEqual(0, got.Index);
                    }
                    var gotInt = (ISelected<int>)got;
                    if (gotInt.Value.Value != i) {
                        Assert.AreEqual(Maybe<int>.Some(i), got.Value);
                    }
                }
            });
        }

        [Test, Timeout(10000)]
        public void z10_send_and_select_many_items_from_queued_channel() {
            Benchmark.Go("select on buffer of 10", (int runs) => {
                var data = new Channel<int>(10);
                Task.Run(() => {
                    for (int i = 0; i < runs; i++) {
                        data.Send(i);
                    }
                });
                var select = new Channels(Op.Recv(data));
                for (int i = 0; i < runs; i++) {
                    ISelected got = select.Select().Result;
                    if (got.Index != 0) {
                        Assert.AreEqual(0, got.Index);
                    }
                    var gotInt = (ISelected<int>)got;
                    if (gotInt.Value.Value != i) {
                        Assert.AreEqual(Maybe<int>.Some(i), got.Value);
                    }
                }
            });
        }

        [Test]
        [Timeout(10000)]
        public void z100_send_and_select_many_items_from_queued_channel() {
            Benchmark.Go("select on buffer of 100", (int runs) => {
                var data = new Channel<int>(100);
                Task.Run(() => {
                    for (int i = 0; i < runs; i++) {
                        data.Send(i);
                    }
                });
                var select = new Channels(Op.Recv(data));
                for (int i = 0; i < runs; i++) {
                    var got = select.Select().Result;
                    if (got.Index != 0) {
                        Assert.AreEqual(0, got.Index);
                    }
                    var gotInt = (ISelected<int>)got;
                    if (gotInt.Value.Value != i) {
                        Assert.AreEqual(Maybe<int>.Some(i), got.Value);
                    }
                }
            });
        }

        [Test, Timeout(10000)]
        public void z1000_send_and_select_many_items_from_queued_channel() {
            Benchmark.Go("select on buffer of 1000", (int runs) => {
                var data = new Channel<int>(1000);
                Task.Run(() => {
                    for (int i = 0; i < runs; i++) {
                        data.Send(i);
                    }
                });
                var select = new Channels(Op.Recv(data));
                for (int i = 0; i < runs; i++) {
                    ISelected got = select.Select().Result;
                    if (got.Index != 0) {
                        Assert.AreEqual(0, got.Index);
                    }
                    var gotInt = (ISelected<int>)got;
                    if (gotInt.Value.Value != i) {
                        Assert.AreEqual(Maybe<int>.Some(i), got.Value);
                    }
                }
            });
        }

        [Test]
        [Timeout(20000)]
        public void _2select_on_two_channels() {
            Benchmark.Go("select on two channels", (int runs) => {
                var ch1 = new Channel<int>(1000);
                var ch2 = new Channel<int>(1000);
                int toSend = runs / 2;
                Task.Run(() => {
                    for (int i = 0; i < toSend; i++) {
                        ch1.Send(i);
                    }
                });
                Task.Run(() => {
                    for (int i = 0; i < toSend; i++) {
                        ch2.Send(i);
                    }
                });
                int sum = 0;
                int count = 0;
                var select = new Channels(Op.Recv(ch1), Op.Recv(ch2));
                int max = toSend * 2;
                try {
                    for (int i = 0; i < max; i++) {
                        count++;
                        ISelected got = select.Select().Result;
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
                } catch (Exception e) {
                    Console.WriteLine(count + " " + e.Message);
                    throw;
                }
            }, 500);
        }

        [Test]
        public void select_on_closed_channel_does_not_block_and_returns_no_value() {
            var ch1 = new Channel<int>(1);
            ch1.Close();
            var select = new Channels(Op.Recv(ch1));
            var got = select.Select().Result;
            Assert.AreEqual(0, got.Index);
            Assert.AreEqual(null, got.Value);
        }

        [Test]
        public void select_on_one_null_channel() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>(2);
            ch2.Send(true);
            var select = new Channels(Op.Recv(ch1), Op.Recv(ch2));
            select.ClearAt(0);
            var got = select.Select().Result;
            Assert.AreEqual(1, got.Index, "expected any channel to return");
            Assert.AreEqual(true, got.Value);
        }

    }
}
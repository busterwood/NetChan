using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Threading;

namespace NetChan {

    [TestFixture]
    public class SelectTests {
        [Test]
        public void can_read_after_select() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            ThreadPool.QueueUserWorkItem(state => {
                ch1.Send(123);
                ch2.Send(true);
                ch1.Send(124);
            });
            var select = new Select(ch1, ch2);
            var got = select.Recv();
            Assert.AreEqual(0, got.Index, "select.Index");
            Assert.AreEqual(123, got.Value, "select.Value");
            Assert.AreEqual(Maybe<bool>.Some(true), ch2.Recv());
            Assert.AreEqual(Maybe<int>.Some(124), ch1.Recv());
        }

        [Test, Timeout(100)]
        public void can_select_on_open_and_closed_Channels() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            ThreadPool.QueueUserWorkItem(state => {
                ch1.Close();
                ch2.Send(true);
            });
            var select = new Select(ch1, ch2);
            var gotTrue = false;
            while (!gotTrue) {
                var got = select.Recv();
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
        public void can_select_on_Channels_closed_by_another_thread() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            ThreadPool.QueueUserWorkItem(state => {
                ch1.Close();
                ch2.Close();
            });
            var select = new Select(ch1, ch2);
            var got = select.Recv();
            Assert.AreNotEqual(-1, got.Index, "expected any channel to return");
            Assert.AreEqual(null, got.Value);
        }

        [Test]
        public void can_select_on_all_closed_Channels() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            ch1.Close();
            ch2.Close();
            var select = new Select(ch1, ch2);
            var got = select.Recv();
            Assert.AreNotEqual(-1, got.Index, "expected any channel to return");
            Assert.AreEqual(null, got.Value);
        }

        [Test]
        public void can_tryrecv_on_all_closed_Channels() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            ch1.Close();
            ch2.Close();
            var select = new Select(ch1, ch2);
            var got = select.TryRecv();
            Assert.AreEqual(-1, got.Index, "index");
        }

        [Test]
        public void tryrecv_returns_minus_one_if_no_channels_are_ready() {
            var ch1 = new Channel<int>();
            var ch2 = new QueuedChannel<bool>(1);
            var select = new Select(ch1, ch2);
            var got = select.TryRecv();
            Assert.AreEqual(-1, got.Index, "index");
        }

        [Test]
        public void tryrecv_returns_if_one_channel_is_ready() {
            var ch1 = new Channel<int>();
            var ch2 = new QueuedChannel<bool>(1);
            ThreadPool.QueueUserWorkItem(state => ch1.Send(123));
            Thread.Sleep(1);
            var select = new Select(ch1, ch2);
            var got = select.TryRecv();
            Assert.AreEqual(0, got.Index, "index");
        }

        [Test]
        public void tryrecv_returns_if_the_other_channel_is_ready() {
            var ch1 = new Channel<int>();
            var ch2 = new QueuedChannel<bool>(1);
            var sent = new AutoResetEvent(false);
            ThreadPool.QueueUserWorkItem(state => { ch2.Send(true); sent.Set(); });
            sent.WaitOne();
            var select = new Select(ch1, ch2);
            var got = select.TryRecv();
            Assert.AreEqual(1, got.Index, "index");
        }

        [Test]
        public void can_read_after_select_on_queued_Channels() {
            var ch1 = new QueuedChannel<int>(1);
            var ch2 = new QueuedChannel<bool>(1);
            ThreadPool.QueueUserWorkItem(state => {
                ch1.Send(123);
                ch2.Send(true);
                ch2.Close();
                ch1.Send(124);
                ch1.Close();
            });
            var select = new Select(ch1, ch2);
            var got = select.Recv();
            Debug.Print("got.Index = " + got.Index);
            if (got.Index == 0) {
                Assert.AreEqual(123, got.Value, "got.Value");
                Assert.AreEqual(Maybe<bool>.Some(true), ch2.Recv());
            } else {
                Assert.AreEqual(1, got.Index, "got.Index");
                Assert.AreEqual(true, got.Value, "got.Value");
                Assert.AreEqual(Maybe<int>.Some(123), ch1.Recv());
            }
            got = select.Recv();
            Assert.AreEqual(0, got.Index, "got.Index, value =" + got.Value);
            Assert.AreEqual(124, got.Value, "got.Value");
        }

        [Test, Timeout(3000)]
        public void z_send_and_select_many_items_from_channel() {
            const int runs = (int)3e5;
            var data = new Channel<int>();
            var startCpu = Process.GetCurrentProcess().TotalProcessorTime;
            var sw = new Stopwatch();
            sw.Start();

            ThreadPool.QueueUserWorkItem(state => {
                for (int i = 0; i < runs; i++) {
                    data.Send(i);
                }
            });
            var select = new Select(data);
            for (int i = 0; i < runs; i++) {
                Selected got = select.Recv();
                Assert.AreEqual(0, got.Index);
                Assert.AreEqual(i, got.Value);
            }

            sw.Stop();
            var elapsedCpu = Process.GetCurrentProcess().TotalProcessorTime - startCpu;
            var elasped = sw.ElapsedMilliseconds;
            var opsms = (float)runs / (float)elasped;
            Console.WriteLine("took {0}ms, {1:N1}op/ns, {2:N0}op/sec", elasped, opsms / 1000f, opsms * 1000);
            Console.WriteLine("CPU time {0}ms", elapsedCpu.TotalMilliseconds);
        }

        [Test, Timeout(3000)]
        public void z_send_and_select_many_items_from_queued_channel() {
            const int runs = (int)3e5;
            var data = new QueuedChannel<int>(10);
            var startCpu = Process.GetCurrentProcess().TotalProcessorTime;
            var sw = new Stopwatch();
            sw.Start();

            ThreadPool.QueueUserWorkItem(state => {
                for (int i = 0; i < runs; i++) {
                    data.Send(i);
                }
            });
            var select = new Select(data);
            for (int i = 0; i < runs; i++) {
                Selected got = select.Recv();
                Assert.AreEqual(0, got.Index);
                Assert.AreEqual(i, got.Value);
            }

            sw.Stop();
            var elapsedCpu = Process.GetCurrentProcess().TotalProcessorTime - startCpu;
            var elasped = sw.ElapsedMilliseconds;
            var opsms = (float)runs / (float)elasped;
            Console.WriteLine("took {0}ms, {1:N1}op/ns, {2:N0}op/sec", elasped, opsms / 1000f, opsms * 1000);
            Console.WriteLine("CPU time {0}ms", elapsedCpu.TotalMilliseconds);
        }

        [Test]
        public void select_on_closed_channel_does_not_block_and_returns_no_value() {
            var ch1 = new QueuedChannel<int>(1);
            ch1.Close();
            var select = new Select(ch1);
            var got = select.Recv();
            Assert.AreEqual(0, got.Index);
            Assert.AreEqual(null, got.Value);
        }

        [Test]
        public void select_on_one_null_channel() {
            var ch1 = new Channel<int>();
            var ch2 = new QueuedChannel<bool>(2);
            ch2.Send(true);
            var select = new Select(ch1, ch2);
            select[0] = null;
            var got = select.Recv();
            Assert.AreEqual(1, got.Index, "expected any channel to return");
            Assert.AreEqual(true, got.Value);
        }

    }
}
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
            select.Recv();
            Assert.AreEqual(0, select.Index, "select.Index");
            Assert.AreEqual(123, select.Value, "select.Value");
            Assert.AreEqual(true, ch2.Recv());
            Assert.AreEqual(124, ch1.Recv());
        }

        [Test]
        public void can_select_on_open_and_closed_Channels() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            ThreadPool.QueueUserWorkItem(state => {
                ch1.Close();
                ch2.Send(true);
            });
            var select = new Select(ch1, ch2);
            select.Recv();
            Assert.AreEqual(1, select.Index, "select.Index");
            Assert.AreEqual(true, select.Value, "select.Value");
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
            select.Recv();
            Assert.AreEqual(-1, select.Index, "select.Index");
        }

        [Test]
        public void can_select_on_all_closed_Channels() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            ch1.Close();
            ch2.Close();
            var select = new Select(ch1, ch2);
            select.Recv();
            Assert.AreEqual(-1, select.Index, "select.Index");
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
            select.Recv();
            Debug.Print("select.Index = " + select.Index);
            if (select.Index == 0) {
                Assert.AreEqual(123, select.Value, "select.Value");
                Assert.AreEqual(true, ch2.Recv());
            } else {
                Assert.AreEqual(1, select.Index, "select.Index");
                Assert.AreEqual(true, select.Value, "select.Value");
                Assert.AreEqual(123, ch1.Recv());
            }
            select.Recv();
            Assert.AreEqual(0, select.Index, "select.Index, value =" + select.Value);
            Assert.AreEqual(124, select.Value, "select.Value");
        }

        [Test]
        public void send_and_select_many_items() {
            const int runs = (int)1e5;
            var start = Environment.TickCount;
            var data = new Channel<int>();
            ThreadPool.QueueUserWorkItem(state => {
                for (int i = 0; i < runs; i++) {
                    data.Send(i);
                }
            });
            var select = new Select(data);
            for (int i = 0; i < runs; i++) {
                Assert.AreEqual(0, select.Recv());
                Assert.AreEqual(i, select.Value);
            }
            var elasped = Environment.TickCount - start;
            var opsms = (float)runs / (float)elasped;
            Console.WriteLine("took {0}ms, {1:N1}op/ms, {2:N0}op/sec", elasped, opsms, opsms * 1000);
        }

        [Test]
        public void can_enumerate_channels_until_all_closed() {
            var ch1 = new Channel<int>();
            var ch2 = new Channel<bool>();
            ThreadPool.QueueUserWorkItem(state => {
                ch1.Send(123);
                ch2.Send(true);
                ch2.Close();
                ch1.Send(124);
                ch1.Close();
            });
            var select = new Select(ch1, ch2);
            var en = select.GetEnumerator();
            Assert.IsTrue(en.MoveNext());
            Assert.AreEqual(0, en.Current);
            Assert.AreEqual(123, select.Value);
            
            Assert.IsTrue(en.MoveNext());
            Assert.AreEqual(1, en.Current);
            Assert.AreEqual(true, select.Value);

            Assert.IsTrue(en.MoveNext());
            Assert.AreEqual(0, en.Current);
            Assert.AreEqual(124, select.Value);

            Assert.IsFalse(en.MoveNext());
        }


        [Test]
        public void can_enumerate_queued_hcannels_until_all_closed() {
            var ch1 = new QueuedChannel<int>(1);
            var ch2 = new QueuedChannel<bool>(1);
            ThreadPool.QueueUserWorkItem(state => {
                ch1.Send(123);
                ch1.Send(124);
                ch1.Close();
                ch2.Send(true);
                ch2.Close();
            });
            var select = new Select(ch1, ch2);
            var en = select.GetEnumerator();
            Assert.IsTrue(en.MoveNext());
            Assert.AreEqual(0, en.Current);
            Assert.AreEqual(123, select.Value);

            bool got124 = false, gotTrue = false;
            for (int i = 0; i < 2; i++) {
                Assert.IsTrue(en.MoveNext(), i.ToString() + " 124=" + got124 + ", true=" + gotTrue);
                if (en.Current == 0) {
                    Assert.IsFalse(got124);
                    Assert.AreEqual(124, select.Value);
                    got124 = true;
                } else {
                    Assert.IsFalse(gotTrue);
                    Assert.AreEqual(true, select.Value);
                    gotTrue = true;
                }
            }
            Assert.IsFalse(en.MoveNext());

        }

    }
}
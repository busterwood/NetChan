using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Threading;

namespace NetChan {

    [TestFixture]
    public class TimeTests {
        [Test]
        public void timer_does_not_crash_even_if_channel_has_been_closed() {
            var ch1 = Time.After(TimeSpan.FromSeconds(0.1));
            ch1.Close();
            Thread.Sleep(TimeSpan.FromSeconds(0.2));
        }
    }
}

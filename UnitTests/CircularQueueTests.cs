using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using NetChan;

namespace UnitTests {
    public class CircularQueueTests {

        [Test]
        public void zero_capacity_queue_is_empty() {
            var q = new CircularQueue<int>(0);
            Assert.IsTrue(q.Empty);
        }

        [Test]
        public void zero_capacity_queue_is_full() {
            var q = new CircularQueue<int>(0);
            Assert.IsTrue(q.Full);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace NetChan {
    public static class Time {

        public static IChannel<DateTime> After(TimeSpan after) {
            var ch = new QueuedChannel<DateTime>(1);
            var t = new Timer(state => { ch.Send(DateTime.Now); ch.Close(); });
            t.Change(after, TimeSpan.FromMilliseconds(-1));
            return ch;
        }
    }
}

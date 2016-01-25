// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;
using System.Threading;

namespace NetChan.Async {
    public static class Time {

        public static Channel<DateTime> After(TimeSpan after) {
            var ch = new Channel<DateTime>(1);
            var t = new Timer(state => { ch.TrySend(DateTime.Now); ch.Close(); });
            t.Change(after, TimeSpan.FromMilliseconds(-1));
            return ch;
        }
    }
}

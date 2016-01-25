// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NetChan.Async {
    public static class Time {

        public static Channel<DateTime> After(TimeSpan after) {
            var ch = new Channel<DateTime>(1);
            Task.Delay(after).ContinueWith(state =>
            {
                ch.TrySend(DateTime.Now);
                ch.Close();
            });
            return ch;
        }
    }
}

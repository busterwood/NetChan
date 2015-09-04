using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading;

namespace NetChan {
    /// <summary>
    /// Thread static pool of Waiters for type {T }
    /// </summary>
    /// <remarks>
    /// This massively reduces the garbage created when processing lots of messages, which in turn means higher throughput
    /// </remarks>
    internal class WaiterPool<T> {
        [ThreadStatic]
        private static Stack<Waiter<T>> pool;

        public static Waiter<T> Get(T v) {
            Waiter<T> s = Get();
            s.SetItem(v);
            return s;
        }

        public static Waiter<T> Get() {
            Waiter<T> s;
            if (pool == null || pool.Count == 0) {
                s = new Waiter<T>(new AutoResetEvent(false));
            } else {
                s = pool.Pop();
            }
            return s;
        }

        public static void Put(Waiter<T> w) {
            w.Clear();
            if (pool == null) {
                pool = new Stack<Waiter<T>>();
            }
            pool.Push(w);
        }
    }
}

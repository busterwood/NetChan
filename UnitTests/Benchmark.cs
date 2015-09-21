using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NetChan {
    class Benchmark {

        public static void Go(string name, Action<int> code, int benchtimeMS = 1000) {
            // make sure code is JIT'ed
            code(1);

	        // Run the benchmark for a single iteration in case it's expensive.
	        int n = 1;
            var b = new B();
            b.RunN(n, code);

	        // Run the benchmark for at least the specified amount of time.
            var d = TimeSpan.FromMilliseconds(benchtimeMS);
	        while (b.Duration < d && n < 1e9) {
		        int last = n;
		        // Predict required iterations.
		        if (b.NsPerOp() == 0.0) {
			        n = 1000 * 1000 * 1000;
		        } else {
			        n = (int)((d.TotalMilliseconds * 1000) / b.NsPerOp());
		        }
		        // Run more iterations than we think we'll need (1.2x).
		        // Don't grow too fast in case we had timing errors previously.
		        // Be sure to run at least one more than last time.
		        n = Max(Min(n+n/5, 100*last), last+1);
		        // Round up to something easy to read.
		        n = roundUp(n);
		        b.RunN(n, code);
	        }
            Console.WriteLine("{4}: {0:#,##0}, {1:0.00} ns/op, {2:N0}MS CPU, {3:N0} memory", b.N, b.NsPerOp(), b.totalCPU.TotalMilliseconds, b.totalMemory, name);
        }

        static int Max(int x, int y) {
	        return x < y ? y : x;
        }

        static int Min(int x, int y) {
            return x < y ? x : y;
        }

        static int roundDown10(int n) {
	        int tens = 0;
	        // tens = floor(log_10(n))
	        while (n >= 10) {
		        n = n / 10;
		        tens++;
	        }
	        // result = 10^tens
	        var result = 1;
	        for (int i = 0; i < tens; i++) {
		        result *= 10;
	        }
	        return result;
        }

        // roundUp rounds x up to a number of the form [1eX, 2eX, 3eX, 5eX].
        static int roundUp(int n)  {
	        int @base = roundDown10(n);
	        if (n <= @base)
		        return @base;
	        if (n <= (2 * @base))
		        return 2 * @base;
	        if (n <= (3 * @base))
		        return 3 * @base;
	        if (n <= (5 * @base))
		        return 5 * @base;
            return 10 * @base;
        }

        internal class B {
            private Stopwatch sw = new Stopwatch();
            public int N;
            private long totalTicks;
            public TimeSpan Duration;
            private long startMemory;
            public long totalMemory;
            private TimeSpan startCPU;
            public TimeSpan totalCPU;
            
            public void Start() {
                if (sw.IsRunning) return;
                startMemory = GC.GetTotalMemory(true);
                startCPU = Process.GetCurrentProcess().TotalProcessorTime;
                sw.Start();
            }

            public void Stop() {
                if (!sw.IsRunning) return;
                sw.Stop();
                totalCPU += Process.GetCurrentProcess().TotalProcessorTime - startCPU;
                totalMemory += GC.GetTotalMemory(true) - startMemory;
                totalTicks += sw.ElapsedTicks;
                Duration += sw.Elapsed;
            }

            private double ElaspedNS() {
                return (totalTicks * 1e9) / (double)Stopwatch.Frequency;
            }

            public double NsPerOp() {
                if (N <= 0) {
                    return 0.0;
                }   
                return ElaspedNS() / (double)N;
            }

            public void RunN(int n, Action<int> code) {
                N = n;
 	            Start();
                try {
                    code(N);
                } finally {
                    Stop();
                }
            }
        }
    }
}

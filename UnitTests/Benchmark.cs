using System;
using System.Diagnostics;
using System.Text;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace NetChan {
    class Benchmark {

        public static void Go(string name, Action<int> code, int benchtimeMS = 1000) {
            // make sure code is JIT'ed
            code(1);
            
            // force a full GC before test, so any GCs that do happen are do to the code being run
            GC.Collect();


	        // Run the benchmark for a single iteration in case it's expensive.
	        int n = 1;
            var b = new B();
            b.RunN(n, code);

	        // Run the benchmark for at least the specified amount of time.
            while (b.ElaspedMS() < benchtimeMS && n < 1e9) {
		        int last = n;
		        // Predict required iterations.
		        if (b.NsPerOp() == 0.0) {
			        n = 1000 * 1000 * 1000;
		        } else {
                    n = (int)((benchtimeMS * 1e6) / b.NsPerOp());
		        }
		        // Run more iterations than we think we'll need (1.2x).
		        // Don't grow too fast in case we had timing errors previously.
		        // Be sure to run at least one more than last time.
		        n = Max(Min(n+n/5, 100*last), last+1);
		        // Round up to something easy to read.
		        n = RoundUp(n);
		        b.RunN(n, code);
	        }
            Console.WriteLine("{4}: {0:#,##0}, {6:#,##0.0}ops/sec, {1:0.00} ns/op, {2:0.00} User, {3:0.00} Kernel {5}", b.N, b.NsPerOp(), b.TotalCpu.User.TotalSeconds, b.TotalCpu.Kernel.TotalSeconds, name, b.TotalGCs, b.N / (b.ElaspedMS() / 1000.0));
        }

        static int Max(int x, int y) {
	        return x < y ? y : x;
        }

        static int Min(int x, int y) {
            return x < y ? x : y;
        }

        static int RoundDown10(int n) {
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
        static int RoundUp(int n)  {
	        int @base = RoundDown10(n);
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
            private CpuTime startCPU;
            public CpuTime TotalCpu;
            private GcCount startGCs;
            public GcCount TotalGCs;
            
            public void Start() {
                if (sw.IsRunning) return;
                startCPU = NativeMethods.GetProcessTime();
                startGCs = GcCount.Now();
                sw.Start();
            }

            public void Stop() {
                if (!sw.IsRunning) return;
                sw.Stop();
                TotalCpu += NativeMethods.GetProcessTime() - startCPU;
                TotalGCs += GcCount.Now() - startGCs;
                totalTicks += sw.ElapsedTicks;
            }

            private double ElaspedNS() {
                return (totalTicks * 1e9) / Stopwatch.Frequency;
            }

            public double ElaspedMS() {
                return (totalTicks * 1000.0) / Stopwatch.Frequency;
            }

            public double NsPerOp() {
                if (N <= 0) {
                    return 0.0;
                }   
                return ElaspedNS() / N;
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

    internal static class NativeMethods {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(IntPtr hProcess, out FILETIME creation, out FILETIME exit, out FILETIME kernel, out FILETIME user);

        public static CpuTime GetProcessTime() {
            FILETIME creation, exit, kernel, user;
            if (!GetProcessTimes(Process.GetCurrentProcess().Handle, out creation, out exit, out kernel, out user)) {
                throw new Win32Exception();
            }
            return new CpuTime(kernel.AsTimeSpan(), user.AsTimeSpan());
        }

        private static TimeSpan AsTimeSpan(this FILETIME fileTime) {
            //NB! uint conversion must be done on both fields before ulong conversion
            ulong hFT2 = unchecked((((ulong)(uint)fileTime.dwHighDateTime) << 32) | (uint)fileTime.dwLowDateTime);
            return TimeSpan.FromTicks((long)hFT2);
        }
    }

    struct CpuTime {
        public readonly TimeSpan Kernel;
        public readonly TimeSpan User;

        public CpuTime(TimeSpan kernel, TimeSpan user) {
            Kernel = kernel;
            User = user;
        }

        public static CpuTime operator +(CpuTime x, CpuTime y) {
            return new CpuTime(x.Kernel + y.Kernel, x.User + y.User);
        }

        public static CpuTime operator -(CpuTime x, CpuTime y) {
            return new CpuTime(x.Kernel - y.Kernel, x.User - y.User);
        }
    }

    struct GcCount {
        public readonly int Gen0;
        public readonly int Gen1;
        public readonly int Gen2;

        public static GcCount Now() {
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            return new GcCount(gen0, gen1, gen2);
        }

        public GcCount(int gen0, int gen1, int gen2) {
            Gen0 = gen0;
            Gen1 = gen1;
            Gen2 = gen2;
        }

        public static GcCount operator +(GcCount x, GcCount y) {
            return new GcCount(x.Gen0 + y.Gen0, x.Gen1 + y.Gen1, x.Gen2 + y.Gen2);
        }

        public static GcCount operator -(GcCount x, GcCount y) {
            return new GcCount(x.Gen0 - y.Gen0, x.Gen1 - y.Gen1, x.Gen2 - y.Gen2);
        }

        public override string ToString() {
            var sb = new StringBuilder();
            if (Gen0 > 0) {
                sb.AppendFormat(", Gen0={0}", Gen0);
            }
            if (Gen1 > 0) {
                sb.AppendFormat(", Gen1={0}", Gen1);
            }
            if (Gen2 > 0) {
                sb.AppendFormat(", Gen2={0}", Gen2);
            }
            return sb.ToString();
        }
    }
}

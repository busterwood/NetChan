using System;
using System.Collections.Generic;
using System.Diagnostics;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;
using System.Runtime.InteropServices;
using System.ComponentModel;

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
		        n = roundUp(n);
		        b.RunN(n, code);
	        }
            Console.WriteLine("{4}: {0:#,##0}, {1:0.00} ns/op, {2:0.00} User, {3:0.00} Kernel", b.N, b.NsPerOp(), b.totalCPU.User.TotalSeconds, b.totalCPU.Kernel.TotalSeconds, name);
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
            private long startMemory;
            public long totalMemory;
            private CpuTime startCPU;
            public CpuTime totalCPU;
            
            public void Start() {
                if (sw.IsRunning) return;
                startMemory = GC.GetTotalMemory(true);
                startCPU = NativeMethods.GetProcessTime();
                sw.Start();
            }

            public void Stop() {
                if (!sw.IsRunning) return;
                sw.Stop();
                totalCPU += NativeMethods.GetProcessTime() - startCPU;
                totalMemory += GC.GetTotalMemory(true) - startMemory;
                totalTicks += sw.ElapsedTicks;
            }

            private double ElaspedNS() {
                return (totalTicks * 1e9) / (double)Stopwatch.Frequency;
            }

            public double ElaspedMS() {
                return (totalTicks * 1000.0) / (double)Stopwatch.Frequency;
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

    internal static class NativeMethods {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(IntPtr hProcess, out FILETIME creation, out FILETIME exit, out FILETIME kernel, out FILETIME user);

        public static TimeSpan AsTimeSpan(this FILETIME fileTime) {
            //NB! uint conversion must be done on both fields before ulong conversion
            ulong hFT2 = unchecked((((ulong)(uint)fileTime.dwHighDateTime) << 32) | (uint)fileTime.dwLowDateTime);
            return TimeSpan.FromTicks((long)hFT2);
        }

        public static CpuTime GetProcessTime() {
            FILETIME creation, exit, kernel, user;
            if (!GetProcessTimes(Process.GetCurrentProcess().Handle, out creation, out exit, out kernel, out user)) {
                throw new Win32Exception();
            }
            return new CpuTime(kernel.AsTimeSpan(), user.AsTimeSpan());
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
}

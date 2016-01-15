using System;
using System.Runtime.InteropServices;
using System.Security;

namespace NetChan {

    /// <remarks>
    /// Used to improve performance by 10-20% based on my benchmarks
    /// </remarks>
    [SuppressUnmanagedCodeSecurity]
    static class NativeMethods {
        [DllImport("Kernel32.dll", SetLastError=true)]
        public static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool manualReset, bool initialState, IntPtr lpName);

        [DllImport("Kernel32.dll", SetLastError = true)]
        public static extern bool SetEvent(IntPtr hEvent);

        [DllImport("Kernel32.dll", SetLastError = true)]
        public static extern bool ResetEvent(IntPtr hEvent);

        [DllImport("Kernel32.dll", SetLastError = true)]
        public static extern int WaitForSingleObject(IntPtr hHandle, int milliseconds);

        [DllImport("Kernel32.dll", SetLastError = true)]
        public static extern int WaitForMultipleObjects(
            int count,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex=0)] IntPtr[] handles,
            bool waitAll,
            int milliseconds
        );

        [DllImport("Kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hEvent);
    }
}

using System.Threading;

namespace NetChan
{
    /// <remarks>Neeed when selecting over many channels to ensure only one channel exchanges messages</remarks>
    public class Sync {
        public int Value = -1;

        public bool IsUnSet => Value == -1;
    
    }

    static class SyncExtensions
    {
        public static bool IsSet(this Sync s)
        {
            return s != null && s.Value != -1;
        }

        public static bool TrySet(this Sync sync, int val)
        {
            return sync == null || Interlocked.CompareExchange(ref sync.Value, val, -1) == -1;
        }
    }

}
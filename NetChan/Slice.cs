using System;
using System.Collections.Generic;
using System.Text;

namespace NetChan {
    public struct Slice<T> {
        private readonly T[] arr;
        private readonly int offset;
        public readonly int Len;

        public Slice(int len) {
            if (len < 0) {
                throw new ArgumentOutOfRangeException("len", "cannot be negative");
            }
            this.arr = new T[len];
            this.offset = 0;
            this.Len = len;
        }

        public Slice(int len, int cap) {
            if (len < 0) {
                throw new ArgumentOutOfRangeException("len", "cannot be negative");
            }
            if (cap < 0) {
                throw new ArgumentOutOfRangeException("cap", "cannot be negative");
            }
            if (len > cap) {
                throw new ArgumentOutOfRangeException("len","cannot be greater than capacity");
            }
            this.arr = new T[cap];
            this.offset = 0;
            this.Len = len;
        }

        private Slice(T[] arr, int offset, int len) {
            this.arr = arr;
            this.offset = offset;
            this.Len = len;
        }

        public int Cap {
            get { return arr == null ? 0 : arr.Length - offset; }
        }

        public T this[int i] {
            get { return arr[offset + i]; }
            set { arr[offset + i] = value; }
        }

        public Slice<T> Sub(int start, int end) {
            if (start > end) {
                throw new ArgumentOutOfRangeException("start", "start cannot be greater than end");
            }
            return new Slice<T>(arr, offset + start, end - start);
        }

        public Slice<T> Append(params T[] data) {
            int m = Len;
            int n = m + data.Length;
            Slice<T> s = this;
            if (n > Cap) { 
                // allocate double what's needed, for future growth.
                s = new Slice<T>((n+1)*2);
                Copy(s, this);
            }
            s = s.Sub(0, n);
            Copy(s.Sub(m, n), data);
            return s;
        }
        
        public static void Copy(Slice<T> dest, Slice<T> src) {
            int toCopy = src.Len > dest.Cap ? dest.Cap : src.Len;
            Array.Copy(src.arr, src.offset, dest.arr, dest.offset, toCopy);
        }

        public static void Copy(Slice<T> dest, T[] src) {
            int toCopy = src.Length > dest.Cap ? dest.Cap : src.Length;
            Array.Copy(src, 0, dest.arr, dest.offset, toCopy);
        }
    }
    
}

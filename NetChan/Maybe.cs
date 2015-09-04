using System;
using System.Collections.Generic;
using System.Text;

namespace NetChan {
    /// <summary>
    /// Optional value (Option monad).  Like Nullable{T} but for classes as well
    /// </summary>
    /// <remarks>
    /// Includes a Reason why the value is missing
    /// </remarks>
    public struct Maybe<T> {
        public readonly T Value;
        public readonly string Reason;
        public readonly bool Present;

        public bool Absent { get { return !Present; } }

        internal Maybe(T value) {
            Value = value;
            Present = true;
            Reason = null;
        }

        internal Maybe(string reason) {
            Value = default(T);
            Present = false;
            Reason = reason;
        }

        public static Maybe<T> Some(T value) {
            return new Maybe<T>(value);
        }

        public static Maybe<T> None() {
            return new Maybe<T>(null);
        }

        public static Maybe<T> None(string reason) {
            return new Maybe<T>(reason);
        }

        public override string ToString() {
            if (!Present) {
                return "(none)";
            }
            return Value.ToString();
        }
    }
}

// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;

namespace NetChan {
    /// <summary>Optional value (Option monad).  Like Nullable{T} but for classes as well</summary>
    public struct Maybe<T> : IEquatable<Maybe<T>> {
        public readonly T Value;
        public readonly bool IsSome;

        public bool IsNone { get { return !IsSome; } }

        internal Maybe(T value) {
            Value = value;
            IsSome = true;
        }

        public static Maybe<T> Some(T value) {
            return new Maybe<T>(value);
        }

        public static Maybe<T> None() {
            return new Maybe<T>();
        }

        public bool Equals(Maybe<T> other) {
            return IsNone ? other.IsNone : Value.Equals(other.Value);
        }

        public override string ToString() {
            return IsNone ? "(none)" : Value.ToString();
        }

        public override bool Equals(object obj) {
            return obj is Maybe<T> && Equals((Maybe<T>)obj);
        }

        public override int GetHashCode() {
            var hc = IsSome.GetHashCode();
            if (!ReferenceEquals(Value, null)) {
                hc *= Value.GetHashCode();
            }
            return hc;
        }

        public static bool operator ==(Maybe<T> left, Maybe<T> right) {
            return left.Equals(right);
        }

        public static bool operator !=(Maybe<T> left, Maybe<T> right) {
            return !left.Equals(right);
        }
    }
}

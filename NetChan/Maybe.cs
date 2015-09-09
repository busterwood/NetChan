﻿using System;
using System.Collections.Generic;
using System.Text;

namespace NetChan {
    /// <summary>Optional value (Option monad).  Like Nullable{T} but for classes as well</summary>
    /// <remarks>Includes a Reason why the value is missing</remarks>
    public struct Maybe<T> {
        public readonly T Value;
        public readonly bool IsSome;
        public readonly string Reason;

        public bool IsNone { get { return !IsSome; } }

        internal Maybe(T value) {
            Value = value;
            IsSome = true;
            Reason = null;
        }

        internal Maybe(string reason) {
            Value = default(T);
            IsSome = false;
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
            return IsNone ? "(none)" : Value.ToString();
        }

        public override bool Equals(object obj) {
            if (obj == null || !(obj is Maybe<T>)) {
                return false;
            }
            var other = (Maybe<T>)obj;
            return IsSome == other.IsSome && (IsNone || Value.Equals(other.Value));
        }

        public override int GetHashCode() {
            var hc = IsSome.GetHashCode();
            if (!ReferenceEquals(Value, null)) {
                hc *= Value.GetHashCode();
            }
            return hc;
        }
    }
}

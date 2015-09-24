// Copyright the Netchan authors, see LICENSE.txt for permitted use
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace NetChan {
    internal sealed class CircularQueue<T> : IEnumerable<T> {
        private readonly T[] arr;
        private int head;       // First valid element in the queue
        private int tail;       // Last valid element in the queue

        public CircularQueue(int capacity) {
            if (capacity < 0) {
                throw new ArgumentOutOfRangeException();
            }
            arr = new T[capacity+1]; // extra free slot to detect if the buffer is full
            head = 0;
            tail = 0;
        }

        public bool Empty {
            get { return head == tail; }
        }

        public bool Full {
            get { return tail == head - 1 || (head == 0 && tail == arr.Length - 1); }
        }

        public int Capacity {
            get { return arr.Length-1; }
        }

        public void Clear() {
            Array.Clear(arr, 0, arr.Length);
            head = 0;
            tail = 0;
        }

        public void Enqueue(T item) {
            Debug.Assert(!Full);
            arr[tail] = item;
            tail = (tail + 1) % arr.Length;
        }

        public T Dequeue() {
            Debug.Assert(!Empty);
            T removed = arr[head];
            arr[head] = default(T);
            head = (head + 1) % arr.Length;
            return removed;
        }

        public T Peek() {
            Debug.Assert(!Empty);
            return arr[head];
        }

        public IEnumerator<T> GetEnumerator() {
            for (int i = head; i != tail; i = (i + 1) % arr.Length) {
                yield return arr[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }
    } 

}

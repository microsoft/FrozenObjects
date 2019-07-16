// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Microsoft.Collections.Extensions
{
    public class DictionarySlim : IReadOnlyCollection<KeyValuePair<string, string>>
    {
        // We want to initialize without allocating arrays. We also want to avoid null checks.
        // Array.Empty would give divide by zero in modulo operation. So we use static one element arrays.
        // The first add will cause a resize replacing these with real arrays of three elements.
        // Arrays are wrapped in a class to avoid being duplicated for each <TKey, TValue>
        private static readonly Entry[] InitialEntries = new Entry[1];
        private int _count;
        // 0-based index into _entries of head of free chain: -1 means empty
        private int _freeList = -1;
        // 1-based index into _entries; 0 means empty
        private int[] _buckets;
        private Entry[] _entries;

        public Entry[] Entries
        {
            get { return this._entries; }
        }

        public int[] Buckets
        {
            get { return this._buckets; }
        }

        public struct Entry
        {
            public string key;
            public string value;
            // 0-based index of next entry in chain: -1 means end of chain
            // also encodes whether this entry _itself_ is part of the free list by changing sign and subtracting 3,
            // so -2 means end of free list, -3 means index 0 but on free list, -4 means index 1 but on free list, etc.
            public int next;
        }

        /// <summary>
        /// Construct with default capacity.
        /// </summary>
        public DictionarySlim()
        {
            _buckets = HashHelpers.SizeOneIntArray;
            _entries = InitialEntries;
        }

        /// <summary>
        /// Construct with at least the specified capacity for
        /// entries before resizing must occur.
        /// </summary>
        /// <param name="capacity">Requested minimum capacity</param>
        public DictionarySlim(int capacity)
        {
            if (capacity < 2)
                capacity = 2; // 1 would indicate the dummy array
            capacity = HashHelpers.PowerOf2(capacity);
            _buckets = new int[capacity];
            _entries = new Entry[capacity];
        }

        /// <summary>
        /// Count of entries in the dictionary.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Clears the dictionary. Note that this invalidates any active enumerators.
        /// </summary>
        public void Clear()
        {
            _count = 0;
            _freeList = -1;
            _buckets = HashHelpers.SizeOneIntArray;
            _entries = InitialEntries;
        }

        // Not safe for concurrent _reads_ (at least, if either of them add)
        // For concurrent reads, prefer TryGetValue(key, out value)
        /// <summary>
        /// Gets the value for the specified key, or, if the key is not present,
        /// adds an entry and returns the value by ref. This makes it possible to
        /// add or update a value in a single look up operation.
        /// </summary>
        /// <param name="key">Key to look for</param>
        /// <returns>Reference to the new or existing value</returns>
        public ref string GetOrAddValueRef(string key)
        {
            Entry[] entries = _entries;
            int bucketIndex = GetHashCodeCompat(key) & (_buckets.Length - 1);
            for (int i = _buckets[bucketIndex] - 1;
                    (uint)i < (uint)entries.Length; i = entries[i].next)
            {
                if (StringComparer.OrdinalIgnoreCase.Compare(key, entries[i].key) == 0)
                    return ref entries[i].value;
            }

            return ref AddKey(key, bucketIndex);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ref string AddKey(string key, int bucketIndex)
        {
            Entry[] entries = _entries;
            int entryIndex;
            if (_freeList != -1)
            {
                entryIndex = _freeList;
                _freeList = -3 - entries[_freeList].next;
            }
            else
            {
                if (_count == entries.Length || entries.Length == 1)
                {
                    entries = Resize();
                    bucketIndex = GetHashCodeCompat(key) & (_buckets.Length - 1);
                    // entry indexes were not changed by Resize
                }
                entryIndex = _count;
            }

            entries[entryIndex].key = key;
            entries[entryIndex].next = _buckets[bucketIndex] - 1;
            _buckets[bucketIndex] = entryIndex + 1;
            _count++;
            return ref entries[entryIndex].value;
        }

        private Entry[] Resize()
        {
            Debug.Assert(_entries.Length == _count || _entries.Length == 1); // We only copy _count, so if it's longer we will miss some
            int count = _count;
            int newSize = _entries.Length * 2;
            if ((uint)newSize > (uint)int.MaxValue) // uint cast handles overflow
                throw new InvalidOperationException("Overflow");

            var entries = new Entry[newSize];
            Array.Copy(_entries, 0, entries, 0, count);

            var newBuckets = new int[entries.Length];
            while (count-- > 0)
            {
                int bucketIndex = GetHashCodeCompat(entries[count].key) & (newBuckets.Length - 1);
                entries[count].next = newBuckets[bucketIndex] - 1;
                newBuckets[bucketIndex] = count + 1;
            }

            _buckets = newBuckets;
            _entries = entries;

            return entries;
        }

        /// <summary>
        /// Gets an enumerator over the dictionary
        /// </summary>
        public Enumerator GetEnumerator() => new Enumerator(this); // avoid boxing

        /// <summary>
        /// Gets an enumerator over the dictionary
        /// </summary>
        IEnumerator<KeyValuePair<string, string>> IEnumerable<KeyValuePair<string, string>>.GetEnumerator() =>
            new Enumerator(this);

        /// <summary>
        /// Gets an enumerator over the dictionary
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        /// <summary>
        /// Enumerator
        /// </summary>
        public struct Enumerator : IEnumerator<KeyValuePair<string, string>>
        {
            private readonly DictionarySlim _dictionary;
            private int _index;
            private int _count;
            private KeyValuePair<string, string> _current;

            internal Enumerator(DictionarySlim dictionary)
            {
                _dictionary = dictionary;
                _index = 0;
                _count = _dictionary._count;
                _current = default;
            }

            /// <summary>
            /// Move to next
            /// </summary>
            public bool MoveNext()
            {
                if (_count == 0)
                {
                    _current = default;
                    return false;
                }

                _count--;

                while (_dictionary._entries[_index].next < -1)
                    _index++;

                _current = new KeyValuePair<string, string>(
                    _dictionary._entries[_index].key,
                    _dictionary._entries[_index++].value);
                return true;
            }

            /// <summary>
            /// Get current value
            /// </summary>
            public KeyValuePair<string, string> Current => _current;

            object IEnumerator.Current => _current;

            void IEnumerator.Reset()
            {
                _index = 0;
                _count = _dictionary._count;
                _current = default;
            }

            /// <summary>
            /// Dispose the enumerator
            /// </summary>
            public void Dispose() { }
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        private static unsafe int GetHashCodeCompat(string content)
        {
            fixed (char* c = content)
            {
                return Marvin.ComputeHash32OrdinalIgnoreCase(ref *c, content.Length, 0xDEAD, 0xBEEF);
            }
        }
    }
}
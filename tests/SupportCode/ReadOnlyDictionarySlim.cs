namespace Microsoft.Collections.Extensions
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential)]
    public sealed unsafe class ReadOnlyDictionarySlim : IReadOnlyCollection<KeyValuePair<string, string>>
    {
        private readonly int[] _buckets;
        private readonly Entry[] _entries;

        internal struct Entry
        {
            public string key;
            public string value;
            public int next;
        }

        internal ReadOnlyDictionarySlim(IntPtr addr, int[] buckets, Entry[] entries)
        {
            this._buckets = buckets;
            this._entries = entries;
        }

        public int Count => this._entries.Length;

        public bool TryGetValue(string key, out string value)
        {
            var entries = this._entries;
            var buckets = this._buckets;

            for (int i = buckets[Marvin.ComputeHash32OrdinalIgnoreCase(ref MemoryMarshal.GetReference(key.AsSpan()), key.Length, 0xDEAD, 0xBEEF) & (buckets.Length - 1)] - 1; (uint)i < (uint)entries.Length; i = entries[i].next)
            {
                if (StringComparer.OrdinalIgnoreCase.Compare(key, entries[i].key) == 0)
                {
                    value = entries[i].value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        public Enumerator GetEnumerator() => new Enumerator(this); // avoid boxing

        IEnumerator<KeyValuePair<string, string>> IEnumerable<KeyValuePair<string, string>>.GetEnumerator() => new Enumerator(this);

        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        public struct Enumerator : IEnumerator<KeyValuePair<string, string>>
        {
            private readonly ReadOnlyDictionarySlim _dictionary;
            private int _index;
            private int _count;
            private KeyValuePair<string, string> _current;

            internal Enumerator(ReadOnlyDictionarySlim dictionary)
            {
                _dictionary = dictionary;
                _index = 0;
                _count = _dictionary._entries.Length;
                _current = default;
            }

            public bool MoveNext()
            {
                if (_count == 0)
                {
                    _current = default;
                    return false;
                }

                _count--;

                var entries = _dictionary._entries;

                while (entries[_index].next < -1)
                    _index++;

                _current = new KeyValuePair<string, string>(entries[_index].key, entries[_index].value);

                _index++;
                return true;
            }

            public KeyValuePair<string, string> Current => _current;

            object IEnumerator.Current => _current;

            void IEnumerator.Reset()
            {
                _index = 0;
                _count = _dictionary._entries.Length;
                _current = default;
            }

            public void Dispose()
            {
            }
        }
    }
}
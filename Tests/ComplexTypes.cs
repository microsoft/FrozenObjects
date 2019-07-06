using System.Collections.Generic;
using Microsoft.Collections.Extensions;

namespace Microsoft.FrozenObjects.UnitTests
{
    public enum LongEnum : long
    {
        Min = long.MinValue,
        Max = long.MaxValue
    }

    public enum IntEnum : int
    {
        Min = int.MinValue,
        Max = int.MaxValue
    }

    public enum UIntEnum : uint
    {
        Min = uint.MinValue,
        Max = uint.MaxValue
    }

    public struct GenericValueTypeWithReferences<T>
    {
        public string A;
        public byte B;
        public T C;
        public T[] D;
        public string E;
    }

    public class GenericBaseClassForThings<T>
    {
        public List<T> BaseA;
        public int BaseB;
        public LongEnum BaseC;
        public string BaseD;
    }

    public class GenericReferenceTypeWithInheritance<X, T, K, V> : GenericBaseClassForThings<DictionarySlim>
    {
        public T A;
        public K[] B;
        public V[] C;
        public V D;
        public X Y;
        public Recursive R;
    }

    public class Recursive
    {
        public Recursive Field;

        public string Data;
    }
}

namespace Microsoft.FrozenObjects
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;

    internal unsafe struct GCDesc
    {
        private readonly IntPtr data;

        private readonly int size;

        public GCDesc(byte* data, int size)
        {
            this.data = new IntPtr(data);
            this.size = size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetNumSeries()
        {
            return (int)Marshal.ReadIntPtr(this.data + this.size - IntPtr.Size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHighestSeries()
        {
            return this.size - IntPtr.Size * 3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetLowestSeries()
        {
            return this.size - ComputeSize(this.GetNumSeries());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetSeriesSize(int curr)
        {
            return (int)Marshal.ReadIntPtr(this.data + curr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong GetSeriesOffset(int curr)
        {
            return (ulong)Marshal.ReadIntPtr(this.data + curr + IntPtr.Size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetPointers(int curr, int i)
        {
            int offset = i * IntPtr.Size;
            return IntPtr.Size == 8 ? (uint)Marshal.ReadInt32(this.data + curr + offset) : (uint)Marshal.ReadInt16(this.data + curr + offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetSkip(int curr, int i)
        {
            int offset = i * IntPtr.Size + IntPtr.Size / 2;
            return IntPtr.Size == 8 ? (uint)Marshal.ReadInt32(this.data + curr + offset) : (uint)Marshal.ReadInt16(this.data + curr + offset);
        }

        public void EnumerateObject(object o, ulong objectSize, Dictionary<object, long> serializedObjectMap, Queue<object> objectQueue, Stream stream, long objectStart, ref long lastReservedObjectEnd)
        {
            int series = this.GetNumSeries();
            int highest = this.GetHighestSeries();
            int curr = highest;

            if (series > 0)
            {
                int lowest = this.GetLowestSeries();
                do
                {
                    ulong offset = this.GetSeriesOffset(curr);
                    ulong stop = offset + (ulong)(this.GetSeriesSize(curr) + (long)objectSize);

                    while (offset < stop)
                    {
                        EachObjectReference(o, (int)offset, serializedObjectMap, objectQueue, stream, objectStart, ref lastReservedObjectEnd);
                        offset += (ulong)IntPtr.Size;
                    }

                    curr -= IntPtr.Size * 2;
                } while (curr >= lowest);
            }
            else
            {
                ulong offset = this.GetSeriesOffset(curr);
                while (offset < objectSize - (ulong)IntPtr.Size)
                {
                    for (int i = 0; i > series; i--)
                    {
                        uint nptrs = this.GetPointers(curr, i);
                        uint skip = this.GetSkip(curr, i);

                        ulong stop = offset + nptrs * (uint)IntPtr.Size;
                        do
                        {
                            EachObjectReference(o, (int)offset, serializedObjectMap, objectQueue, stream, objectStart, ref lastReservedObjectEnd);
                            offset += (ulong)IntPtr.Size;
                        } while (offset < stop);

                        offset += skip;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeSize(int series)
        {
            return IntPtr.Size + series * IntPtr.Size * 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static void EachObjectReference(object o, int fieldOffset, Dictionary<object, long> serializedObjectMap, Queue<object> objectQueue, Stream stream, long objectStart, ref long lastReservedObjectEnd)
        {
            ref var objectReference = ref Unsafe.As<byte, object>(ref Unsafe.Add(ref Serializer.GetRawData(o), fieldOffset - IntPtr.Size));
            if (objectReference == null)
            {
                return;
            }

            if (!serializedObjectMap.TryGetValue(objectReference, out var objectReferenceDiskOffset))
            {
                objectReferenceDiskOffset = lastReservedObjectEnd + IntPtr.Size; // + IntPtr.Size because object references point to the MT*
                var objectSize = Serializer.GetObjectSize(objectReference);
                lastReservedObjectEnd += objectSize + Serializer.Padding(objectSize, IntPtr.Size);

                serializedObjectMap.Add(objectReference, objectReferenceDiskOffset);
                objectQueue.Enqueue(objectReference);
            }

            WriteFileAtPosition(stream, objectStart + fieldOffset, new ReadOnlySpan<byte>(&objectReferenceDiskOffset, IntPtr.Size));
        }

        private static void WriteFileAtPosition(Stream stream, long position, ReadOnlySpan<byte> data)
        {
            stream.Seek(position, SeekOrigin.Begin);
            stream.Write(data);
        }
    }
}
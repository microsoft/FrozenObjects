// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.FrozenObjects
{
    using System;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using static NativeMethods;

    public static unsafe class Deserializer
    {
        public static object Deserialize(RuntimeTypeHandle[] runtimeTypeHandles, string blobPath)
        {
            var extraSpace = IntPtr.Size + IntPtr.Size; // 1 extra IntPtr for segmentHandle and the other for size of the allocation (needed on Linux)
            var length = 0L;
            byte* buffer;

            using (var fs = new FileStream(blobPath, FileMode.Open, FileAccess.Read))
            {
                length = fs.Length;
                var chunkSize = Math.Min(64 * 1024, (int)length); // 64KB is a reasonable size

                buffer = AllocateMemory(length + extraSpace) + extraSpace; // so that buffer points to the real start of the frozen segment.

                var span = new Span<byte>(buffer, chunkSize);
                var read = 0L;
                int chunk;

                while ((chunk = fs.Read(span)) > 0)
                {
                    read += chunk;

                    if (read == length)
                    {
                        break;
                    }

                    span = new Span<byte>(buffer + read, Math.Min(chunkSize, (int)(length - read)));
                }
            }

            var retVal = DeserializeInner(runtimeTypeHandles, buffer, length);

            // -InPtr.Size - IntPtr.Size is for size (we need it in Linux)
            Marshal.WriteIntPtr((IntPtr)buffer, 0 - IntPtr.Size - IntPtr.Size, (IntPtr)(length + extraSpace));

            // -IntPtr.Size is for segment handle
            Marshal.WriteIntPtr((IntPtr)buffer, 0 - IntPtr.Size, InternalHelpers.RegisterFrozenSegment((IntPtr)buffer, (IntPtr)(length-IntPtr.Size)));

            return retVal;
        }

        public static void UnloadFrozenObject(object o)
        {
            var optr = Marshal.ReadIntPtr((IntPtr)Unsafe.AsPointer(ref o));
            var baseAddress = optr - IntPtr.Size - IntPtr.Size - IntPtr.Size; // 3 because optr is actually pointing to MT*
            var length = Marshal.ReadIntPtr(baseAddress);
            var segmentHandle = Marshal.ReadIntPtr(baseAddress, IntPtr.Size);

            InternalHelpers.UnregisterFrozenSegment(segmentHandle);
            FreeMemory(baseAddress, length);
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
        private static object DeserializeInner(RuntimeTypeHandle[] runtimeTypeHandles, byte* buffer, long length)
        {
            byte* objectId = buffer + IntPtr.Size;

            while (objectId < buffer + length)
            {
                IntPtr typeHandle = IntPtr.Size == 8 ? runtimeTypeHandles[(int)*(long*)objectId].Value : runtimeTypeHandles[*(int*)objectId].Value;
                bool isArray = ((long)typeHandle & 0x2) == 0x2;

                var mt = (MethodTable*)typeHandle;
                if (isArray)
                {
                    mt = IntPtr.Size == 8 ? (MethodTable*)*(long*)(typeHandle + 6) : (MethodTable*)*(int*)(typeHandle + 6); // TODO: Is this correct for 32-bit?
                }

                long objectSize = mt->BaseSize;
                var flags = mt->Flags;
                bool hasComponentSize = (flags & 0x80000000) == 0x80000000;

                if (hasComponentSize)
                {
                    var numComponents = (long)*(int*)(objectId + IntPtr.Size);
                    objectSize += numComponents * mt->ComponentSize;
                }

                bool containsPointerOrCollectible = (flags & 0x10000000) == 0x10000000 || (flags & 0x1000000) == 0x1000000;
                if (containsPointerOrCollectible)
                {
                    var entries = *(int*)((byte*)mt - IntPtr.Size);
                    if (entries < 0)
                    {
                        entries -= entries;
                    }

                    var slots = 1 + entries * 2;

                    var gcdesc = new GCDesc(buffer, (byte*)mt - (slots * IntPtr.Size), slots * IntPtr.Size);

                    if (IntPtr.Size == 8)
                    {
                        gcdesc.FixupObject64(objectId, objectSize);
                    }
                    else
                    {
                        gcdesc.FixupObject32(objectId, objectSize);
                    }
                }

                if (IntPtr.Size == 8)
                {
                    *(long*)objectId = (long)mt;
                }
                else
                {
                    *(int*)objectId = (int)mt;
                }

                objectId += objectSize + Padding(objectSize, IntPtr.Size);
            }

            var tmp = buffer + IntPtr.Size;
            return Unsafe.Read<object>(&tmp);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Padding(long num, int align)
        {
            return (int)(0 - num & (align - 1));
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct MethodTable
        {
            [FieldOffset(0)]
            public ushort ComponentSize;

            [FieldOffset(0)]
            public uint Flags;

            [FieldOffset(4)]
            public int BaseSize;
        }

        internal unsafe struct GCDesc
        {
            private readonly byte* @base;

            private readonly IntPtr data;

            private readonly int size;

            public GCDesc(byte* @base, byte* data, int size)
            {
                this.@base = @base;
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

            public void FixupObject64(byte* addr, long size)
            {
                var series = this.GetNumSeries();
                var highest = this.GetHighestSeries();
                var curr = highest;

                if (series > 0)
                {
                    var lowest = this.GetLowestSeries();
                    do
                    {
                        var ptr = addr + this.GetSeriesOffset(curr);
                        var stop = ptr + this.GetSeriesSize(curr) + size;

                        while (ptr < stop)
                        {
                            var ret = *(ulong*)ptr;
                            if (ret != 0)
                            {
                                *(ulong*)ptr = (ulong)(this.@base + ret);
                            }

                            ptr += 8;
                        }

                        curr -= 8 * 2;
                    } while (curr >= lowest);
                }
                else
                {
                    var ptr = addr + this.GetSeriesOffset(curr);
                    while (ptr < addr + size - 8)
                    {
                        for (int i = 0; i > series; i--)
                        {
                            var nptrs = this.GetPointers(curr, i);
                            var skip = this.GetSkip(curr, i);

                            var stop = ptr + nptrs * (ulong)8;
                            do
                            {
                                var ret = *(ulong*)ptr;
                                if (ret != 0)
                                {
                                    *(ulong*)ptr = (ulong)(this.@base + ret);
                                }

                                ptr += 8;
                            } while (ptr < stop);

                            ptr += skip;
                        }
                    }
                }
            }

            public void FixupObject32(byte* addr, long size)
            {
                var series = this.GetNumSeries();
                var highest = this.GetHighestSeries();
                var curr = highest;

                if (series > 0)
                {
                    var lowest = this.GetLowestSeries();
                    do
                    {
                        var ptr = addr + this.GetSeriesOffset(curr);
                        var stop = ptr + this.GetSeriesSize(curr) + size;

                        while (ptr < stop)
                        {
                            var ret = *(uint*)ptr;
                            if (ret != 0)
                            {
                                *(uint*)ptr = (uint)(this.@base + ret);
                            }

                            ptr += 4;
                        }

                        curr -= 4 * 2;
                    } while (curr >= lowest);
                }
                else
                {
                    var ptr = addr + this.GetSeriesOffset(curr);
                    while (ptr < addr + size - 4)
                    {
                        for (int i = 0; i > series; i--)
                        {
                            var nptrs = this.GetPointers(curr, i);
                            var skip = this.GetSkip(curr, i);

                            var stop = ptr + nptrs * (ulong)4;
                            do
                            {
                                var ret = *(uint*)ptr;
                                if (ret != 0)
                                {
                                    *(uint*)ptr = (uint)(this.@base + ret);
                                }

                                ptr += 4;
                            } while (ptr < stop);

                            ptr += skip;
                        }
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int ComputeSize(int series)
            {
                return IntPtr.Size + series * IntPtr.Size * 2;
            }
        }
    }
}
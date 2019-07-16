namespace Microsoft.FrozenObjects
{
    using System;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;

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
            Marshal.WriteIntPtr((IntPtr)buffer, 0 - IntPtr.Size, InternalHelpers.RegisterFrozenSegment((IntPtr)buffer, (IntPtr)length));

            return retVal;
        }

        public static void UnloadFrozenObject(object o)
        {
            var optr = Marshal.ReadIntPtr((IntPtr)Unsafe.AsPointer(ref o));
            var baseAddress = optr - IntPtr.Size - IntPtr.Size - IntPtr.Size; // 3 because optr is actually pointing to MT*
            var length = Marshal.ReadIntPtr(baseAddress);
            var segmentHandle = Marshal.ReadIntPtr(baseAddress, IntPtr.Size);

            InternalHelpers.UnregisterFrozenSegment(segmentHandle);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                VirtualFree(baseAddress, IntPtr.Zero, FreeType.Release);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                munmap(baseAddress, length);
            }
            else
            {
                throw new PlatformNotSupportedException();
            }
        }

        private static object DeserializeInner(RuntimeTypeHandle[] runtimeTypeHandles, byte* buffer, long length)
        {
            byte* objectId = buffer + IntPtr.Size;

            while (objectId < buffer + length)
            {
                var typeHandle = runtimeTypeHandles[(int)Marshal.ReadIntPtr((IntPtr)objectId)].Value;
                bool isArray = (typeHandle.ToInt64() & 0x2) == 0x2;

                var mt = (MethodTable*)typeHandle;
                if (isArray)
                {
                    mt = (MethodTable*)Marshal.ReadIntPtr(typeHandle, 6);
                }

                var objectSize = mt->BaseSize;
                var flags = mt->Flags;
                bool hasComponentSize = (flags & 0x80000000) == 0x80000000;

                if (hasComponentSize)
                {
                    var numComponents = Marshal.ReadInt32((IntPtr)objectId, IntPtr.Size);
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

                Marshal.WriteIntPtr((IntPtr)objectId, (IntPtr)mt);
                objectId += objectSize + Padding(objectSize, IntPtr.Size);
            }

            var tmp = buffer + IntPtr.Size;
            return Unsafe.Read<object>(&tmp);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Padding(int num, int align)
        {
            return 0 - num & (align - 1);
        }

        private static byte* AllocateMemory(long allocationSize)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                byte* buffer = (byte*)VirtualAlloc(IntPtr.Zero, (IntPtr)allocationSize, AllocationType.Reserve | AllocationType.Commit, MemoryProtection.ReadWrite);
                return buffer;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                byte* buffer = (byte*)mmap(IntPtr.Zero, (IntPtr)allocationSize, MemoryMappedProtections.PROT_READ | MemoryMappedProtections.PROT_WRITE, MemoryMappedFlags.MAP_PRIVATE | MemoryMappedFlags.MAP_ANONYMOUS, -1, IntPtr.Zero);
                return buffer;
            }
            else
            {
                throw new PlatformNotSupportedException();
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr baseAddress, IntPtr size, AllocationType allocationType, MemoryProtection memoryProtection);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFree(IntPtr baseAddress, IntPtr size, FreeType freeType);

        [DllImport("libc", EntryPoint = "mmap", SetLastError = true)]
        private static extern IntPtr mmap(IntPtr addr, IntPtr length, MemoryMappedProtections prot, MemoryMappedFlags flags, int fd, IntPtr offset);

        [DllImport("libc", EntryPoint = "munmap", SetLastError = true)]
        private static extern int munmap(IntPtr addr, IntPtr length);

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

        [Flags]
        internal enum AllocationType
        {
            Commit = 0x1000,
            Reserve = 0x2000,
            Decommit = 0x4000,
            Release = 0x8000,
            Reset = 0x80000,
            Physical = 0x400000,
            TopDown = 0x100000,
            WriteWatch = 0x200000,
            LargePages = 0x20000000
        }

        [Flags]
        internal enum MemoryProtection
        {
            Execute = 0x10,
            ExecuteRead = 0x20,
            ExecuteReadWrite = 0x40,
            ExecuteWriteCopy = 0x80,
            NoAccess = 0x01,
            ReadOnly = 0x02,
            ReadWrite = 0x04,
            WriteCopy = 0x08,
            GuardModifierflag = 0x100,
            NoCacheModifierflag = 0x200,
            WriteCombineModifierflag = 0x400
        }

        [Flags]
        internal enum FreeType
        {
            Decommit = 0x4000,
            Release = 0x8000,
        }

        [Flags]
        internal enum MemoryMappedProtections
        {
            PROT_NONE = 0x0,
            PROT_READ = 0x1,
            PROT_WRITE = 0x2,
            PROT_EXEC = 0x4
        }

        [Flags]
        internal enum MemoryMappedFlags
        {
            MAP_SHARED = 0x01,
            MAP_PRIVATE = 0x02,
            MAP_ANONYMOUS = 0x20,
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

            public void FixupObject64(byte* addr, int size)
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

                            var stop = ptr + nptrs * 8;
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

            public void FixupObject32(byte* addr, int size)
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

                            var stop = ptr + nptrs * 4;
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
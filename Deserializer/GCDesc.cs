namespace Microsoft.FrozenObjects
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;

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
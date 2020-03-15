// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.FrozenObjects
{
    using System;
    using System.Runtime.InteropServices;

    internal static class NativeMethods
    {
        public static void FreeMemory(IntPtr baseAddress, IntPtr length)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                VirtualFree(baseAddress, IntPtr.Zero, FreeType.Release);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                munmap(baseAddress, length);
            }
            else
            {
                throw new PlatformNotSupportedException();
            }
        }

        public static unsafe byte* AllocateMemory(long allocationSize)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                byte* buffer = (byte*)VirtualAlloc(IntPtr.Zero, (IntPtr)allocationSize, AllocationType.Reserve | AllocationType.Commit, MemoryProtection.ReadWrite);

                return buffer;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                byte* buffer = (byte*)mmap(IntPtr.Zero, (IntPtr)allocationSize, MemoryMappedProtections.PROT_READ | MemoryMappedProtections.PROT_WRITE, (int)(MemoryMappedFlags.MAP_PRIVATE | MemoryMappedFlags.MAP_ANONYMOUS), -1, IntPtr.Zero);

                if (buffer == (void*)-1)
                {
                    buffer = (byte*)0;
                }

                return buffer;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                byte* buffer = (byte*)mmap(IntPtr.Zero, (IntPtr)allocationSize, MemoryMappedProtections.PROT_READ | MemoryMappedProtections.PROT_WRITE, (int)(MemoryMappedFlagsDarwin.MAP_PRIVATE | MemoryMappedFlagsDarwin.MAP_ANONYMOUS), -1, IntPtr.Zero);

                if (buffer == (void*)-1)
                {
                    buffer = (byte*)0;
                }

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
        private static extern IntPtr mmap(IntPtr addr, IntPtr length, MemoryMappedProtections prot, int flags, int fd, IntPtr offset);

        [DllImport("libc", EntryPoint = "munmap", SetLastError = true)]
        private static extern int munmap(IntPtr addr, IntPtr length);
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

    [Flags]
    internal enum MemoryMappedFlagsDarwin
    {
        MAP_SHARED = 0x01,
        MAP_PRIVATE = 0x02,
        MAP_ANONYMOUS = 0x1000,
    }
}

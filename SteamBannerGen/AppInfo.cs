using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ValveKeyValue;

namespace SteamBannerGen;

public class AppInfo {
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 20)]
    public record struct AppInfoHash {
        public ulong A;
        public ulong B;
        public uint C;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public record struct AppInfoHeader {
        public uint Magic;
        public uint Universe;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public record struct AppInfoEntry {
        public uint Size;
        public uint State;
        public uint LastUpdated;
        public ulong AccessToken;
        public AppInfoHash Hash;
        public uint ChangeNumber;
        public AppInfoHash DataHash;
    }

    public Dictionary<uint, (AppInfoEntry Entry, KVObject Data)> Entries { get; } = new();

    public unsafe AppInfo(Stream data) {
        Span<AppInfoHeader> header = stackalloc AppInfoHeader[1];
        Span<uint> appId = stackalloc uint[1];
        Span<AppInfoEntry> entries = stackalloc AppInfoEntry[1];

        data.ReadExactly(MemoryMarshal.AsBytes(header));

        if (header[0].Magic != 0x07564428) {
            throw new NotSupportedException("AppInfo file is not supported");
        }

        data.ReadExactly(MemoryMarshal.AsBytes(appId));
        if (appId[0] == 0) {
            return;
        }

        var structSize = Unsafe.SizeOf<AppInfoEntry>() - 4;

        do {
            data.ReadExactly(MemoryMarshal.AsBytes(entries));
            var entry = entries[0];
            var dataLength = entry.Size - structSize;
            using var rented = MemoryPool<byte>.Shared.Rent((int) dataLength);
            var kvData = rented.Memory[..(int) dataLength];
            data.ReadExactly(kvData.Span);
            using var pinned = kvData.Pin();
            using var unmanagedStream = new UnmanagedMemoryStream((byte*) pinned.Pointer, dataLength, dataLength, FileAccess.Read);
            try {
                Entries.Add(appId[0], (entry, KVSerializer.Create(KVSerializationFormat.KeyValues1Binary).Deserialize(unmanagedStream)));
            } catch {
                Console.WriteLine($"Failed to parse app info for {appId[0]}");
            }

            data.ReadExactly(MemoryMarshal.AsBytes(appId));
        } while (appId[0] != 0);
    }
}

// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Security.Cryptography;
using System.Text;

namespace FolderManaging.Shell.Com;

internal static class CommandGuids
{
    internal static Guid ForVerb(string verbId)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(verbId));
        Span<byte> g = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(g);
        g[6] = (byte)((g[6] & 0x0F) | 0x30);
        g[8] = (byte)((g[8] & 0x3F) | 0x80);
        return new Guid(g);
    }
}

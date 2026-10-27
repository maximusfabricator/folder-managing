// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace FolderManaging.Shell.Com;

[ComVisible(true)]
internal sealed class ExplorerCommandsEnumerator : Shell32.IEnumExplorerCommand
{
    private readonly Shell32.IExplorerCommand[] _items;
    private uint _index;

    internal ExplorerCommandsEnumerator(IReadOnlyList<Shell32.IExplorerCommand> items)
    {
        _items = items.ToArray();
    }

    private ExplorerCommandsEnumerator(Shell32.IExplorerCommand[] items, uint index)
    {
        _items = items;
        _index = index;
    }

    public HRESULT Next(uint celt, Shell32.IExplorerCommand[]? rgelt, out uint celtFetched)
    {
        celtFetched = 0;
        if (rgelt is null || rgelt.Length < celt)
        {
            return HRESULT.E_INVALIDARG;
        }

        while (celtFetched < celt && _index < _items.Length)
        {
            rgelt[celtFetched] = _items[_index];
            celtFetched++;
            _index++;
        }

        return celtFetched == celt ? HRESULT.S_OK : HRESULT.S_FALSE;
    }

    public HRESULT Skip(uint celt)
    {
        _index = Math.Min((uint)_items.Length, _index + celt);
        return HRESULT.S_OK;
    }

    public void Reset() => _index = 0;

    public Shell32.IEnumExplorerCommand Clone() => new ExplorerCommandsEnumerator(_items, _index);
}

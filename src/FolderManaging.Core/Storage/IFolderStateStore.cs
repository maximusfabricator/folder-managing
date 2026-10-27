// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core.Models;

namespace FolderManaging.Core.Storage;

public interface IFolderStateStore
{
    Task<AppStateDocument> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppStateDocument state, CancellationToken cancellationToken = default);
}

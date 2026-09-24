using FolderManaging.Core.Models;

namespace FolderManaging.Core.Storage;

public interface IFolderStateStore
{
    Task<AppStateDocument> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppStateDocument state, CancellationToken cancellationToken = default);
}

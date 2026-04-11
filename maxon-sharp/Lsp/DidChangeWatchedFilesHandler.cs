using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace MaxonSharp.Lsp;

public class DidChangeWatchedFilesHandler(LspServer server, ILanguageServerFacade facade) : IDidChangeWatchedFilesHandler {
  private readonly LspServer _server = server;
  private readonly ILanguageServerFacade _facade = facade;

  public Task<Unit> Handle(DidChangeWatchedFilesParams request, CancellationToken cancellationToken) {
    foreach (var change in request.Changes) {
      if (change.Type == FileChangeType.Deleted) {
        // Clear stale diagnostics for the deleted URI (no-op if nothing was published)
        _facade.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams {
          Uri = change.Uri,
          Diagnostics = new Container<Diagnostic>()
        });
        var path = change.Uri.GetFileSystemPath();
        if (LspServer.IsMaxonFilePath(path)) {
          _server.RemoveDocument(change.Uri);
        } else if (path != null) {
          // Directory (or other non-.maxon path) was deleted. VSCode does not
          // always report per-file deletes when a parent directory is removed,
          // so prune any tracked files under this path.
          _server.NotifyPathDeleted(path);
        }
      } else if (change.Type == FileChangeType.Changed || change.Type == FileChangeType.Created) {
        // Reload file from disk if it's not currently open in the editor
        _server.ReloadFromDiskIfNotOpen(change.Uri);
      }
    }
    return Unit.Task;
  }

  public DidChangeWatchedFilesRegistrationOptions GetRegistrationOptions(
    DidChangeWatchedFilesCapability capability, ClientCapabilities clientCapabilities
  ) => new() {
    Watchers = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher>(
      new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher {
        GlobPattern = new GlobPattern("**/*.maxon"),
        Kind = WatchKind.Create | WatchKind.Change | WatchKind.Delete
      },
      // Watch directory deletes so we can prune files that disappear because
      // an ancestor directory was removed (VSCode doesn't always send per-file
      // delete events for those).
      new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher {
        GlobPattern = new GlobPattern("**"),
        Kind = WatchKind.Delete
      }
    )
  };
}

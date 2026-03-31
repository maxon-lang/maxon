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
        // Clear stale diagnostics for files deleted on disk
        _facade.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams {
          Uri = change.Uri,
          Diagnostics = new Container<Diagnostic>()
        });
        _server.RemoveDocument(change.Uri);
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
      }
    )
  };
}

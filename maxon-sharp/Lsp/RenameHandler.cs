using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace MaxonSharp.Lsp;

public class RenameHandler(LspServer server) : RenameHandlerBase {
  private readonly LspServer _server = server;

  private readonly TextDocumentSelector _documentSelector = TextDocumentSelector.ForLanguage("maxon");

  public override Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken) {
    var edit = _server.GetRename(request.TextDocument.Uri, request.Position, request.NewName);
    return Task.FromResult(edit);
  }

  protected override RenameRegistrationOptions CreateRegistrationOptions(
    RenameCapability capability,
    ClientCapabilities clientCapabilities
  ) {
    return new RenameRegistrationOptions {
      DocumentSelector = _documentSelector,
      PrepareProvider = true
    };
  }
}

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace MaxonSharp.Lsp;

public class PrepareRenameHandler(LspServer server) : PrepareRenameHandlerBase {
  private readonly LspServer _server = server;

  private readonly TextDocumentSelector _documentSelector = TextDocumentSelector.ForLanguage("maxon");

  public override Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken cancellationToken) {
    var result = _server.PrepareRename(request.TextDocument.Uri, request.Position);
    return Task.FromResult(result);
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

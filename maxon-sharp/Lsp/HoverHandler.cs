using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace MaxonSharp.Lsp;

public class HoverHandler(LspServer server) : HoverHandlerBase {
  private readonly LspServer _server = server;

  private readonly TextDocumentSelector _documentSelector = TextDocumentSelector.ForLanguage("maxon");

  public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken) {
    var hover = _server.GetHover(request.TextDocument.Uri, request.Position);
    return Task.FromResult(hover);
  }

  protected override HoverRegistrationOptions CreateRegistrationOptions(
    HoverCapability capability,
    ClientCapabilities clientCapabilities
  ) {
    return new HoverRegistrationOptions {
      DocumentSelector = _documentSelector
    };
  }
}

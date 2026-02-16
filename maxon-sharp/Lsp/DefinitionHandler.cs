using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace MaxonSharp.Lsp;

public class DefinitionHandler(LspServer server) : DefinitionHandlerBase {
  private readonly LspServer _server = server;

  private readonly TextDocumentSelector _documentSelector = TextDocumentSelector.ForLanguage("maxon");

  public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken) {
    var location = _server.GetDefinition(request.TextDocument.Uri, request.Position);
    if (location == null)
      return Task.FromResult<LocationOrLocationLinks?>(null);

    return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(location));
  }

  protected override DefinitionRegistrationOptions CreateRegistrationOptions(
    DefinitionCapability capability,
    ClientCapabilities clientCapabilities
  ) {
    return new DefinitionRegistrationOptions {
      DocumentSelector = _documentSelector
    };
  }
}

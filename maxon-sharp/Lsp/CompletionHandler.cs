using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace MaxonSharp.Lsp;

public class CompletionHandler(LspServer server) : CompletionHandlerBase {
  private readonly LspServer _server = server;

  private readonly TextDocumentSelector _documentSelector = TextDocumentSelector.ForLanguage("maxon");

  public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken) {
    var completions = _server.GetCompletions(request.TextDocument.Uri, request.Position);
    return Task.FromResult(completions);
  }

  public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken) {
    return Task.FromResult(request);
  }

  protected override CompletionRegistrationOptions CreateRegistrationOptions(
    CompletionCapability capability,
    ClientCapabilities clientCapabilities
  ) {
    return new CompletionRegistrationOptions {
      DocumentSelector = _documentSelector,
      TriggerCharacters = new Container<string>(".", ":"),
      ResolveProvider = true
    };
  }
}

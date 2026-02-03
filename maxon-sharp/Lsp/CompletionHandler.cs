using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace MaxonSharp.Lsp;

public class CompletionHandler : CompletionHandlerBase {
  private readonly LspServer _server;

  private readonly TextDocumentSelector _documentSelector = TextDocumentSelector.ForLanguage("maxon");

  public CompletionHandler(LspServer server) {
    _server = server;
  }

  public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken) {
    var completions = LspServer.GetCompletions(request.TextDocument.Uri, request.Position);
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

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace MaxonSharp.Lsp;

public class CodeActionHandler(LspServer server) : CodeActionHandlerBase {
  private readonly LspServer _server = server;

  private readonly TextDocumentSelector _documentSelector = TextDocumentSelector.ForLanguage("maxon");

  public override Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken cancellationToken) {
    var actions = _server.GetCodeActions(request.TextDocument.Uri, request.Context);
    if (actions == null || actions.Count == 0)
      return Task.FromResult<CommandOrCodeActionContainer?>(null);
    return Task.FromResult<CommandOrCodeActionContainer?>(
      new CommandOrCodeActionContainer(actions.Select(a => new CommandOrCodeAction(a))));
  }

  public override Task<CodeAction> Handle(CodeAction request, CancellationToken cancellationToken) {
    // Edits are precomputed in the initial Handle, so resolve is a passthrough.
    return Task.FromResult(request);
  }

  protected override CodeActionRegistrationOptions CreateRegistrationOptions(
    CodeActionCapability capability,
    ClientCapabilities clientCapabilities
  ) {
    return new CodeActionRegistrationOptions {
      DocumentSelector = _documentSelector,
      CodeActionKinds = new Container<CodeActionKind>(CodeActionKind.QuickFix),
      ResolveProvider = false
    };
  }
}

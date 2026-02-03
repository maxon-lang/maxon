using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

namespace MaxonSharp.Lsp;

public class TextDocumentSyncHandler(LspServer server, ILanguageServerFacade facade) : TextDocumentSyncHandlerBase {
  private readonly LspServer _server = server;
  private readonly ILanguageServerFacade _facade = facade;

  private readonly TextDocumentSelector _documentSelector = TextDocumentSelector.ForLanguage("maxon");

  public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) {
    return new TextDocumentAttributes(uri, "maxon");
  }

  public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken) {
    _server.UpdateDocument(request.TextDocument.Uri, request.TextDocument.Text);
    PublishDiagnostics(request.TextDocument.Uri);
    return Unit.Task;
  }

  public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken) {
    // We use full sync, so just take the last change
    var text = request.ContentChanges.LastOrDefault()?.Text ?? "";
    _server.UpdateDocument(request.TextDocument.Uri, text);
    PublishDiagnostics(request.TextDocument.Uri);
    return Unit.Task;
  }

  public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken) {
    PublishDiagnostics(request.TextDocument.Uri);
    return Unit.Task;
  }

  public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken) {
    _server.RemoveDocument(request.TextDocument.Uri);
    // Clear diagnostics when document is closed
    _facade.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams {
      Uri = request.TextDocument.Uri,
      Diagnostics = new Container<Diagnostic>()
    });
    return Unit.Task;
  }

  private void PublishDiagnostics(DocumentUri uri) {
    var diagnostics = _server.GetDiagnostics(uri);
    _facade.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams {
      Uri = uri,
      Diagnostics = diagnostics
    });
  }

  protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
    TextSynchronizationCapability capability,
    ClientCapabilities clientCapabilities
  ) {
    return new TextDocumentSyncRegistrationOptions {
      DocumentSelector = _documentSelector,
      Change = TextDocumentSyncKind.Full,
      Save = new SaveOptions { IncludeText = true }
    };
  }
}

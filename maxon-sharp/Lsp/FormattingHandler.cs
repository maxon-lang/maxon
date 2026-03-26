using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace MaxonSharp.Lsp;

public class FormattingHandler(LspServer server) : DocumentFormattingHandlerBase {
  private readonly LspServer _server = server;

  private readonly TextDocumentSelector _documentSelector = TextDocumentSelector.ForLanguage("maxon");

  public override Task<TextEditContainer?> Handle(DocumentFormattingParams request, CancellationToken cancellationToken) {
    var content = _server.GetDocument(request.TextDocument.Uri);
    if (content == null) return Task.FromResult<TextEditContainer?>(null);

    var useTabs = !request.Options.InsertSpaces;
    var tabSize = (int)request.Options.TabSize;
    var formatted = MaxonFormatter.Format(content, indentSize: tabSize, useTabs: useTabs);

    if (formatted == content) return Task.FromResult<TextEditContainer?>(null);

    // Replace the entire document with one edit
    var lines = content.Split('\n');
    var edit = new TextEdit {
      Range = new Range(0, 0, lines.Length - 1, lines[^1].Length),
      NewText = formatted,
    };

    return Task.FromResult<TextEditContainer?>(new TextEditContainer(edit));
  }

  protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(
    DocumentFormattingCapability capability,
    ClientCapabilities clientCapabilities
  ) {
    return new DocumentFormattingRegistrationOptions {
      DocumentSelector = _documentSelector,
    };
  }
}

#include "lsp_json.h"

namespace maxon::lsp {

// =============================================================================
// Helper implementations
// =============================================================================

template <typename T>
void addOptional(json &j, const std::string &key, const std::optional<T> &opt) {
	if (opt.has_value()) {
		j[key] = toJson(opt.value());
	}
}

// Specialization for basic types
template <>
void addOptional<std::string>(json &j, const std::string &key, const std::optional<std::string> &opt) {
	if (opt.has_value()) {
		j[key] = opt.value();
	}
}

template <>
void addOptional<int>(json &j, const std::string &key, const std::optional<int> &opt) {
	if (opt.has_value()) {
		j[key] = opt.value();
	}
}

template <>
void addOptional<bool>(json &j, const std::string &key, const std::optional<bool> &opt) {
	if (opt.has_value()) {
		j[key] = opt.value();
	}
}

// =============================================================================
// Position and Range Types
// =============================================================================

json toJson(const Position &pos) {
	return json{{"line", pos.line}, {"character", pos.character}};
}

Position positionFromJson(const json &j) {
	Position pos;
	pos.line = j.value("line", 0);
	pos.character = j.value("character", 0);
	return pos;
}

json toJson(const Range &range) {
	return json{{"start", toJson(range.start)}, {"end", toJson(range.end)}};
}

Range rangeFromJson(const json &j) {
	Range range;
	if (j.contains("start"))
		range.start = positionFromJson(j["start"]);
	if (j.contains("end"))
		range.end = positionFromJson(j["end"]);
	return range;
}

json toJson(const Location &loc) {
	return json{{"uri", loc.uri}, {"range", toJson(loc.range)}};
}

Location locationFromJson(const json &j) {
	Location loc;
	loc.uri = j.value("uri", "");
	if (j.contains("range"))
		loc.range = rangeFromJson(j["range"]);
	return loc;
}

json toJson(const LocationLink &link) {
	json j;
	j["targetUri"] = link.targetUri;
	j["targetRange"] = toJson(link.targetRange);
	j["targetSelectionRange"] = toJson(link.targetSelectionRange);
	if (link.originSelectionRange.has_value()) {
		j["originSelectionRange"] = toJson(link.originSelectionRange.value());
	}
	return j;
}

LocationLink locationLinkFromJson(const json &j) {
	LocationLink link;
	link.targetUri = j.value("targetUri", "");
	if (j.contains("targetRange"))
		link.targetRange = rangeFromJson(j["targetRange"]);
	if (j.contains("targetSelectionRange"))
		link.targetSelectionRange = rangeFromJson(j["targetSelectionRange"]);
	if (j.contains("originSelectionRange"))
		link.originSelectionRange = rangeFromJson(j["originSelectionRange"]);
	return link;
}

// =============================================================================
// Document Types
// =============================================================================

json toJson(const TextDocumentIdentifier &id) {
	return json{{"uri", id.uri}};
}

TextDocumentIdentifier textDocumentIdentifierFromJson(const json &j) {
	TextDocumentIdentifier id;
	id.uri = j.value("uri", "");
	return id;
}

json toJson(const VersionedTextDocumentIdentifier &id) {
	return json{{"uri", id.uri}, {"version", id.version}};
}

VersionedTextDocumentIdentifier versionedTextDocumentIdentifierFromJson(const json &j) {
	VersionedTextDocumentIdentifier id;
	id.uri = j.value("uri", "");
	id.version = j.value("version", 0);
	return id;
}

json toJson(const OptionalVersionedTextDocumentIdentifier &id) {
	json j{{"uri", id.uri}};
	if (id.version.has_value()) {
		j["version"] = id.version.value();
	}
	return j;
}

OptionalVersionedTextDocumentIdentifier optionalVersionedTextDocumentIdentifierFromJson(const json &j) {
	OptionalVersionedTextDocumentIdentifier id;
	id.uri = j.value("uri", "");
	if (j.contains("version") && !j["version"].is_null()) {
		id.version = j["version"].get<int>();
	}
	return id;
}

json toJson(const TextDocumentItem &item) {
	return json{
		{"uri", item.uri},
		{"languageId", item.languageId},
		{"version", item.version},
		{"text", item.text}};
}

TextDocumentItem textDocumentItemFromJson(const json &j) {
	TextDocumentItem item;
	item.uri = j.value("uri", "");
	item.languageId = j.value("languageId", "");
	item.version = j.value("version", 0);
	item.text = j.value("text", "");
	return item;
}

json toJson(const TextDocumentContentChangeEvent &event) {
	json j{{"text", event.text}};
	if (event.range.has_value()) {
		j["range"] = toJson(event.range.value());
	}
	if (event.rangeLength.has_value()) {
		j["rangeLength"] = event.rangeLength.value();
	}
	return j;
}

TextDocumentContentChangeEvent textDocumentContentChangeEventFromJson(const json &j) {
	TextDocumentContentChangeEvent event;
	event.text = j.value("text", "");
	if (j.contains("range") && !j["range"].is_null()) {
		event.range = rangeFromJson(j["range"]);
	}
	if (j.contains("rangeLength") && !j["rangeLength"].is_null()) {
		event.rangeLength = j["rangeLength"].get<int>();
	}
	return event;
}

json toJson(const TextDocumentPositionParams &params) {
	return json{
		{"textDocument", toJson(params.textDocument)},
		{"position", toJson(params.position)}};
}

TextDocumentPositionParams textDocumentPositionParamsFromJson(const json &j) {
	TextDocumentPositionParams params;
	if (j.contains("textDocument"))
		params.textDocument = textDocumentIdentifierFromJson(j["textDocument"]);
	if (j.contains("position"))
		params.position = positionFromJson(j["position"]);
	return params;
}

// =============================================================================
// Diagnostic Types
// =============================================================================

json toJson(DiagnosticSeverity severity) {
	return static_cast<int>(severity);
}

DiagnosticSeverity diagnosticSeverityFromJson(const json &j) {
	return static_cast<DiagnosticSeverity>(j.get<int>());
}

json toJson(const Diagnostic &diag) {
	json j{
		{"range", toJson(diag.range)},
		{"message", diag.message}};
	if (diag.severity.has_value()) {
		j["severity"] = toJson(diag.severity.value());
	}
	if (diag.code.has_value()) {
		const auto &code = diag.code.value();
		if (std::holds_alternative<int>(code)) {
			j["code"] = std::get<int>(code);
		} else {
			j["code"] = std::get<std::string>(code);
		}
	}
	if (diag.source.has_value()) {
		j["source"] = diag.source.value();
	}
	return j;
}

Diagnostic diagnosticFromJson(const json &j) {
	Diagnostic diag;
	if (j.contains("range"))
		diag.range = rangeFromJson(j["range"]);
	diag.message = j.value("message", "");
	if (j.contains("severity"))
		diag.severity = diagnosticSeverityFromJson(j["severity"]);
	if (j.contains("code") && !j["code"].is_null()) {
		if (j["code"].is_string()) {
			diag.code = j["code"].get<std::string>();
		} else {
			diag.code = std::to_string(j["code"].get<int>());
		}
	}
	if (j.contains("source") && !j["source"].is_null()) {
		diag.source = j["source"].get<std::string>();
	}
	return diag;
}

json toJson(const PublishDiagnosticsParams &params) {
	json j{
		{"uri", params.uri}};
	json diagnostics = json::array();
	for (const auto &diag : params.diagnostics) {
		diagnostics.push_back(toJson(diag));
	}
	j["diagnostics"] = diagnostics;
	if (params.version.has_value()) {
		j["version"] = params.version.value();
	}
	return j;
}

PublishDiagnosticsParams publishDiagnosticsParamsFromJson(const json &j) {
	PublishDiagnosticsParams params;
	params.uri = j.value("uri", "");
	if (j.contains("diagnostics")) {
		for (const auto &d : j["diagnostics"]) {
			params.diagnostics.push_back(diagnosticFromJson(d));
		}
	}
	if (j.contains("version") && !j["version"].is_null()) {
		params.version = j["version"].get<int>();
	}
	return params;
}

// =============================================================================
// Text Edit Types
// =============================================================================

json toJson(const TextEdit &edit) {
	return json{
		{"range", toJson(edit.range)},
		{"newText", edit.newText}};
}

TextEdit textEditFromJson(const json &j) {
	TextEdit edit;
	if (j.contains("range"))
		edit.range = rangeFromJson(j["range"]);
	edit.newText = j.value("newText", "");
	return edit;
}

json toJson(const WorkspaceEdit &edit) {
	json j;
	if (edit.changes.has_value() && !edit.changes->empty()) {
		json changes;
		for (const auto &[uri, edits] : *edit.changes) {
			json editArray = json::array();
			for (const auto &e : edits) {
				editArray.push_back(toJson(e));
			}
			changes[uri] = editArray;
		}
		j["changes"] = changes;
	}
	return j;
}

WorkspaceEdit workspaceEditFromJson(const json &j) {
	WorkspaceEdit edit;
	if (j.contains("changes")) {
		std::map<std::string, std::vector<TextEdit>> changesMap;
		for (const auto &[uri, edits] : j["changes"].items()) {
			std::vector<TextEdit> textEdits;
			for (const auto &e : edits) {
				textEdits.push_back(textEditFromJson(e));
			}
			changesMap[uri] = textEdits;
		}
		edit.changes = changesMap;
	}
	return edit;
}

// =============================================================================
// Completion Types
// =============================================================================

json toJson(CompletionItemKind kind) {
	return static_cast<int>(kind);
}

CompletionItemKind completionItemKindFromJson(const json &j) {
	return static_cast<CompletionItemKind>(j.get<int>());
}

json toJson(InsertTextFormat format) {
	return static_cast<int>(format);
}

InsertTextFormat insertTextFormatFromJson(const json &j) {
	return static_cast<InsertTextFormat>(j.get<int>());
}

json toJson(MarkupKind kind) {
	return kind == MarkupKind::Markdown ? "markdown" : "plaintext";
}

MarkupKind markupKindFromJson(const json &j) {
	std::string s = j.get<std::string>();
	return s == "markdown" ? MarkupKind::Markdown : MarkupKind::PlainText;
}

json toJson(const MarkupContent &content) {
	return json{
		{"kind", toJson(content.kind)},
		{"value", content.value}};
}

MarkupContent markupContentFromJson(const json &j) {
	MarkupContent content;
	if (j.contains("kind"))
		content.kind = markupKindFromJson(j["kind"]);
	content.value = j.value("value", "");
	return content;
}

json toJson(const CompletionItem &item) {
	json j{{"label", item.label}};
	if (item.kind.has_value())
		j["kind"] = toJson(item.kind.value());
	if (item.detail.has_value())
		j["detail"] = item.detail.value();
	if (item.documentation.has_value()) {
		const auto &doc = item.documentation.value();
		if (std::holds_alternative<std::string>(doc)) {
			j["documentation"] = std::get<std::string>(doc);
		} else {
			j["documentation"] = toJson(std::get<MarkupContent>(doc));
		}
	}
	if (item.insertText.has_value())
		j["insertText"] = item.insertText.value();
	if (item.insertTextFormat.has_value())
		j["insertTextFormat"] = toJson(item.insertTextFormat.value());
	if (item.filterText.has_value())
		j["filterText"] = item.filterText.value();
	if (item.sortText.has_value())
		j["sortText"] = item.sortText.value();
	return j;
}

CompletionItem completionItemFromJson(const json &j) {
	CompletionItem item;
	item.label = j.value("label", "");
	if (j.contains("kind"))
		item.kind = completionItemKindFromJson(j["kind"]);
	if (j.contains("detail"))
		item.detail = j["detail"].get<std::string>();
	if (j.contains("insertText"))
		item.insertText = j["insertText"].get<std::string>();
	if (j.contains("insertTextFormat"))
		item.insertTextFormat = insertTextFormatFromJson(j["insertTextFormat"]);
	if (j.contains("filterText"))
		item.filterText = j["filterText"].get<std::string>();
	if (j.contains("sortText"))
		item.sortText = j["sortText"].get<std::string>();
	return item;
}

json toJson(const CompletionList &list) {
	json j{{"isIncomplete", list.isIncomplete}};
	json items = json::array();
	for (const auto &item : list.items) {
		items.push_back(toJson(item));
	}
	j["items"] = items;
	return j;
}

CompletionList completionListFromJson(const json &j) {
	CompletionList list;
	list.isIncomplete = j.value("isIncomplete", false);
	if (j.contains("items")) {
		for (const auto &item : j["items"]) {
			list.items.push_back(completionItemFromJson(item));
		}
	}
	return list;
}

json toJson(const CompletionParams &params) {
	json j{
		{"textDocument", toJson(params.textDocument)},
		{"position", toJson(params.position)}};
	return j;
}

CompletionParams completionParamsFromJson(const json &j) {
	CompletionParams params;
	auto base = textDocumentPositionParamsFromJson(j);
	params.textDocument = base.textDocument;
	params.position = base.position;
	return params;
}

// =============================================================================
// Hover Types
// =============================================================================

json toJson(const Hover &hover) {
	json j;
	if (std::holds_alternative<std::string>(hover.contents)) {
		j["contents"] = std::get<std::string>(hover.contents);
	} else {
		j["contents"] = toJson(std::get<MarkupContent>(hover.contents));
	}
	if (hover.range.has_value()) {
		j["range"] = toJson(hover.range.value());
	}
	return j;
}

Hover hoverFromJson(const json &j) {
	Hover hover;
	if (j.contains("contents")) {
		if (j["contents"].is_string()) {
			hover.contents = j["contents"].get<std::string>();
		} else {
			hover.contents = markupContentFromJson(j["contents"]);
		}
	}
	if (j.contains("range")) {
		hover.range = rangeFromJson(j["range"]);
	}
	return hover;
}

json toJson(const HoverParams &params) {
	return json{
		{"textDocument", toJson(params.textDocument)},
		{"position", toJson(params.position)}};
}

HoverParams hoverParamsFromJson(const json &j) {
	HoverParams params;
	auto base = textDocumentPositionParamsFromJson(j);
	params.textDocument = base.textDocument;
	params.position = base.position;
	return params;
}

// =============================================================================
// Signature Help Types
// =============================================================================

json toJson(const ParameterInformation &param) {
	json j;
	if (std::holds_alternative<std::string>(param.label)) {
		j["label"] = std::get<std::string>(param.label);
	} else {
		j["label"] = std::get<std::pair<int, int>>(param.label);
	}
	if (param.documentation.has_value()) {
		const auto &doc = param.documentation.value();
		if (std::holds_alternative<std::string>(doc)) {
			j["documentation"] = std::get<std::string>(doc);
		} else {
			j["documentation"] = toJson(std::get<MarkupContent>(doc));
		}
	}
	return j;
}

ParameterInformation parameterInformationFromJson(const json &j) {
	ParameterInformation param;
	if (j.contains("label")) {
		if (j["label"].is_string()) {
			param.label = j["label"].get<std::string>();
		} else if (j["label"].is_array()) {
			param.label = std::make_pair(j["label"][0].get<int>(), j["label"][1].get<int>());
		}
	}
	return param;
}

json toJson(const SignatureInformation &sig) {
	json j{{"label", sig.label}};
	if (sig.documentation.has_value()) {
		const auto &doc = sig.documentation.value();
		if (std::holds_alternative<std::string>(doc)) {
			j["documentation"] = std::get<std::string>(doc);
		} else {
			j["documentation"] = toJson(std::get<MarkupContent>(doc));
		}
	}
	if (sig.parameters.has_value() && !sig.parameters->empty()) {
		json params = json::array();
		for (const auto &p : *sig.parameters) {
			params.push_back(toJson(p));
		}
		j["parameters"] = params;
	}
	if (sig.activeParameter.has_value()) {
		j["activeParameter"] = sig.activeParameter.value();
	}
	return j;
}

SignatureInformation signatureInformationFromJson(const json &j) {
	SignatureInformation sig;
	sig.label = j.value("label", "");
	if (j.contains("parameters")) {
		std::vector<ParameterInformation> params;
		for (const auto &p : j["parameters"]) {
			params.push_back(parameterInformationFromJson(p));
		}
		sig.parameters = params;
	}
	if (j.contains("activeParameter")) {
		sig.activeParameter = j["activeParameter"].get<int>();
	}
	return sig;
}

json toJson(const SignatureHelp &help) {
	json j;
	json signatures = json::array();
	for (const auto &sig : help.signatures) {
		signatures.push_back(toJson(sig));
	}
	j["signatures"] = signatures;
	if (help.activeSignature.has_value()) {
		j["activeSignature"] = help.activeSignature.value();
	}
	if (help.activeParameter.has_value()) {
		j["activeParameter"] = help.activeParameter.value();
	}
	return j;
}

SignatureHelp signatureHelpFromJson(const json &j) {
	SignatureHelp help;
	if (j.contains("signatures")) {
		for (const auto &sig : j["signatures"]) {
			help.signatures.push_back(signatureInformationFromJson(sig));
		}
	}
	if (j.contains("activeSignature")) {
		help.activeSignature = j["activeSignature"].get<int>();
	}
	if (j.contains("activeParameter")) {
		help.activeParameter = j["activeParameter"].get<int>();
	}
	return help;
}

json toJson(const SignatureHelpParams &params) {
	return json{
		{"textDocument", toJson(params.textDocument)},
		{"position", toJson(params.position)}};
}

SignatureHelpParams signatureHelpParamsFromJson(const json &j) {
	SignatureHelpParams params;
	auto base = textDocumentPositionParamsFromJson(j);
	params.textDocument = base.textDocument;
	params.position = base.position;
	return params;
}

// =============================================================================
// Symbol Types
// =============================================================================

json toJson(SymbolKind kind) {
	return static_cast<int>(kind);
}

SymbolKind symbolKindFromJson(const json &j) {
	return static_cast<SymbolKind>(j.get<int>());
}

json toJson(const DocumentSymbol &symbol) {
	json j{
		{"name", symbol.name},
		{"kind", toJson(symbol.kind)},
		{"range", toJson(symbol.range)},
		{"selectionRange", toJson(symbol.selectionRange)}};
	if (symbol.detail.has_value()) {
		j["detail"] = symbol.detail.value();
	}
	if (symbol.children.has_value() && !symbol.children->empty()) {
		json children = json::array();
		for (const auto &child : *symbol.children) {
			children.push_back(toJson(child));
		}
		j["children"] = children;
	}
	return j;
}

DocumentSymbol documentSymbolFromJson(const json &j) {
	DocumentSymbol symbol;
	symbol.name = j.value("name", "");
	if (j.contains("kind"))
		symbol.kind = symbolKindFromJson(j["kind"]);
	if (j.contains("range"))
		symbol.range = rangeFromJson(j["range"]);
	if (j.contains("selectionRange"))
		symbol.selectionRange = rangeFromJson(j["selectionRange"]);
	if (j.contains("detail"))
		symbol.detail = j["detail"].get<std::string>();
	if (j.contains("children")) {
		std::vector<DocumentSymbol> children;
		for (const auto &child : j["children"]) {
			children.push_back(documentSymbolFromJson(child));
		}
		symbol.children = children;
	}
	return symbol;
}

json toJson(const DocumentSymbolParams &params) {
	return json{{"textDocument", toJson(params.textDocument)}};
}

DocumentSymbolParams documentSymbolParamsFromJson(const json &j) {
	DocumentSymbolParams params;
	if (j.contains("textDocument")) {
		params.textDocument = textDocumentIdentifierFromJson(j["textDocument"]);
	}
	return params;
}

// =============================================================================
// Definition/References Types
// =============================================================================

json toJson(const DefinitionParams &params) {
	return json{
		{"textDocument", toJson(params.textDocument)},
		{"position", toJson(params.position)}};
}

DefinitionParams definitionParamsFromJson(const json &j) {
	DefinitionParams params;
	auto base = textDocumentPositionParamsFromJson(j);
	params.textDocument = base.textDocument;
	params.position = base.position;
	return params;
}

json toJson(const ReferenceContext &ctx) {
	return json{{"includeDeclaration", ctx.includeDeclaration}};
}

ReferenceContext referenceContextFromJson(const json &j) {
	ReferenceContext ctx;
	ctx.includeDeclaration = j.value("includeDeclaration", false);
	return ctx;
}

json toJson(const ReferenceParams &params) {
	json j{
		{"textDocument", toJson(params.textDocument)},
		{"position", toJson(params.position)}};
	j["context"] = toJson(params.context);
	return j;
}

ReferenceParams referenceParamsFromJson(const json &j) {
	ReferenceParams params;
	auto base = textDocumentPositionParamsFromJson(j);
	params.textDocument = base.textDocument;
	params.position = base.position;
	if (j.contains("context")) {
		params.context = referenceContextFromJson(j["context"]);
	}
	return params;
}

// =============================================================================
// Rename Types
// =============================================================================

json toJson(const PrepareRenameResult &result) {
	json j{{"range", toJson(result.range)}};
	if (result.placeholder.has_value()) {
		j["placeholder"] = result.placeholder.value();
	}
	return j;
}

PrepareRenameResult prepareRenameResultFromJson(const json &j) {
	PrepareRenameResult result;
	if (j.contains("range"))
		result.range = rangeFromJson(j["range"]);
	if (j.contains("placeholder"))
		result.placeholder = j["placeholder"].get<std::string>();
	return result;
}

json toJson(const RenameParams &params) {
	json j{
		{"textDocument", toJson(params.textDocument)},
		{"position", toJson(params.position)}};
	j["newName"] = params.newName;
	return j;
}

RenameParams renameParamsFromJson(const json &j) {
	RenameParams params;
	auto base = textDocumentPositionParamsFromJson(j);
	params.textDocument = base.textDocument;
	params.position = base.position;
	params.newName = j.value("newName", "");
	return params;
}

// =============================================================================
// Code Action Types
// =============================================================================

json toJson(const CodeActionContext &ctx) {
	json j;
	json diagnostics = json::array();
	for (const auto &diag : ctx.diagnostics) {
		diagnostics.push_back(toJson(diag));
	}
	j["diagnostics"] = diagnostics;
	return j;
}

CodeActionContext codeActionContextFromJson(const json &j) {
	CodeActionContext ctx;
	if (j.contains("diagnostics")) {
		for (const auto &d : j["diagnostics"]) {
			ctx.diagnostics.push_back(diagnosticFromJson(d));
		}
	}
	return ctx;
}

json toJson(const CodeAction &action) {
	json j{
		{"title", action.title}};
	if (action.kind.has_value()) {
		j["kind"] = action.kind.value();
	}
	if (action.diagnostics.has_value() && !action.diagnostics->empty()) {
		json diags = json::array();
		for (const auto &d : *action.diagnostics) {
			diags.push_back(toJson(d));
		}
		j["diagnostics"] = diags;
	}
	if (action.edit.has_value()) {
		j["edit"] = toJson(action.edit.value());
	}
	return j;
}

CodeAction codeActionFromJson(const json &j) {
	CodeAction action;
	action.title = j.value("title", "");
	if (j.contains("kind"))
		action.kind = j["kind"].get<std::string>();
	if (j.contains("diagnostics")) {
		std::vector<Diagnostic> diags;
		for (const auto &d : j["diagnostics"]) {
			diags.push_back(diagnosticFromJson(d));
		}
		action.diagnostics = diags;
	}
	if (j.contains("edit")) {
		action.edit = workspaceEditFromJson(j["edit"]);
	}
	return action;
}

json toJson(const CodeActionParams &params) {
	return json{
		{"textDocument", toJson(params.textDocument)},
		{"range", toJson(params.range)},
		{"context", toJson(params.context)}};
}

CodeActionParams codeActionParamsFromJson(const json &j) {
	CodeActionParams params;
	if (j.contains("textDocument"))
		params.textDocument = textDocumentIdentifierFromJson(j["textDocument"]);
	if (j.contains("range"))
		params.range = rangeFromJson(j["range"]);
	if (j.contains("context"))
		params.context = codeActionContextFromJson(j["context"]);
	return params;
}

// =============================================================================
// Formatting Types
// =============================================================================

json toJson(const FormattingOptions &options) {
	json j{
		{"tabSize", options.tabSize},
		{"insertSpaces", options.insertSpaces}};
	if (options.trimTrailingWhitespace.has_value()) {
		j["trimTrailingWhitespace"] = options.trimTrailingWhitespace.value();
	}
	if (options.insertFinalNewline.has_value()) {
		j["insertFinalNewline"] = options.insertFinalNewline.value();
	}
	if (options.trimFinalNewlines.has_value()) {
		j["trimFinalNewlines"] = options.trimFinalNewlines.value();
	}
	return j;
}

FormattingOptions formattingOptionsFromJson(const json &j) {
	FormattingOptions options;
	options.tabSize = j.value("tabSize", 4);
	options.insertSpaces = j.value("insertSpaces", true);
	if (j.contains("trimTrailingWhitespace")) {
		options.trimTrailingWhitespace = j["trimTrailingWhitespace"].get<bool>();
	}
	if (j.contains("insertFinalNewline")) {
		options.insertFinalNewline = j["insertFinalNewline"].get<bool>();
	}
	if (j.contains("trimFinalNewlines")) {
		options.trimFinalNewlines = j["trimFinalNewlines"].get<bool>();
	}
	return options;
}

json toJson(const DocumentFormattingParams &params) {
	return json{
		{"textDocument", toJson(params.textDocument)},
		{"options", toJson(params.options)}};
}

DocumentFormattingParams documentFormattingParamsFromJson(const json &j) {
	DocumentFormattingParams params;
	if (j.contains("textDocument"))
		params.textDocument = textDocumentIdentifierFromJson(j["textDocument"]);
	if (j.contains("options"))
		params.options = formattingOptionsFromJson(j["options"]);
	return params;
}

// =============================================================================
// Folding Range Types
// =============================================================================

json toJson(const FoldingRange &range) {
	json j{
		{"startLine", range.startLine},
		{"endLine", range.endLine}};
	if (range.startCharacter.has_value()) {
		j["startCharacter"] = range.startCharacter.value();
	}
	if (range.endCharacter.has_value()) {
		j["endCharacter"] = range.endCharacter.value();
	}
	if (range.kind.has_value()) {
		j["kind"] = range.kind.value();
	}
	if (range.collapsedText.has_value()) {
		j["collapsedText"] = range.collapsedText.value();
	}
	return j;
}

FoldingRange foldingRangeFromJson(const json &j) {
	FoldingRange range;
	range.startLine = j.value("startLine", 0);
	range.endLine = j.value("endLine", 0);
	if (j.contains("startCharacter"))
		range.startCharacter = j["startCharacter"].get<int>();
	if (j.contains("endCharacter"))
		range.endCharacter = j["endCharacter"].get<int>();
	if (j.contains("kind"))
		range.kind = j["kind"].get<std::string>();
	if (j.contains("collapsedText"))
		range.collapsedText = j["collapsedText"].get<std::string>();
	return range;
}

json toJson(const FoldingRangeParams &params) {
	return json{{"textDocument", toJson(params.textDocument)}};
}

FoldingRangeParams foldingRangeParamsFromJson(const json &j) {
	FoldingRangeParams params;
	if (j.contains("textDocument"))
		params.textDocument = textDocumentIdentifierFromJson(j["textDocument"]);
	return params;
}

// =============================================================================
// Linked Editing Types
// =============================================================================

json toJson(const LinkedEditingRanges &ranges) {
	json j;
	json rangesJson = json::array();
	for (const auto &r : ranges.ranges) {
		rangesJson.push_back(toJson(r));
	}
	j["ranges"] = rangesJson;
	if (ranges.wordPattern.has_value()) {
		j["wordPattern"] = ranges.wordPattern.value();
	}
	return j;
}

LinkedEditingRanges linkedEditingRangesFromJson(const json &j) {
	LinkedEditingRanges ranges;
	if (j.contains("ranges")) {
		for (const auto &r : j["ranges"]) {
			ranges.ranges.push_back(rangeFromJson(r));
		}
	}
	if (j.contains("wordPattern")) {
		ranges.wordPattern = j["wordPattern"].get<std::string>();
	}
	return ranges;
}

json toJson(const LinkedEditingRangeParams &params) {
	return json{
		{"textDocument", toJson(params.textDocument)},
		{"position", toJson(params.position)}
	};
}

LinkedEditingRangeParams linkedEditingRangeParamsFromJson(const json &j) {
	LinkedEditingRangeParams params;
	if (j.contains("textDocument"))
		params.textDocument = textDocumentIdentifierFromJson(j["textDocument"]);
	if (j.contains("position"))
		params.position = positionFromJson(j["position"]);
	return params;
}

// =============================================================================
// Initialization Types
// =============================================================================

json toJson(TextDocumentSyncKind kind) {
	return static_cast<int>(kind);
}

TextDocumentSyncKind textDocumentSyncKindFromJson(const json &j) {
	return static_cast<TextDocumentSyncKind>(j.get<int>());
}

json toJson(const TextDocumentSyncOptions &options) {
	json j;
	if (options.openClose.has_value())
		j["openClose"] = options.openClose.value();
	if (options.change.has_value())
		j["change"] = toJson(options.change.value());
	if (options.save.has_value()) {
		const auto &save = options.save.value();
		json saveJson;
		if (save.includeText.has_value()) {
			saveJson["includeText"] = save.includeText.value();
		}
		j["save"] = saveJson;
	}
	return j;
}

TextDocumentSyncOptions textDocumentSyncOptionsFromJson(const json &j) {
	TextDocumentSyncOptions options;
	if (j.contains("openClose"))
		options.openClose = j["openClose"].get<bool>();
	if (j.contains("change"))
		options.change = textDocumentSyncKindFromJson(j["change"]);
	if (j.contains("save")) {
		TextDocumentSyncOptions::SaveOptions save;
		save.includeText = j["save"].value("includeText", false);
		options.save = save;
	}
	return options;
}

json toJson(const CompletionOptions &options) {
	json j;
	if (options.triggerCharacters.has_value() && !options.triggerCharacters->empty()) {
		j["triggerCharacters"] = *options.triggerCharacters;
	}
	if (options.resolveProvider.has_value()) {
		j["resolveProvider"] = options.resolveProvider.value();
	}
	return j;
}

CompletionOptions completionOptionsFromJson(const json &j) {
	CompletionOptions options;
	if (j.contains("triggerCharacters")) {
		options.triggerCharacters = j["triggerCharacters"].get<std::vector<std::string>>();
	}
	if (j.contains("resolveProvider")) {
		options.resolveProvider = j["resolveProvider"].get<bool>();
	}
	return options;
}

json toJson(const SignatureHelpOptions &options) {
	json j;
	if (options.triggerCharacters.has_value() && !options.triggerCharacters->empty()) {
		j["triggerCharacters"] = *options.triggerCharacters;
	}
	if (options.retriggerCharacters.has_value() && !options.retriggerCharacters->empty()) {
		j["retriggerCharacters"] = *options.retriggerCharacters;
	}
	return j;
}

SignatureHelpOptions signatureHelpOptionsFromJson(const json &j) {
	SignatureHelpOptions options;
	if (j.contains("triggerCharacters")) {
		options.triggerCharacters = j["triggerCharacters"].get<std::vector<std::string>>();
	}
	if (j.contains("retriggerCharacters")) {
		options.retriggerCharacters = j["retriggerCharacters"].get<std::vector<std::string>>();
	}
	return options;
}

json toJson(const RenameOptions &options) {
	json j;
	if (options.prepareProvider.has_value()) {
		j["prepareProvider"] = options.prepareProvider.value();
	}
	return j;
}

RenameOptions renameOptionsFromJson(const json &j) {
	RenameOptions options;
	if (j.contains("prepareProvider")) {
		options.prepareProvider = j["prepareProvider"].get<bool>();
	}
	return options;
}

json toJson(const ServerCapabilities &caps) {
	json j;

	// Text document sync
	if (caps.textDocumentSync.has_value()) {
		const auto &sync = caps.textDocumentSync.value();
		if (std::holds_alternative<TextDocumentSyncKind>(sync)) {
			j["textDocumentSync"] = toJson(std::get<TextDocumentSyncKind>(sync));
		} else {
			j["textDocumentSync"] = toJson(std::get<TextDocumentSyncOptions>(sync));
		}
	}

	// Completion
	if (caps.completionProvider.has_value()) {
		j["completionProvider"] = toJson(caps.completionProvider.value());
	}

	// Hover
	if (caps.hoverProvider.has_value()) {
		j["hoverProvider"] = caps.hoverProvider.value();
	}

	// Signature help
	if (caps.signatureHelpProvider.has_value()) {
		j["signatureHelpProvider"] = toJson(caps.signatureHelpProvider.value());
	}

	// Definition
	if (caps.definitionProvider.has_value()) {
		j["definitionProvider"] = caps.definitionProvider.value();
	}

	// References
	if (caps.referencesProvider.has_value()) {
		j["referencesProvider"] = caps.referencesProvider.value();
	}

	// Document symbol
	if (caps.documentSymbolProvider.has_value()) {
		j["documentSymbolProvider"] = caps.documentSymbolProvider.value();
	}

	// Rename
	if (caps.renameProvider.has_value()) {
		const auto &rename = caps.renameProvider.value();
		if (std::holds_alternative<bool>(rename)) {
			j["renameProvider"] = std::get<bool>(rename);
		} else {
			j["renameProvider"] = toJson(std::get<RenameOptions>(rename));
		}
	}

	// Code action
	if (caps.codeActionProvider.has_value()) {
		const auto &codeAction = caps.codeActionProvider.value();
		if (std::holds_alternative<bool>(codeAction)) {
			j["codeActionProvider"] = std::get<bool>(codeAction);
		}
	}

	// Document formatting
	if (caps.documentFormattingProvider.has_value()) {
		j["documentFormattingProvider"] = caps.documentFormattingProvider.value();
	}

	// Folding range
	if (caps.foldingRangeProvider.has_value()) {
		const auto &folding = caps.foldingRangeProvider.value();
		if (std::holds_alternative<bool>(folding)) {
			j["foldingRangeProvider"] = std::get<bool>(folding);
		}
	}

	return j;
}

ServerCapabilities serverCapabilitiesFromJson(const json &j) {
	ServerCapabilities caps;
	// Implementation would parse all capability fields
	return caps;
}

// =============================================================================
// Document Did Open/Change/Close/Save
// =============================================================================

json toJson(const DidOpenTextDocumentParams &params) {
	return json{{"textDocument", toJson(params.textDocument)}};
}

DidOpenTextDocumentParams didOpenTextDocumentParamsFromJson(const json &j) {
	DidOpenTextDocumentParams params;
	if (j.contains("textDocument")) {
		params.textDocument = textDocumentItemFromJson(j["textDocument"]);
	}
	return params;
}

json toJson(const DidChangeTextDocumentParams &params) {
	json j{{"textDocument", toJson(params.textDocument)}};
	json changes = json::array();
	for (const auto &change : params.contentChanges) {
		changes.push_back(toJson(change));
	}
	j["contentChanges"] = changes;
	return j;
}

DidChangeTextDocumentParams didChangeTextDocumentParamsFromJson(const json &j) {
	DidChangeTextDocumentParams params;
	if (j.contains("textDocument")) {
		params.textDocument = versionedTextDocumentIdentifierFromJson(j["textDocument"]);
	}
	if (j.contains("contentChanges")) {
		for (const auto &change : j["contentChanges"]) {
			params.contentChanges.push_back(textDocumentContentChangeEventFromJson(change));
		}
	}
	return params;
}

json toJson(const DidSaveTextDocumentParams &params) {
	json j{{"textDocument", toJson(params.textDocument)}};
	if (params.text.has_value()) {
		j["text"] = params.text.value();
	}
	return j;
}

DidSaveTextDocumentParams didSaveTextDocumentParamsFromJson(const json &j) {
	DidSaveTextDocumentParams params;
	if (j.contains("textDocument")) {
		params.textDocument = textDocumentIdentifierFromJson(j["textDocument"]);
	}
	if (j.contains("text") && !j["text"].is_null()) {
		params.text = j["text"].get<std::string>();
	}
	return params;
}

json toJson(const DidCloseTextDocumentParams &params) {
	return json{{"textDocument", toJson(params.textDocument)}};
}

DidCloseTextDocumentParams didCloseTextDocumentParamsFromJson(const json &j) {
	DidCloseTextDocumentParams params;
	if (j.contains("textDocument")) {
		params.textDocument = textDocumentIdentifierFromJson(j["textDocument"]);
	}
	return params;
}

} // namespace maxon::lsp

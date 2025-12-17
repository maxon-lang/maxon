#ifndef MAXON_LSP_JSON_H
#define MAXON_LSP_JSON_H

#include "lsp_types.h"
#include <nlohmann/json.hpp>

namespace maxon::lsp {

using json = nlohmann::json;

// =============================================================================
// JSON Conversion Helpers
// =============================================================================

// Helper to add optional field to JSON if value is present
template <typename T>
void addOptional(json &j, const std::string &key, const std::optional<T> &opt);

// Helper to get optional field from JSON
template <typename T>
std::optional<T> getOptional(const json &j, const std::string &key);

// Helper to get required field from JSON with type checking
template <typename T>
T getRequired(const json &j, const std::string &key);

// =============================================================================
// Position and Range Types
// =============================================================================

json toJson(const Position &pos);
Position positionFromJson(const json &j);

json toJson(const Range &range);
Range rangeFromJson(const json &j);

json toJson(const Location &loc);
Location locationFromJson(const json &j);

json toJson(const LocationLink &link);
LocationLink locationLinkFromJson(const json &j);

// =============================================================================
// Document Types
// =============================================================================

json toJson(const TextDocumentIdentifier &id);
TextDocumentIdentifier textDocumentIdentifierFromJson(const json &j);

json toJson(const VersionedTextDocumentIdentifier &id);
VersionedTextDocumentIdentifier versionedTextDocumentIdentifierFromJson(const json &j);

json toJson(const OptionalVersionedTextDocumentIdentifier &id);
OptionalVersionedTextDocumentIdentifier optionalVersionedTextDocumentIdentifierFromJson(const json &j);

json toJson(const TextDocumentItem &item);
TextDocumentItem textDocumentItemFromJson(const json &j);

json toJson(const TextDocumentContentChangeEvent &event);
TextDocumentContentChangeEvent textDocumentContentChangeEventFromJson(const json &j);

json toJson(const TextDocumentPositionParams &params);
TextDocumentPositionParams textDocumentPositionParamsFromJson(const json &j);

// =============================================================================
// Diagnostic Types
// =============================================================================

json toJson(DiagnosticSeverity severity);
DiagnosticSeverity diagnosticSeverityFromJson(const json &j);

json toJson(DiagnosticTag tag);
DiagnosticTag diagnosticTagFromJson(const json &j);

json toJson(const DiagnosticRelatedInformation &info);
DiagnosticRelatedInformation diagnosticRelatedInformationFromJson(const json &j);

json toJson(const CodeDescription &desc);
CodeDescription codeDescriptionFromJson(const json &j);

json toJson(const DiagnosticCode &code);
DiagnosticCode diagnosticCodeFromJson(const json &j);

json toJson(const Diagnostic &diag);
Diagnostic diagnosticFromJson(const json &j);

json toJson(const PublishDiagnosticsParams &params);
PublishDiagnosticsParams publishDiagnosticsParamsFromJson(const json &j);

// =============================================================================
// Text Edit Types
// =============================================================================

json toJson(const TextEdit &edit);
TextEdit textEditFromJson(const json &j);

json toJson(const AnnotatedTextEdit &edit);
AnnotatedTextEdit annotatedTextEditFromJson(const json &j);

json toJson(const TextDocumentEdit &edit);
TextDocumentEdit textDocumentEditFromJson(const json &j);

json toJson(const CreateFileOptions &options);
CreateFileOptions createFileOptionsFromJson(const json &j);

json toJson(const CreateFile &file);
CreateFile createFileFromJson(const json &j);

json toJson(const RenameFileOptions &options);
RenameFileOptions renameFileOptionsFromJson(const json &j);

json toJson(const RenameFile &file);
RenameFile renameFileFromJson(const json &j);

json toJson(const DeleteFileOptions &options);
DeleteFileOptions deleteFileOptionsFromJson(const json &j);

json toJson(const DeleteFile &file);
DeleteFile deleteFileFromJson(const json &j);

json toJson(const DocumentChange &change);
DocumentChange documentChangeFromJson(const json &j);

json toJson(const ChangeAnnotation &annotation);
ChangeAnnotation changeAnnotationFromJson(const json &j);

json toJson(const WorkspaceEdit &edit);
WorkspaceEdit workspaceEditFromJson(const json &j);

// =============================================================================
// Completion Types
// =============================================================================

json toJson(CompletionItemKind kind);
CompletionItemKind completionItemKindFromJson(const json &j);

json toJson(InsertTextFormat format);
InsertTextFormat insertTextFormatFromJson(const json &j);

json toJson(CompletionTriggerKind kind);
CompletionTriggerKind completionTriggerKindFromJson(const json &j);

json toJson(CompletionItemTag tag);
CompletionItemTag completionItemTagFromJson(const json &j);

json toJson(InsertTextMode mode);
InsertTextMode insertTextModeFromJson(const json &j);

json toJson(MarkupKind kind);
MarkupKind markupKindFromJson(const json &j);

json toJson(const MarkupContent &content);
MarkupContent markupContentFromJson(const json &j);

json toJson(const Documentation &doc);
Documentation documentationFromJson(const json &j);

json toJson(const InsertReplaceEdit &edit);
InsertReplaceEdit insertReplaceEditFromJson(const json &j);

json toJson(const CompletionItemLabelDetails &details);
CompletionItemLabelDetails completionItemLabelDetailsFromJson(const json &j);

json toJson(const Command &cmd);
Command commandFromJson(const json &j);

json toJson(const CompletionItem &item);
CompletionItem completionItemFromJson(const json &j);

json toJson(const CompletionList &list);
CompletionList completionListFromJson(const json &j);

json toJson(const CompletionContext &ctx);
CompletionContext completionContextFromJson(const json &j);

json toJson(const CompletionParams &params);
CompletionParams completionParamsFromJson(const json &j);

// =============================================================================
// Hover Types
// =============================================================================

json toJson(const Hover &hover);
Hover hoverFromJson(const json &j);

json toJson(const HoverParams &params);
HoverParams hoverParamsFromJson(const json &j);

// =============================================================================
// Signature Help Types
// =============================================================================

json toJson(const ParameterInformation &param);
ParameterInformation parameterInformationFromJson(const json &j);

json toJson(const SignatureInformation &sig);
SignatureInformation signatureInformationFromJson(const json &j);

json toJson(const SignatureHelp &help);
SignatureHelp signatureHelpFromJson(const json &j);

json toJson(SignatureHelpTriggerKind kind);
SignatureHelpTriggerKind signatureHelpTriggerKindFromJson(const json &j);

json toJson(const SignatureHelpContext &ctx);
SignatureHelpContext signatureHelpContextFromJson(const json &j);

json toJson(const SignatureHelpParams &params);
SignatureHelpParams signatureHelpParamsFromJson(const json &j);

// =============================================================================
// Symbol Types
// =============================================================================

json toJson(SymbolKind kind);
SymbolKind symbolKindFromJson(const json &j);

json toJson(SymbolTag tag);
SymbolTag symbolTagFromJson(const json &j);

json toJson(const DocumentSymbol &symbol);
DocumentSymbol documentSymbolFromJson(const json &j);

json toJson(const SymbolInformation &info);
SymbolInformation symbolInformationFromJson(const json &j);

json toJson(const DocumentSymbolParams &params);
DocumentSymbolParams documentSymbolParamsFromJson(const json &j);

json toJson(const WorkspaceSymbolParams &params);
WorkspaceSymbolParams workspaceSymbolParamsFromJson(const json &j);

// =============================================================================
// Code Action Types
// =============================================================================

json toJson(CodeActionTriggerKind kind);
CodeActionTriggerKind codeActionTriggerKindFromJson(const json &j);

json toJson(const CodeActionContext &ctx);
CodeActionContext codeActionContextFromJson(const json &j);

json toJson(const CodeActionDisabled &disabled);
CodeActionDisabled codeActionDisabledFromJson(const json &j);

json toJson(const CodeAction &action);
CodeAction codeActionFromJson(const json &j);

json toJson(const CodeActionParams &params);
CodeActionParams codeActionParamsFromJson(const json &j);

// =============================================================================
// Formatting Types
// =============================================================================

json toJson(const FormattingOptions &options);
FormattingOptions formattingOptionsFromJson(const json &j);

json toJson(const DocumentFormattingParams &params);
DocumentFormattingParams documentFormattingParamsFromJson(const json &j);

json toJson(const DocumentRangeFormattingParams &params);
DocumentRangeFormattingParams documentRangeFormattingParamsFromJson(const json &j);

json toJson(const DocumentOnTypeFormattingParams &params);
DocumentOnTypeFormattingParams documentOnTypeFormattingParamsFromJson(const json &j);

// =============================================================================
// Folding Range Types
// =============================================================================

json toJson(const FoldingRange &range);
FoldingRange foldingRangeFromJson(const json &j);

json toJson(const FoldingRangeParams &params);
FoldingRangeParams foldingRangeParamsFromJson(const json &j);

// =============================================================================
// Semantic Tokens Types
// =============================================================================

json toJson(const SemanticTokensLegend &legend);
SemanticTokensLegend semanticTokensLegendFromJson(const json &j);

json toJson(const SemanticTokens &tokens);
SemanticTokens semanticTokensFromJson(const json &j);

json toJson(const SemanticTokensEdit &edit);
SemanticTokensEdit semanticTokensEditFromJson(const json &j);

json toJson(const SemanticTokensDelta &delta);
SemanticTokensDelta semanticTokensDeltaFromJson(const json &j);

json toJson(const SemanticTokensParams &params);
SemanticTokensParams semanticTokensParamsFromJson(const json &j);

json toJson(const SemanticTokensRangeParams &params);
SemanticTokensRangeParams semanticTokensRangeParamsFromJson(const json &j);

json toJson(const SemanticTokensDeltaParams &params);
SemanticTokensDeltaParams semanticTokensDeltaParamsFromJson(const json &j);

// =============================================================================
// Definition/Declaration/References Types
// =============================================================================

json toJson(const DefinitionParams &params);
DefinitionParams definitionParamsFromJson(const json &j);

json toJson(const DeclarationParams &params);
DeclarationParams declarationParamsFromJson(const json &j);

json toJson(const TypeDefinitionParams &params);
TypeDefinitionParams typeDefinitionParamsFromJson(const json &j);

json toJson(const ImplementationParams &params);
ImplementationParams implementationParamsFromJson(const json &j);

json toJson(const ReferenceContext &ctx);
ReferenceContext referenceContextFromJson(const json &j);

json toJson(const ReferenceParams &params);
ReferenceParams referenceParamsFromJson(const json &j);

// =============================================================================
// Rename Types
// =============================================================================

json toJson(const PrepareRenameResult &result);
PrepareRenameResult prepareRenameResultFromJson(const json &j);

json toJson(const RenameParams &params);
RenameParams renameParamsFromJson(const json &j);

// =============================================================================
// Linked Editing Range Types
// =============================================================================

json toJson(const LinkedEditingRanges &ranges);
LinkedEditingRanges linkedEditingRangesFromJson(const json &j);

json toJson(const LinkedEditingRangeParams &params);
LinkedEditingRangeParams linkedEditingRangeParamsFromJson(const json &j);

// =============================================================================
// Inlay Hint Types
// =============================================================================

json toJson(InlayHintKind kind);
InlayHintKind inlayHintKindFromJson(const json &j);

json toJson(const InlayHintLabelPart &part);
InlayHintLabelPart inlayHintLabelPartFromJson(const json &j);

json toJson(const InlayHint &hint);
InlayHint inlayHintFromJson(const json &j);

json toJson(const InlayHintParams &params);
InlayHintParams inlayHintParamsFromJson(const json &j);

// =============================================================================
// CodeLens Types
// =============================================================================

json toJson(const CodeLens &lens);
CodeLens codeLensFromJson(const json &j);

json toJson(const CodeLensParams &params);
CodeLensParams codeLensParamsFromJson(const json &j);

// =============================================================================
// Call Hierarchy Types
// =============================================================================

json toJson(const CallHierarchyItem &item);
CallHierarchyItem callHierarchyItemFromJson(const json &j);

json toJson(const CallHierarchyIncomingCall &call);
CallHierarchyIncomingCall callHierarchyIncomingCallFromJson(const json &j);

json toJson(const CallHierarchyOutgoingCall &call);
CallHierarchyOutgoingCall callHierarchyOutgoingCallFromJson(const json &j);

json toJson(const CallHierarchyPrepareParams &params);
CallHierarchyPrepareParams callHierarchyPrepareParamsFromJson(const json &j);

json toJson(const CallHierarchyIncomingCallsParams &params);
CallHierarchyIncomingCallsParams callHierarchyIncomingCallsParamsFromJson(const json &j);

json toJson(const CallHierarchyOutgoingCallsParams &params);
CallHierarchyOutgoingCallsParams callHierarchyOutgoingCallsParamsFromJson(const json &j);

// =============================================================================
// Initialization Types
// =============================================================================

json toJson(TextDocumentSyncKind kind);
TextDocumentSyncKind textDocumentSyncKindFromJson(const json &j);

json toJson(TraceValue trace);
TraceValue traceValueFromJson(const json &j);

json toJson(const ClientInfo &info);
ClientInfo clientInfoFromJson(const json &j);

json toJson(const ServerInfo &info);
ServerInfo serverInfoFromJson(const json &j);

json toJson(const WorkspaceFolder &folder);
WorkspaceFolder workspaceFolderFromJson(const json &j);

json toJson(const ClientCapabilities &caps);
ClientCapabilities clientCapabilitiesFromJson(const json &j);

json toJson(const TextDocumentSyncOptions &options);
TextDocumentSyncOptions textDocumentSyncOptionsFromJson(const json &j);

json toJson(const CompletionOptions &options);
CompletionOptions completionOptionsFromJson(const json &j);

json toJson(const SignatureHelpOptions &options);
SignatureHelpOptions signatureHelpOptionsFromJson(const json &j);

json toJson(const CodeActionOptions &options);
CodeActionOptions codeActionOptionsFromJson(const json &j);

json toJson(const DocumentOnTypeFormattingOptions &options);
DocumentOnTypeFormattingOptions documentOnTypeFormattingOptionsFromJson(const json &j);

json toJson(const RenameOptions &options);
RenameOptions renameOptionsFromJson(const json &j);

json toJson(const FoldingRangeOptions &options);
FoldingRangeOptions foldingRangeOptionsFromJson(const json &j);

json toJson(const SemanticTokensOptions &options);
SemanticTokensOptions semanticTokensOptionsFromJson(const json &j);

json toJson(const InlayHintOptions &options);
InlayHintOptions inlayHintOptionsFromJson(const json &j);

json toJson(const LinkedEditingRangeOptions &options);
LinkedEditingRangeOptions linkedEditingRangeOptionsFromJson(const json &j);

json toJson(const CallHierarchyOptions &options);
CallHierarchyOptions callHierarchyOptionsFromJson(const json &j);

json toJson(const WorkspaceSymbolOptions &options);
WorkspaceSymbolOptions workspaceSymbolOptionsFromJson(const json &j);

json toJson(const ServerCapabilities &caps);
ServerCapabilities serverCapabilitiesFromJson(const json &j);

json toJson(const InitializeParams &params);
InitializeParams initializeParamsFromJson(const json &j);

json toJson(const InitializeResult &result);
InitializeResult initializeResultFromJson(const json &j);

json toJson(const InitializeError &error);
InitializeError initializeErrorFromJson(const json &j);

// =============================================================================
// Document Did Open/Change/Close/Save
// =============================================================================

json toJson(const DidOpenTextDocumentParams &params);
DidOpenTextDocumentParams didOpenTextDocumentParamsFromJson(const json &j);

json toJson(const DidChangeTextDocumentParams &params);
DidChangeTextDocumentParams didChangeTextDocumentParamsFromJson(const json &j);

json toJson(TextDocumentSaveReason reason);
TextDocumentSaveReason textDocumentSaveReasonFromJson(const json &j);

json toJson(const WillSaveTextDocumentParams &params);
WillSaveTextDocumentParams willSaveTextDocumentParamsFromJson(const json &j);

json toJson(const DidSaveTextDocumentParams &params);
DidSaveTextDocumentParams didSaveTextDocumentParamsFromJson(const json &j);

json toJson(const DidCloseTextDocumentParams &params);
DidCloseTextDocumentParams didCloseTextDocumentParamsFromJson(const json &j);

// =============================================================================
// File Watching Types
// =============================================================================

json toJson(const FileSystemWatcher &watcher);
FileSystemWatcher fileSystemWatcherFromJson(const json &j);

json toJson(const FileEvent &event);
FileEvent fileEventFromJson(const json &j);

json toJson(const DidChangeWatchedFilesParams &params);
DidChangeWatchedFilesParams didChangeWatchedFilesParamsFromJson(const json &j);

json toJson(const DidChangeWatchedFilesRegistrationOptions &options);
DidChangeWatchedFilesRegistrationOptions didChangeWatchedFilesRegistrationOptionsFromJson(const json &j);

// =============================================================================
// Registration/Unregistration Types
// =============================================================================

json toJson(const Registration &reg);
Registration registrationFromJson(const json &j);

json toJson(const RegistrationParams &params);
RegistrationParams registrationParamsFromJson(const json &j);

json toJson(const Unregistration &unreg);
Unregistration unregistrationFromJson(const json &j);

json toJson(const UnregistrationParams &params);
UnregistrationParams unregistrationParamsFromJson(const json &j);

// =============================================================================
// Progress Types
// =============================================================================

json toJson(const ProgressToken &token);
ProgressToken progressTokenFromJson(const json &j);

json toJson(const WorkDoneProgressBegin &progress);
WorkDoneProgressBegin workDoneProgressBeginFromJson(const json &j);

json toJson(const WorkDoneProgressReport &progress);
WorkDoneProgressReport workDoneProgressReportFromJson(const json &j);

json toJson(const WorkDoneProgressEnd &progress);
WorkDoneProgressEnd workDoneProgressEndFromJson(const json &j);

// =============================================================================
// Show Message Types
// =============================================================================

json toJson(MessageType type);
MessageType messageTypeFromJson(const json &j);

json toJson(const ShowMessageParams &params);
ShowMessageParams showMessageParamsFromJson(const json &j);

json toJson(const MessageActionItem &item);
MessageActionItem messageActionItemFromJson(const json &j);

json toJson(const ShowMessageRequestParams &params);
ShowMessageRequestParams showMessageRequestParamsFromJson(const json &j);

json toJson(const LogMessageParams &params);
LogMessageParams logMessageParamsFromJson(const json &j);

// =============================================================================
// Error Types
// =============================================================================

json toJson(const ResponseError &error);
ResponseError responseErrorFromJson(const json &j);

} // namespace maxon::lsp

#endif // MAXON_LSP_JSON_H

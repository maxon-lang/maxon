import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';
import { log } from './logger';

interface IRError {
	message: string;
	line: number;
	column: number;
	type?: string;
}

interface GenerateIRResponse {
	ir: string;
	errors: IRError[];
}

interface GenerateAsmResponse {
	assembly: string;
	errors: IRError[];
}

type OutputMode = 'mir' | 'asm';

/**
 * Compiler Explorer View
 *
 * A webview view that lives in the Maxon activity bar container. Shows Maxon
 * source code in an editor and the generated output (MIR or x86-64 assembly)
 * in a separate panel below. Supports toggling between optimized and
 * unoptimized output.
 */
export class CompilerExplorerViewProvider implements vscode.WebviewViewProvider {
	public static readonly viewType = 'maxon.compilerExplorerView';

	private _view: vscode.WebviewView | undefined;
	private readonly _disposables: vscode.Disposable[] = [];

	private _currentSource: string = '';
	private _optimize: boolean = false;
	private _outputMode: OutputMode = 'mir';
	private _debounceTimer: NodeJS.Timeout | undefined;

	constructor(
		private readonly _extensionUri: vscode.Uri,
		private readonly _getClient: () => LanguageClient | undefined
	) {}

	public resolveWebviewView(
		webviewView: vscode.WebviewView,
		_context: vscode.WebviewViewResolveContext,
		_token: vscode.CancellationToken
	): void {
		this._view = webviewView;

		webviewView.webview.options = {
			enableScripts: true,
			localResourceRoots: [this._extensionUri]
		};

		webviewView.webview.html = this._getHtmlForWebview();

		webviewView.webview.onDidReceiveMessage(
			async (message) => {
				switch (message.command) {
					case 'sourceChanged':
						this._currentSource = message.source;
						this._debouncedGenerateOutput();
						break;
					case 'toggleOptimize':
						this._optimize = message.optimize;
						this._generateOutput();
						break;
					case 'setOutputMode':
						this._outputMode = message.mode;
						this._generateOutput();
						break;
					case 'requestOutput':
						this._generateOutput();
						break;
				}
			},
			null,
			this._disposables
		);

		webviewView.onDidChangeVisibility(() => {
			if (webviewView.visible && this._currentSource.trim()) {
				this._generateOutput();
			}
		}, null, this._disposables);

		webviewView.onDidDispose(() => {
			while (this._disposables.length) {
				this._disposables.pop()?.dispose();
			}
			if (this._debounceTimer) {
				clearTimeout(this._debounceTimer);
				this._debounceTimer = undefined;
			}
			this._view = undefined;
		});
	}

	private _debouncedGenerateOutput() {
		if (this._debounceTimer) {
			clearTimeout(this._debounceTimer);
		}
		this._debounceTimer = setTimeout(() => {
			this._generateOutput();
		}, 500);
	}

	private async _generateOutput() {
		if (!this._view) return;
		const webview = this._view.webview;

		if (!this._currentSource.trim()) {
			webview.postMessage({ command: 'updateOutput', output: '', errors: [] });
			return;
		}

		const client = this._getClient();
		if (!client) {
			webview.postMessage({
				command: 'updateOutput',
				output: '',
				errors: [{ message: 'Language server not started', line: 1, column: 1 }]
			});
			return;
		}

		try {
			if (this._outputMode === 'mir') {
				log(`Generating MIR (optimize=${this._optimize})`);
				const response = await client.sendRequest<GenerateIRResponse>(
					'maxon/generateIR',
					{
						source: this._currentSource,
						filename: 'compiler_explorer.maxon',
						optimize: this._optimize
					}
				);
				webview.postMessage({
					command: 'updateOutput',
					output: response.ir,
					errors: response.errors
				});
			} else {
				log(`Generating Assembly (optimize=${this._optimize})`);
				const response = await client.sendRequest<GenerateAsmResponse>(
					'maxon/generateAsm',
					{
						source: this._currentSource,
						filename: 'compiler_explorer.maxon',
						optimize: this._optimize
					}
				);
				webview.postMessage({
					command: 'updateOutput',
					output: response.assembly,
					errors: response.errors
				});
			}
		} catch (error) {
			log(`Error generating output: ${error}`);
			webview.postMessage({
				command: 'updateOutput',
				output: '',
				errors: [{ message: `Error: ${error}`, line: 1, column: 1 }]
			});
		}
	}

	private _getHtmlForWebview(): string {
		const nonce = getNonce();

		return `<!DOCTYPE html>
<html lang="en">
<head>
	<meta charset="UTF-8">
	<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
	<meta name="viewport" content="width=device-width, initial-scale=1.0">
	<title>Compiler Explorer</title>
	<style>
		* {
			margin: 0;
			padding: 0;
			box-sizing: border-box;
		}

		body {
			font-family: var(--vscode-font-family);
			background-color: var(--vscode-sideBar-background);
			color: var(--vscode-sideBar-foreground, var(--vscode-foreground));
			height: 100vh;
			display: flex;
			flex-direction: column;
			overflow: hidden;
		}

		.toolbar {
			display: flex;
			align-items: center;
			padding: 6px 8px;
			background-color: var(--vscode-sideBarSectionHeader-background);
			border-bottom: 1px solid var(--vscode-panel-border);
			gap: 8px;
			flex-wrap: wrap;
		}

		.toolbar-spacer {
			flex: 1;
		}

		.toggle-container {
			display: flex;
			align-items: center;
			gap: 6px;
		}

		.toggle-label {
			font-size: 11px;
			color: var(--vscode-descriptionForeground);
		}

		.toggle-switch {
			position: relative;
			width: 30px;
			height: 16px;
			background-color: var(--vscode-input-background);
			border: 1px solid var(--vscode-input-border);
			border-radius: 8px;
			cursor: pointer;
			transition: background-color 0.2s;
		}

		.toggle-switch.active {
			background-color: var(--vscode-button-background);
		}

		.toggle-switch::after {
			content: '';
			position: absolute;
			top: 1px;
			left: 1px;
			width: 12px;
			height: 12px;
			background-color: var(--vscode-button-foreground);
			border-radius: 50%;
			transition: transform 0.2s;
		}

		.toggle-switch.active::after {
			transform: translateX(14px);
		}

		.main-container {
			display: flex;
			flex-direction: column;
			flex: 1;
			overflow: hidden;
		}

		.panel {
			display: flex;
			flex-direction: column;
			flex: 1;
			min-height: 0;
		}

		.panel-header {
			display: flex;
			align-items: center;
			padding: 4px 8px;
			background-color: var(--vscode-sideBarSectionHeader-background);
			border-bottom: 1px solid var(--vscode-panel-border);
			font-size: 11px;
			font-weight: 600;
			text-transform: uppercase;
			letter-spacing: 0.5px;
		}

		.panel-content {
			flex: 1;
			overflow: hidden;
			position: relative;
		}

		.source-editor {
			width: 100%;
			height: 100%;
			background-color: var(--vscode-editor-background);
			color: var(--vscode-editor-foreground);
			border: none;
			padding: 8px;
			font-family: var(--vscode-editor-font-family, 'Consolas', 'Courier New', monospace);
			font-size: var(--vscode-editor-font-size, 13px);
			line-height: 1.5;
			resize: none;
			outline: none;
			tab-size: 4;
		}

		.source-editor::placeholder {
			color: var(--vscode-input-placeholderForeground);
		}

		.ir-output {
			width: 100%;
			height: 100%;
			background-color: var(--vscode-editor-background);
			color: var(--vscode-editor-foreground);
			padding: 8px;
			font-family: var(--vscode-editor-font-family, 'Consolas', 'Courier New', monospace);
			font-size: var(--vscode-editor-font-size, 13px);
			line-height: 1.5;
			overflow: auto;
			white-space: pre;
		}

		.ir-output.has-errors {
			color: var(--vscode-errorForeground);
		}

		.divider {
			height: 4px;
			background-color: var(--vscode-panel-border);
			cursor: ns-resize;
			flex-shrink: 0;
		}

		.divider:hover {
			background-color: var(--vscode-focusBorder);
		}

		.error-list {
			padding: 6px 8px;
			background-color: var(--vscode-inputValidation-errorBackground);
			border-top: 1px solid var(--vscode-inputValidation-errorBorder);
			font-size: 11px;
			max-height: 100px;
			overflow: auto;
		}

		.error-item {
			padding: 2px 0;
		}

		.error-location {
			color: var(--vscode-errorForeground);
			font-weight: 600;
		}

		.mode-selector {
			display: flex;
			background-color: var(--vscode-input-background);
			border: 1px solid var(--vscode-input-border);
			border-radius: 4px;
			overflow: hidden;
		}

		.mode-btn {
			padding: 2px 8px;
			font-size: 11px;
			background: transparent;
			border: none;
			color: var(--vscode-foreground);
			cursor: pointer;
			transition: background-color 0.2s;
		}

		.mode-btn:hover {
			background-color: var(--vscode-list-hoverBackground);
		}

		.mode-btn.active {
			background-color: var(--vscode-button-background);
			color: var(--vscode-button-foreground);
		}
	</style>
</head>
<body>
	<div class="toolbar">
		<div class="mode-selector">
			<button class="mode-btn active" id="mirBtn">MIR</button>
			<button class="mode-btn" id="asmBtn">Assembly</button>
		</div>
		<span class="toolbar-spacer"></span>
		<div class="toggle-container">
			<span class="toggle-label">Optimized</span>
			<div class="toggle-switch" id="optimizeToggle"></div>
		</div>
	</div>

	<div class="main-container">
		<div class="panel" id="sourcePanel">
			<div class="panel-header">Source</div>
			<div class="panel-content">
				<textarea class="source-editor" id="sourceEditor" placeholder="Enter Maxon code here...

Example:
function add(a int, b int) returns int
    return a + b
end 'add'

function main()
    let result = add(1, 2)
end 'main'"></textarea>
			</div>
		</div>

		<div class="divider" id="divider"></div>

		<div class="panel" id="outputPanel">
			<div class="panel-header" id="outputPanelHeader">MIR Output</div>
			<div class="panel-content">
				<div class="ir-output" id="outputView"></div>
			</div>
			<div class="error-list" id="errorList" style="display: none;"></div>
		</div>
	</div>

	<script nonce="${nonce}">
		const vscode = acquireVsCodeApi();
		const sourceEditor = document.getElementById('sourceEditor');
		const outputView = document.getElementById('outputView');
		const outputPanelHeader = document.getElementById('outputPanelHeader');
		const errorList = document.getElementById('errorList');
		const optimizeToggle = document.getElementById('optimizeToggle');
		const mirBtn = document.getElementById('mirBtn');
		const asmBtn = document.getElementById('asmBtn');
		const divider = document.getElementById('divider');
		const sourcePanel = document.getElementById('sourcePanel');
		const outputPanel = document.getElementById('outputPanel');

		let isOptimized = false;
		let outputMode = 'mir';

		const previousState = vscode.getState();
		if (previousState && previousState.source) {
			sourceEditor.value = previousState.source;
		}
		if (previousState && previousState.outputMode) {
			outputMode = previousState.outputMode;
			if (outputMode === 'asm') {
				asmBtn.classList.add('active');
				mirBtn.classList.remove('active');
				outputPanelHeader.textContent = 'Assembly Output';
			}
		}
		if (previousState && previousState.isOptimized) {
			isOptimized = previousState.isOptimized;
			optimizeToggle.classList.toggle('active', isOptimized);
		}

		function saveState() {
			vscode.setState({ source: sourceEditor.value, outputMode, isOptimized });
		}

		sourceEditor.addEventListener('input', () => {
			saveState();
			vscode.postMessage({
				command: 'sourceChanged',
				source: sourceEditor.value
			});
		});

		sourceEditor.addEventListener('keydown', (e) => {
			if (e.key === 'Tab') {
				e.preventDefault();
				const start = sourceEditor.selectionStart;
				const end = sourceEditor.selectionEnd;
				sourceEditor.value = sourceEditor.value.substring(0, start) + '\\t' + sourceEditor.value.substring(end);
				sourceEditor.selectionStart = sourceEditor.selectionEnd = start + 1;
				sourceEditor.dispatchEvent(new Event('input'));
			}
		});

		optimizeToggle.addEventListener('click', () => {
			isOptimized = !isOptimized;
			optimizeToggle.classList.toggle('active', isOptimized);
			saveState();
			vscode.postMessage({
				command: 'toggleOptimize',
				optimize: isOptimized
			});
		});

		mirBtn.addEventListener('click', () => {
			if (outputMode !== 'mir') {
				outputMode = 'mir';
				mirBtn.classList.add('active');
				asmBtn.classList.remove('active');
				outputPanelHeader.textContent = 'MIR Output';
				saveState();
				vscode.postMessage({
					command: 'setOutputMode',
					mode: 'mir'
				});
			}
		});

		asmBtn.addEventListener('click', () => {
			if (outputMode !== 'asm') {
				outputMode = 'asm';
				asmBtn.classList.add('active');
				mirBtn.classList.remove('active');
				outputPanelHeader.textContent = 'Assembly Output';
				saveState();
				vscode.postMessage({
					command: 'setOutputMode',
					mode: 'asm'
				});
			}
		});

		let isDragging = false;
		let startY = 0;
		let startSourceHeight = 0;

		divider.addEventListener('mousedown', (e) => {
			isDragging = true;
			startY = e.clientY;
			startSourceHeight = sourcePanel.offsetHeight;
			document.body.style.cursor = 'ns-resize';
			e.preventDefault();
		});

		document.addEventListener('mousemove', (e) => {
			if (!isDragging) return;

			const deltaY = e.clientY - startY;
			const newSourceHeight = Math.max(60, Math.min(window.innerHeight - 120, startSourceHeight + deltaY));

			sourcePanel.style.flex = 'none';
			sourcePanel.style.height = newSourceHeight + 'px';
			outputPanel.style.flex = '1';
		});

		document.addEventListener('mouseup', () => {
			if (isDragging) {
				isDragging = false;
				document.body.style.cursor = '';
			}
		});

		window.addEventListener('message', (event) => {
			const message = event.data;
			switch (message.command) {
				case 'updateOutput':
					updateOutput(message.output, message.errors);
					break;
			}
		});

		function updateOutput(output, errors) {
			if (errors && errors.length > 0) {
				errorList.style.display = 'block';
				errorList.innerHTML = errors.map(err =>
					\`<div class="error-item"><span class="error-location">Line \${err.line}:\${err.column}:</span> \${escapeHtml(err.message)}</div>\`
				).join('');
				outputView.classList.add('has-errors');
				outputView.textContent = '';
			} else {
				errorList.style.display = 'none';
				outputView.classList.remove('has-errors');
				outputView.textContent = output || '';
			}
		}

		function escapeHtml(text) {
			const div = document.createElement('div');
			div.textContent = text;
			return div.innerHTML;
		}

		if (sourceEditor.value) {
			vscode.postMessage({
				command: 'sourceChanged',
				source: sourceEditor.value
			});
		}
	</script>
</body>
</html>`;
	}
}

function getNonce(): string {
	let text = '';
	const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
	for (let i = 0; i < 32; i++) {
		text += possible.charAt(Math.floor(Math.random() * possible.length));
	}
	return text;
}

import * as vscode from 'vscode';

const logs: string[] = [];
let outputChannel: vscode.OutputChannel | null = null;

/**
 * Initialize the logger with a VS Code output channel.
 * This should be called during extension activation.
 */
export function initLogger(channel: vscode.OutputChannel) {
    outputChannel = channel;
}

/**
 * Log a message to both the output channel and the internal log buffer.
 * The internal buffer can be accessed by tests.
 */
export function log(message: string) {
    const timestamp = new Date().toISOString();
    const logMessage = `[${timestamp}] ${message}`;
    
    logs.push(logMessage);
    
    if (outputChannel) {
        outputChannel.appendLine(logMessage);
    }
}

/**
 * Get a copy of all logged messages.
 * This is primarily used for testing.
 */
export function getLogs(): string[] {
    return logs.slice(); // Return a copy for safety
}

/**
 * Clear all logged messages.
 * Useful for test isolation.
 */
export function clearLogs() {
    logs.length = 0;
}

/**
 * Get the output channel instance (for direct access if needed).
 */
export function getOutputChannel(): vscode.OutputChannel | null {
    return outputChannel;
}

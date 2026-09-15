// The commands that install Maxon. `public/install.sh` and `public/install.ps1` are the scripts they run.
export const INSTALL_SCRIPT = 'curl -fsSL https://maxon.dev/install.sh | sh';

// Typed into PowerShell, so the script runs in that session and the PATH it sets reaches the terminal it
// was typed into. A `powershell -c` wrapper would run it in a child whose PATH dies with it.
export const INSTALL_POWERSHELL = 'irm https://maxon.dev/install.ps1 | iex';

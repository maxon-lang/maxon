// The commands that install Maxon. `public/install.sh` and `public/install.ps1` are the scripts they run.
export const INSTALL_SCRIPT = 'curl -fsSL https://maxon.dev/install.sh | sh';

// Run in PowerShell itself, so the session it runs in can use `maxon` straight away.
export const INSTALL_POWERSHELL = 'irm https://maxon.dev/install.ps1 | iex';

// The same, from cmd.exe or any other shell.
export const INSTALL_POWERSHELL_ANY_SHELL = 'powershell -c "irm https://maxon.dev/install.ps1 | iex"';

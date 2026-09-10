// The commands that install Maxon. `public/install.sh` and `public/install.ps1` are the scripts they run.
export const INSTALL_SCRIPT = 'curl -fsSL https://maxon.dev/install.sh | sh';

// Bun's shape, so it runs the same from PowerShell, cmd.exe or any other shell. maxon.dev answers the
// plain-http first request with a redirect to https.
export const INSTALL_POWERSHELL = 'powershell -c "irm maxon.dev/install.ps1|iex"';

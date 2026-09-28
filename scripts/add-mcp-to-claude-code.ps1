# Registers the RetroTerm MCP server with Claude Code for the current user.
#
# RetroTerm hosts the server itself, on the local machine only, at the URL below. Nothing has
# to be installed for it; RetroTerm just has to be running when the tools are used.
#
# If you changed the port in RetroTerm's Preferences, MCP tab, change it here too.
#
# Usage:  .\add-mcp-to-claude-code.ps1
# Undo:   claude mcp remove retroterm

$url = "http://127.0.0.1:5715/mcp"

claude mcp add --transport http retroterm $url
if ($LASTEXITCODE -ne 0) {
    Write-Host "claude mcp add failed. Is Claude Code installed and on the PATH?"
    exit $LASTEXITCODE
}

Write-Host "Done. Start RetroTerm, then run /mcp inside Claude Code to see 'retroterm' connected."

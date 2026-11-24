# Registers the RetroTerm MCP server with Claude Code for the current user.
#
# RetroTerm hosts the server itself, on the local machine only, at the URL below. Nothing has
# to be installed for it; RetroTerm just has to be running when the tools are used.
#
# If you changed the port in RetroTerm's Preferences, MCP tab, change it here too.
#
# Usage:  .\add-mcp-to-claude-code.ps1
# Undo:   claude mcp remove --scope user retroterm

$url = "http://127.0.0.1:5715/mcp"

# --scope user registers it for every project. Without it Claude Code uses its default scope,
# "local", which is only the folder this script is run in (checked with `claude mcp add --help`,
# Claude Code 2.1.331, 24 November 2026).
claude mcp add --transport http --scope user retroterm $url
if ($LASTEXITCODE -ne 0) {
    Write-Host "claude mcp add failed. Is Claude Code installed and on the PATH?"
    exit $LASTEXITCODE
}

Write-Host "Done. Start RetroTerm, then run /mcp inside Claude Code to see 'retroterm' connected."

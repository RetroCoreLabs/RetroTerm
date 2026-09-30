#!/bin/sh
# Installs RetroTerm for the current user, so it shows up in the application menu with its icon.
# Nothing goes outside the home folder and nothing needs root.
#
#   ~/.local/share/RetroTerm/RetroTerm                      the program
#   ~/.local/share/icons/hicolor/<size>/apps/retroterm.png  the icon, one per size in icons/
#   ~/.local/share/icons/hicolor/scalable/apps/retroterm.svg
#   ~/.local/share/applications/retroterm.desktop           the menu entry
#
# To remove it again: ./install.sh --uninstall
set -e

here=$(cd "$(dirname "$0")" && pwd)
data="${XDG_DATA_HOME:-$HOME/.local/share}"
app="$data/RetroTerm"
icons="$data/icons/hicolor"
menu="$data/applications"

if [ "$1" = "--uninstall" ]; then
    rm -rf "$app"
    rm -f "$menu/retroterm.desktop"
    find "$icons" -name 'retroterm.*' -path '*/apps/*' -delete 2>/dev/null || true
    echo "RetroTerm removed."
    exit 0
fi

mkdir -p "$app" "$menu"
cp "$here/RetroTerm" "$app/RetroTerm"
chmod +x "$app/RetroTerm"

# icons/<size>/retroterm.png, as laid out by the release workflow.
for dir in "$here"/icons/*/; do
    size=$(basename "$dir")
    mkdir -p "$icons/$size/apps"
    cp "$dir"retroterm.* "$icons/$size/apps/"
done

# The menu entry needs the full path of the program, which is only known now.
sed "s|__EXEC__|$app/RetroTerm|" "$here/retroterm.desktop" > "$menu/retroterm.desktop"
chmod +x "$menu/retroterm.desktop"

# Both refreshes are optional; most desktops notice the new files on their own.
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$menu" || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t "$icons" || true

echo "RetroTerm installed. Start it from the application menu, or run $app/RetroTerm"

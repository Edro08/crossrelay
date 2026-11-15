#!/usr/bin/env bash
set -euo pipefail

DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
INSTALL_DIR="$DATA_HOME/crossrelay"
DESKTOP_FILE="$DATA_HOME/applications/crossrelay.desktop"
ICON_FILE="$DATA_HOME/icons/hicolor/256x256/apps/crossrelay.png"
BIN_LINK="$HOME/.local/bin/crossrelay"

echo "Desinstalando CrossRelay de $DATA_HOME ..."

rm -rf "$INSTALL_DIR"
rm -f "$DESKTOP_FILE" "$ICON_FILE" "$BIN_LINK"

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$DATA_HOME/applications" >/dev/null 2>&1 || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -f -t "$DATA_HOME/icons/hicolor" >/dev/null 2>&1 || true
fi

echo "CrossRelay desinstalado correctamente."

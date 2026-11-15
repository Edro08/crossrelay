#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
INSTALL_DIR="$DATA_HOME/crossrelay"
APPS_DIR="$DATA_HOME/applications"
ICON_DIR="$DATA_HOME/icons/hicolor/256x256/apps"
BIN_DIR="$HOME/.local/bin"

if [[ ! -f "$SCRIPT_DIR/CrossRelay.Desktop" || ! -f "$SCRIPT_DIR/crossrelay-core" ]]; then
    echo "Error: ejecuta install.sh desde la carpeta compilada de CrossRelay (dist/linux-x64)." >&2
    exit 1
fi

echo "Instalando CrossRelay en $INSTALL_DIR ..."

rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR" "$APPS_DIR" "$ICON_DIR" "$BIN_DIR"

cp -a "$SCRIPT_DIR"/. "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/CrossRelay.Desktop" "$INSTALL_DIR/crossrelay-core"
[[ -f "$INSTALL_DIR/install.sh" ]] && chmod +x "$INSTALL_DIR/install.sh"
[[ -f "$INSTALL_DIR/uninstall.sh" ]] && chmod +x "$INSTALL_DIR/uninstall.sh"

if [[ -f "$INSTALL_DIR/crossrelay.png" ]]; then
    cp -f "$INSTALL_DIR/crossrelay.png" "$ICON_DIR/crossrelay.png"
fi

cat > "$APPS_DIR/crossrelay.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=CrossRelay
GenericName=LAN File Transfer
Comment=Transferencia P2P segura de archivos en red local
Exec="$INSTALL_DIR/CrossRelay.Desktop" %U
Icon=crossrelay
Terminal=false
Categories=Network;FileTransfer;Utility;
StartupWMClass=CrossRelay.Desktop
EOF

ln -sf "$INSTALL_DIR/CrossRelay.Desktop" "$BIN_DIR/crossrelay"

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -f -t "$DATA_HOME/icons/hicolor" >/dev/null 2>&1 || true
fi

echo "Listo. CrossRelay ya aparece en tu menú de aplicaciones."
echo "También puedes ejecutarlo desde terminal con: $BIN_DIR/crossrelay"

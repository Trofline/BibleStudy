#!/bin/bash
# Sets up BibleStudy on a Mac (once) and opens it. macOS needs this for programs that are not registered with Apple
# (see README-macOS.txt). Nothing is sent anywhere and no system file is changed.
cd "$(dirname "$0")" || exit 1
APP="BibleStudy.app"

if [ ! -d "$APP" ]; then
  echo "Das Programm BibleStudy.app fehlt in diesem Ordner. / BibleStudy.app is missing in this folder."
  exit 1
fi

if [ ! -f ".eingerichtet" ]; then
  echo "BibleStudy: einmalige Einrichtung (etwa eine halbe Minute) ..."
  echo "BibleStudy: one-time setup (about half a minute) ..."
  # 1. remove the "downloaded from the internet" mark from the files of this folder
  xattr -dr com.apple.quarantine . 2>/dev/null
  # 2. make the programs executable
  chmod +x "$APP/Contents/MacOS/BibleStudy" "$APP/Contents/MacOS/mcp/BibleStudy.Mcp" 2>/dev/null
  # 3. sign every program file with a local signature (Apple-silicon Macs refuse to start unsigned programs), then the app itself
  find "$APP" -type f -exec sh -c 'file "$1" | grep -q Mach-O && codesign --force --sign - "$1" >/dev/null 2>&1' _ {} \;
  codesign --force --deep --sign - "$APP" >/dev/null 2>&1
  touch .eingerichtet
fi

echo "BibleStudy wird gestartet. Dieses Fenster kannst du jetzt schließen."
echo "Starting BibleStudy. You can close this window now."
open "$APP"

#!/bin/bash
# Starts BibleStudy on a Mac. The first time it also does the one-time setup that macOS needs for programs
# that are not registered with Apple (see README-macOS.txt). Nothing is sent anywhere and no system file is changed.
cd "$(dirname "$0")" || exit 1

if [ ! -f ".eingerichtet" ]; then
  echo "BibleStudy: einmalige Einrichtung (etwa eine halbe Minute) ..."
  echo "BibleStudy: one-time setup (about half a minute) ..."
  # 1. remove the "downloaded from the internet" mark from the files of this folder
  xattr -dr com.apple.quarantine . 2>/dev/null
  # 2. make the programs executable
  chmod +x BibleStudy mcp/BibleStudy.Mcp 2>/dev/null
  # 3. sign every program file with a local signature (Apple-silicon Macs refuse to start unsigned programs)
  find . -type f -exec sh -c 'file "$1" | grep -q Mach-O && codesign --force --sign - "$1" >/dev/null 2>&1' _ {} \;
  touch .eingerichtet
fi

if [ ! -x ./BibleStudy ]; then
  echo "Die Datei BibleStudy fehlt in diesem Ordner. / The file BibleStudy is missing in this folder."
  exit 1
fi

echo "BibleStudy wird gestartet. Dieses Fenster bitte offen lassen, solange du BibleStudy benutzt."
echo "Starting BibleStudy. Please keep this window open while you use BibleStudy."
exec ./BibleStudy

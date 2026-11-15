# 19. On the phone (Android and iPhone)

BibleStudy also runs on your phone, with the same vaults, marks, notes and documents as on the PC. Both phone versions are **beta**: please report what does not work.

## 19.1 Installing on the iPhone (without a paid Apple account)

This works with **SideStore**. It signs the app with your own free Apple ID and renews it by itself afterwards. You need a PC or Mac with a cable **once**. All in all it takes about 10 minutes.

1. **Put SideStore on the iPhone (about 4 min).** On the PC open the program *iloader*, connect the iPhone with the cable and install SideStore (guide at docs.sidestore.io).
2. **Trust the profile and switch on Developer Mode (about 2 min).** iPhone: Settings → General → VPN & Device Management → trust your Apple ID. Settings → Privacy & Security → Developer Mode → switch on (the iPhone restarts).
3. **Add BibleStudy (about 2 min).** Sign in to SideStore, then tap this link on the iPhone: `sidestore://source?url=https://trofline.github.io/BibleStudy/sidestore.json`. Or in SideStore → Sources → **+** → `https://trofline.github.io/BibleStudy/sidestore.json`. Then BibleStudy → **Install**.
4. **Renew automatically (about 1 min).** A free Apple ID signs apps for 7 days only. The shortcut **"Renew BibleStudy"** does it for you: get the shortcut, then Shortcuts → Automation → Time of Day (for example daily 3:00) → **Run Immediately** → choose the shortcut.

**If the app has expired after all:** open SideStore and tap **Refresh** at BibleStudy. Your data is kept.

**Updates** come through SideStore: update BibleStudy there.

### Limits, honestly

- A free Apple ID allows at most **3 self-signed apps** at the same time; SideStore counts as one.
- After a long time switched off, or after big iOS updates, a manual refresh may be needed once.
- Apple may restrict this method one day.
- The live Bible study only runs while the app is in the foreground.
- Claude Code, Ollama and the MCP server exist only on the PC.

## 19.2 Using it on the phone

- **Tap** selects a verse, **long press** opens the menu (on the PC: right click).
- Below the top bar an **arrow** folds the tabs and toolbar away, so there is more room for the text.
- **Split view** and several translations side by side turn the phone to landscape by themselves (Android).
- **Exporting** opens the share sheet: save to Files, send by messenger, open in another app.

## 19.3 Syncing devices (PC, Android, iPhone)

Settings → **Sync devices**: your vaults are kept in step between your own devices, **with no account and no server**.

1. On the first device choose **Set up syncing**. It shows a **QR code** and a code of 26 characters.
2. On the second device scan the QR code with the **camera app** (BibleStudy opens and pairs) or type the code under **Code from another device**.
3. When both devices are on the **same Wi-Fi** they find each other by themselves and sync at start and every few minutes after. **Sync now** starts it at once.

The connection is encrypted; without the code no other device can take part. Nothing is overwritten or deleted: where they differ, both versions are kept. **Deleted items do not travel along** (they stay on the other device), and settings and materials are not synced yet.

On the PC, Windows Firewall asks the first time: allow **Private networks** only. On the iPhone, iOS asks for permission for the **local network**: allow it.

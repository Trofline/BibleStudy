# 19. On the phone (Android and iPhone)

BibleStudy also runs on your phone, with the same vaults, marks, notes and documents as on the PC. Both phone versions are **beta**: please report what does not work.

## 19.1 Installing on the iPhone (without a paid Apple account)

This works with **SideStore**. It signs the app with your own free Apple ID and renews it by itself afterwards. You need a computer (Windows or Mac) and a cable **once**. All in all it takes about 10 minutes.

1. **Put SideStore on the iPhone (about 4 min).** On Windows install *Apple Devices* (Microsoft Store) or iTunes (not needed on a Mac), then open the program *iloader* and connect the iPhone with the cable. If the iPhone asks **"Trust This Computer?"**: tap **Trust** and enter your passcode. In iloader sign in with your Apple ID and install **SideStore**. If SideStore later says the pairing file is missing: in iloader click **Manage Pairing File** at the bottom and place it for SideStore.
2. **Trust your own Apple ID (1 min).** Otherwise SideStore will not start ("Untrusted Developer"). **Settings → General → VPN & Device Management** → under *Developer App* tap your Apple ID (email address) → **Trust "…"** → **Trust**.
3. **Turn on Developer Mode (2 min).** The switch only appears after SideStore is installed.
   1. **Settings → Privacy & Security** → all the way down → **Developer Mode** → switch on.
   2. Tap **Restart**.
   3. After the restart **"Turn On Developer Mode?"** appears: tap **Turn On** and enter your passcode.

   If the entry is missing: tap SideStore once, then it shows up. Otherwise step 1 did not finish.
4. **Install LocalDevVPN (1 min).** Get it from the App Store, open it once and **Allow** the VPN configuration. It is a VPN on the phone itself that SideStore uses to talk to the phone; it sends no data anywhere.
5. **Install BibleStudy (2 min).** Sign in to SideStore, then tap this link on the iPhone: `sidestore://source?url=https://trofline.github.io/BibleStudy/sidestore.json`. Or in SideStore → **Sources → +** → `https://trofline.github.io/BibleStudy/sidestore.json`. Then BibleStudy → **Install**. You do not download the `.ipa` file yourself; through the source SideStore fetches it from the latest release. Only if the source does not work: download the `.ipa` from the release and pick it in SideStore under **My Apps** → **+**.
6. **Add the ready-made shortcut (1 min).** It switches the VPN on and off for SideStore and re-signs BibleStudy every 6 days. See **19.4**.

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

1. On the first device (best the PC) choose **Set up syncing**. It shows a **QR code** and a code of 26 characters.
2. On the second device open BibleStudy once, then scan the QR code with the **camera app** (BibleStudy opens and pairs). Or click **Copy code** on the PC, send the link to the phone and paste it under **Code from another device**.
3. As soon as the devices reach each other, a small green notice **"Connected to …"** appears at the bottom, and once everything is transferred **"Synced with …"**: then you can close the app on the phone. From then on you never pair again. The notices go away by themselves; a tap or a swipe to the side closes them at once.

**How it runs afterwards:** when both apps are open on the **same Wi-Fi** they sync by themselves: at start, when the app comes to the front again, a few seconds after every change and otherwise every 20 seconds. The PC sends its last changes when it closes, but only to a phone where the app is open right then. **So: before you stop on the PC, open BibleStudy on the phone for a moment** and wait for "Synced with …". To sync at once use **Sync**: on the PC at the bottom next to the version number, on the phone at the top of the **Tools** menu.

**Nothing gets lost:** if you marked on both devices apart, both have all the marks afterwards. If something changed on one side only, that side wins. If the same note was changed differently on both, both versions are kept. Deletions travel along, into the trash on the other device. A new or empty device never deletes anything. Synced are marks, notes, documents, bookmarks, tasks, prayer requests, reading progress, the reading plan, memory verses, vocabulary, and categories, colors and saved searches. What belongs to one device only (window size, font size, theme) stays on that device. Your own translations and materials are not synced yet.

**iPhone:** iOS does not let apps work in the background, so the iPhone syncs only **while BibleStudy is open**. Until then the changes simply wait on both sides. After 3 days without a sync the app reminds you. The automation below is the easiest way.

On the PC, Windows Firewall asks the first time: allow **Private networks** only. On the iPhone, iOS asks for permission for the **local network**: allow it (otherwise Settings → Privacy & Security → Local Network → BibleStudy).

## 19.4 The ready-made shortcut (iPhone)

You build nothing yourself. One ready-made shortcut does it all: the VPN goes on as soon as SideStore opens and off again when it closes. Every 6 days the iPhone re-signs BibleStudy by itself, so the 7-day limit never hits.

1. On the iPhone open in Safari: `https://trofline.github.io/BibleStudy/BibleStudy.shortcut` → **Download**.
2. Tap the download arrow at the top right → tap the file. It opens in **Shortcuts**.
3. **Add Shortcut**. Done.

If iOS asks on the first run whether the shortcut may use the VPN: **Always Allow**. It needs **LocalDevVPN** (19.1, step 4).

**Optional, sync when you get home:** **Shortcuts → Automation → +** → **Wi-Fi** → choose your Wi-Fi, tick **Joins** → **Run Immediately** → action **Open App** → **BibleStudy**. The app opens for a moment and syncs with the PC (19.3).

**Updates:** iOS lets no app install another without you, SideStore neither. So a new BibleStudy is always one tap: BibleStudy tells you about the new version and opens SideStore if you like; there tap **Update** at BibleStudy under **My Apps** (pull down in **Sources** first if it does not show yet). If an app has expired anyway: open SideStore and tap **Refresh** at BibleStudy; your data stays.

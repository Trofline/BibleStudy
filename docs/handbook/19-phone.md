# 19. On the phone (Android and iPhone)

BibleStudy also runs on your phone, with the same vaults, marks, notes and documents as on the PC. Both phone versions are **beta**: please report what does not work.

## 19.1 Installing on the iPhone (without a paid Apple account)

This works with **SideStore**. It signs the app with your own free Apple ID and renews it by itself afterwards. You need a PC or Mac with a cable **once**. All in all it takes about 10 minutes.

1. **Put SideStore on the iPhone (about 4 min).** On the PC open the program *iloader*, connect the iPhone with the cable and install SideStore (guide at docs.sidestore.io). If SideStore says the pairing file is missing: in iloader click **Manage Pairing File** at the bottom and place it for SideStore.
2. **Trust the profile and switch on Developer Mode (about 2 min).** iPhone: Settings → General → VPN & Device Management → trust your Apple ID. Settings → Privacy & Security → Developer Mode → switch on (the iPhone restarts).
3. **Add BibleStudy (about 2 min).** Sign in to SideStore, then tap this link on the iPhone: `sidestore://source?url=https://trofline.github.io/BibleStudy/sidestore.json`. Or in SideStore → Sources → **+** → `https://trofline.github.io/BibleStudy/sidestore.json`. Then BibleStudy → **Install**.
4. **Renew automatically (about 2 min).** A free Apple ID signs apps for 7 days only. An automation does it every night for you; step by step in **19.4**.

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
3. As soon as the devices reach each other, a green notice **"Connected to …"** appears. From then on you never pair again.

**How it runs afterwards:** when both apps are open on the **same Wi-Fi** they sync by themselves: at start, when the app comes to the front again, and a few seconds after every change. The PC sends its last changes when it closes. **Sync now** starts it at once.

**Nothing gets lost:** if you marked on both devices apart, both have all the marks afterwards. If something changed on one side only, that side wins. If the same note was changed differently on both, both versions are kept. Deletions travel along, into the trash on the other device. A new or empty device never deletes anything. Settings and materials are not synced yet.

**iPhone:** iOS does not let apps work in the background, so the iPhone syncs only **while BibleStudy is open**. Until then the changes simply wait on both sides. After 3 days without a sync the app reminds you. The automation below is the easiest way.

On the PC, Windows Firewall asks the first time: allow **Private networks** only. On the iPhone, iOS asks for permission for the **local network**: allow it (otherwise Settings → Privacy & Security → Local Network → BibleStudy).

## 19.4 iPhone automations (Shortcuts)

Two automations so you do not have to think of anything. Both are made in the **Shortcuts** app, in the **Automation** tab at the bottom.

**A) Sync when you get home**

1. **Shortcuts → Automation → +** (top right) → **Wi-Fi**.
2. **Network** → choose your home Wi-Fi, tick **Joins**.
3. Choose **Run Immediately** (not "Run After Confirmation"), **Next**.
4. **New Blank Automation** → action **Open App** → choose **BibleStudy** → **Done**.

When you come home, BibleStudy opens for a moment and syncs with the PC (the PC has to be on with BibleStudy open, also minimized in the tray).

**B) Renew the app every day (against the 7-day limit)**

1. **Shortcuts → Shortcuts → +** with these actions in this order: turn **on** the VPN of **StosVPN** (or LocalDevVPN), **Refresh All Apps** of **SideStore**, turn the VPN **off** again. Name it **Renew BibleStudy**.
2. **Shortcuts → Automation → +** → **Time of Day** → for example **3:00**, **Daily** → **Run Immediately** → **Next** → choose the shortcut **Renew BibleStudy**.
3. Leave the phone on the charger and on Wi-Fi at night.

The exact names of the actions depend on the version of SideStore and of the VPN app. Just type "SideStore" or "VPN" into the action search.

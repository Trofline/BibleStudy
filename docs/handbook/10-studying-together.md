# 10. Studying together

Study **connected** with other people: chat, share verses and documents, and read along in the same Bible passage. The connection is **encrypted, needs no account, and is free** (technology: in the same network a direct connection encrypted with ECDH and AES-GCM, with no server; across different networks a room on a small relay server, end-to-end encrypted).

## 10.1 Setting up the connection

Right-hand bar → **Together → Study together**. Enter your name first. The easiest way is a **room with a room code**: it works from anywhere, whatever network you are in.

### Over the internet with a room code (recommended)

Wherever you are (for example each at home, or one on mobile data), you meet in a **room**. Up to **five people** fit in one room.

Under **Over the internet (room code)**:

1. One person chooses **Create a room**. A short **room code** like `K7F2-QXM9` appears.
2. They send the code to the others by messenger (or read it out on the phone).
3. The others choose **Join a room**, type in the code and tap **Join**.
4. After a second or two you are connected. The room code stays at the top of the page so that someone else can still join.

Everybody sees a **check number**. If it is the same for all of you, nobody is in between.

**How it works:** The programs do not connect to each other directly (that fails with many internet connections) but each one to a small **relay server** on the internet, the way a browser opens a website. The relay only passes the messages on. Everything is **end-to-end encrypted**: the key is made on your devices from the room code; the relay does not know the code (only a hash of it as the room name) and cannot read anything. It stores nothing. You need no account and nothing to set up.

**You normally do not need ZeroTier.** Create a room, send the code, done. ZeroTier is only the fallback for the rare case that the daily limit is used up.

**Daily limit:** The relay runs on the free plan and therefore has a daily limit for all users together. When it is used up, connect **in the same network** (next section) until the next day. If you are in different places, all join the same free **ZeroTier** network (zerotier.com: one person creates a network, the others join with its network ID). Then you are “in the same network” for BibleStudy and connect without any relay. As a rough size: two people with camera, sound and pointer can spend about 25 hours a day together in a room; the picture quality hardly changes that. The limit starts again every day at midnight UTC.

### In the same network (no codes)

If you are on the same Wi-Fi or in the same ZeroTier or Tailscale network, you need no codes:

1. Both open the **Study together** page and enter their name.
2. Under **In the same network** the other person's name appears after a few seconds. Tap **Connect with …**.
3. The other person sees the request and taps **Accept**.
4. Both see a **check number**. If it is the same for both of you, nobody is in between.

If nobody shows up, some networks do not allow searching. Then open **Connect by address** and enter the other person's address. Your own address is shown right below; with ZeroTier it is also in the ZeroTier app under “Managed IPs”.

### Several people (home group)

You can be as many as you like. Whoever is connected already adds someone under **Add another person**. Every program passes on what it gets: if Anna is connected with Ben and Ben with Carla, all three still see each other. Each person has their own color.

### Friends

For a person in the same network, **Remember as friend** appears, and also in a room where only the two of you are. Afterwards they are listed under **Friends**, and one tap connects you, even when they do not show up in the list (then by the address used last). When you are in different places, both of you tap **Over the internet**: you meet in your own room on the relay, without a room code. That uses exactly as much of the daily limit as a room with a code, and nothing while neither of you has it open. With **Accept automatically** you are no longer asked for this friend. A friend is recognised by a shared secret swapped when remembering, not only by the name.

## 10.2 What you can do

- **Chat.** The chat is a tile of its own with a fixed height: new messages appear at the bottom and stay in view, the buttons around it do not move. If you scrolled up, **New messages ↓** takes you back down. You see when the other person is typing. While the page is closed, a small notice shows new messages.
- **Chat saved.** The chat and shared verses are saved only on your device, separately for each person. When you are in a room with the same person again (recognised by their fixed ID, not by the name), your earlier chat is at the top; with someone else a new one begins. Turn it off or empty it: **Settings → My data → Chats**.
- **Lead:** whoever taps **I lead** sets the passage. Everyone else jumps along automatically until they turn the page themselves; then **Follow again** appears.
- **Questions about the text:** a shared list. Everyone can add questions and tick them when they have been discussed.
- **Shared notepad:** one page everyone writes on (if two write at the same time, the version sent last counts). Can be saved as a document.
- **React:** tap a chat message or choose **React** for a verse (♥, Amen, ✓, !, ?). A reaction to a verse shows over the verse for a moment.
- **Save meeting as a document:** conversation, shared verses, questions and notepad go into your vault as one document.
- **See live:** when you read the same chapter, you see the verses the other person clicks (framed in purple, with their name) and their mouse pointer. If they selected words, they appear at the frame. Their highlights show as bars beside the verses (take them over with one button), and while they read aloud, the verse being read is underlined. Switch off with **Show my mouse pointer**.
- **Show here:** the other person gets a notice with a button to your place.
- **Share highlights:** of the chosen verses, this chapter, this book or all. Taking them over adds only new ones.
- **Share notes:** this chapter, this book or all. Where there is already a note, the other one is added below it.
- **Send vault:** a copy of a whole vault. The other person can **view it as a guest** (gone when the program closes) or **save it as a new vault**.
- **Share verses.**
- **Share documents** (as a copy; what you send is limited in size, and long documents are shortened).
- **Send along your own Bible passage** and **follow the other person's passage.**
- See each other's highlights (Together highlights).

### Talking and camera (on a computer)

Under **Talking and camera**:

- **Hold to talk:** while you hold the button, the others hear you (like a walkie-talkie).
- **Microphone on (call):** like a call, until you switch it off again. With headphones there is no echo. The sound goes in full quality (48 kHz), not like a telephone; voice messages stay smaller (16 kHz).
- **Voice message:** tap, speak, tap again; 2 minutes at most. The others play it with **▶ Play**.
- **Camera on:** your picture appears as a small tile for everyone; **Large video** shows all pictures in a window of their own.
- **Picture quality:** under **Choose microphone and camera**: low, medium, **HD (720p)** (default) or **Full HD (1080p)**, plus **30 or 60 frames per second**. The camera goes as a video stream (H.264): only what changes from picture to picture is sent. Per person with a camera, HD needs about 2.5 Mbit/s to send, Full HD about 5 Mbit/s, half as much again at 60 frames per second. If the picture stutters or arrives late, choose a lower level. When the picture cannot keep up, BibleStudy jumps to the next complete picture (at most two seconds), so the delay does not keep growing. Everyone needs the newest BibleStudy version for this. On the phone you see the others' pictures.
- **Hear the others** switches the others' sound off and on. Under **Choose microphone and camera** you pick the devices.

On Windows, talking works without anything extra. For the camera (and on Mac and Linux also for sound) BibleStudy needs the free program **FFmpeg**: on Windows in the download manager under **Camera and sound** (about 110 MB), on a Mac with `brew install ffmpeg`, on Linux with the package manager (`sudo apt install ffmpeg`). If FFmpeg is installed already, it is used. Talking and camera on the phone come later.

Nothing is recorded, except a voice message you send yourself. Sound and picture go through the same encrypted connection as everything else.

## 10.3 Privacy

- Only what you send is transmitted.
- In the same network the other person sees **your internet address** (this is a consequence of the direct connection).
- Over the internet (room code) the relay server sees your internet address, like every website you open. It cannot read any content and stores nothing. It runs at Cloudflare; its program code is open in the `relay` folder of the source code.
- Texts from plugin translations are **never** sent to others.
- Before you use it for the first time, the program asks for your consent.

## 10.4 If it doesn't work

**In the same network:**

- Both of you must be **online at the same time**.
- A **VPN** or a **strict firewall** can prevent the connection. On Windows choose “Allow access” at the first start; on a Mac grant the “Local Network” permission.
- If it does not work in the same network, simply use the room code instead.

**With a room code:**

- Everybody must have the room **open at the same time**. Whoever created the room keeps the page open until the others are there.
- “Nobody came”: check the code. It has 8 characters; upper or lower case and the dash do not matter.
- “There are already five people in this room”: ask for a new code.
- “The relay server cannot be reached right now”: check the internet connection. On the free plan the relay has a daily limit; when it is reached, connect in the same network until the next day, or from different places through a shared ZeroTier network (see above).

### Your own relay server

If you like, you can run the relay yourself (free at Cloudflare, instructions in `relay/README.md` in the source code) and enter it in the *Study together* tab under **Own relay server (optional)**. Everybody in the room must then enter the same one.

## 10.5 Strategies for groups

- **Online home group:** One person creates a room, leads (**I lead**) and reads the text aloud; the others follow along.
- **Learning as a pair:** Fill out a document together using the *Section study* template and compare the copies.
- **Mentoring:** Send questions as a document, and send answers back as a document.

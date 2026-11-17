# 10. Studying together

Study **directly connected** with another person: chat, share verses and documents, and read along in the same Bible passage. The connection is **encrypted, needs no server, no account, and is free** (technology: in the same network a direct connection encrypted with ECDH and AES-GCM; across different networks WebRTC).

## 10.1 Setting up the connection

Right-hand bar → **Together → Study together**. Enter your name first.

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

For a person in the same network, **Remember as friend** appears. Afterwards they are listed under **Friends**, and one tap connects you, even when they do not show up in the list (then by the address used last). With **Accept automatically** you are no longer asked for this friend. A friend is recognised by a shared secret swapped when remembering, not only by the name.

### In different networks (with codes, beta)

Across different networks the connection does not always work. Tested and reliable is the way in the same network: if you are far apart, both join the same ZeroTier network (free) and connect as described above.

Under **With codes (beta, other networks)**:

1. Person A chooses **Create an invitation**. A code appears.
2. Person A sends the code to person B by messenger.
3. Person B chooses **Accept an invitation**, pastes in the code, and receives a **reply code**.
4. Person B sends the reply code back; person A pastes it in.
5. Now you are directly and securely connected.

## 10.2 What you can do

- **Chat.** You see when the other person is typing. While the page is closed, a small notice shows new messages.
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
- **Microphone on (call):** like a call, until you switch it off again. With headphones there is no echo.
- **Voice message:** tap, speak, tap again; 2 minutes at most. The others play it with **▶ Play**.
- **Camera on:** your picture appears as a small tile for everyone; **Large video** shows all pictures in a window of their own.
- **Hear the others** switches the others' sound off and on. Under **Choose microphone and camera** you pick the devices.

On Windows, talking works without anything extra. For the camera (and on Mac and Linux also for sound) BibleStudy needs the free program **FFmpeg**: on Windows in the download manager under **Camera and sound** (about 110 MB), on a Mac with `brew install ffmpeg`, on Linux with the package manager (`sudo apt install ffmpeg`). If FFmpeg is installed already, it is used. Talking and camera on the phone come later.

Nothing is recorded, except a voice message you send yourself. Sound and picture go through the same encrypted connection as everything else.

## 10.3 Privacy

- Only what you send is transmitted.
- The other person sees **your internet address** (this is a consequence of the direct connection).
- Texts from plugin translations are **never** sent to others.
- Before you use it for the first time, the program asks for your consent.

## 10.4 If it doesn't work

- Both of you must be **online at the same time**.
- The connection goes **directly from computer to computer**, with no server in between. This works reliably when you are **in the same network** (for example the same Wi-Fi). Across different networks it only works if your connections allow it. It often fails on mobile data, company, school and hotel networks, and with some providers (CGNAT).
- A **VPN** or a **strict firewall** can prevent the connection. On Windows choose “Allow access” at the first start; on a Mac grant the “Local Network” permission.
- The codes are long; copy them completely and without spaces at the beginning or end.
- Try again with a new invitation.

### If you are not in the same network

There are two ways that are not built into BibleStudy:

- **A private network such as ZeroTier or Tailscale** (both offer a free plan for individuals, but need an account). Both of you install the program and join the same network. Afterwards the computers behave as if they were on the same Wi-Fi, and you connect as described above under *In the same network*, without codes. These programs are independent products with their own licences and terms; BibleStudy does not ship them and is not affiliated with them.
- **Your own relay server (TURN)** that forwards the traffic. This is for advanced users and usually costs money or effort of your own. The data stays encrypted. In BibleStudy you enter it in the *Study together* tab under **Own relay server (optional)**: address (for example `turn:server.example:3478`), user name and password. Best if both of you enter the same, and then create new codes. The password is stored unencrypted in the settings. We have not yet tested the connection with a relay server.

## 10.5 Strategies for groups

- **Online home group:** One person reads the text aloud and shares their passage; the other follows along.
- **Learning as a pair:** Fill out a document together using the *Section study* template and compare the copies.
- **Mentoring:** Send questions as a document, and send answers back as a document.

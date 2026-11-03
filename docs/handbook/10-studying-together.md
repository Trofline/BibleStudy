# 10. Studying together

Study **directly connected** with another person: chat, share verses and documents, and read along in the same Bible passage. The connection is **encrypted, needs no server, no account, and is free** (technology: WebRTC).

## 10.1 Setting up the connection

Right-hand bar → **Together → Study together**.

1. Person A chooses **Create an invitation**. A code appears.
2. Person A sends the code to person B by messenger.
3. Person B chooses **Accept an invitation**, pastes in the code, and receives a **reply code**.
4. Person B sends the reply code back; person A pastes it in.
5. Now you are directly and securely connected.

## 10.2 What you can do

- **Chat.**
- **Share verses.**
- **Share documents** (as a copy; what you send is limited in size, and long documents are shortened).
- **Send along your own Bible passage** and **follow the other person's passage.**
- See each other's highlights (Together highlights).

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

- **A private network such as ZeroTier or Tailscale** (both offer a free plan for individuals, but need an account). Both of you install the program and join the same network. Afterwards the computers behave as if they were on the same Wi-Fi. Then create new codes. These programs are independent products with their own licences and terms; BibleStudy does not ship them and is not affiliated with them. We have not yet tested the connection through them.
- **Your own relay server (TURN)** that forwards the traffic. This is for advanced users and usually costs money or effort of your own. The data stays encrypted. In BibleStudy you enter it in the *Study together* tab under **Own relay server (optional)**: address (for example `turn:server.example:3478`), user name and password. Best if both of you enter the same, and then create new codes. The password is stored unencrypted in the settings. We have not yet tested the connection with a relay server.

## 10.5 Strategies for groups

- **Online home group:** One person reads the text aloud and shares their passage; the other follows along.
- **Learning as a pair:** Fill out a document together using the *Section study* template and compare the copies.
- **Mentoring:** Send questions as a document, and send answers back as a document.

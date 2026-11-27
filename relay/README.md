# BibleStudy relay

The small server that lets people **study together across different networks** (the "room code" in *Study together*).
Up to five BibleStudy programs meet in a room and the relay passes their messages on. That is all it does.

## Why it is safe

- **It cannot read anything.** Every program seals its messages end to end (ECDH key agreement mixed with the room code, AES-GCM).
  The relay only sees sealed bytes.
- **It does not learn the room code.** Rooms are named by a SHA-256 hash of the code.
- **It stores nothing.** No database, no logs of content; a room exists only while people are in it.
- **It holds no secrets.** There is no password or key in the app or in this folder. Deploying needs a Cloudflare login on the
  computer that deploys, and nothing of that login ever goes into the app or the repository.
- **Limits against misuse:** at most five people per room, at most 256 KB per message, people who vanish are removed after about a minute.
- **No bill by surprise:** on the free Workers plan, Cloudflare simply refuses requests for the rest of the day once the daily limit is
  reached. There is no credit card on the free plan.

## Deploy (free Cloudflare account)

You need Node.js 20 or newer.

```
cd relay
npm install
npx wrangler login      # opens the browser once to sign in to Cloudflare
npx wrangler deploy
```

On Windows PowerShell, write `npx.cmd` instead of `npx` if scripts are blocked.

```
```

`wrangler deploy` prints the address (the built-in one is `https://biblestudy-relay.troflineblack.workers.dev`), for example `https://biblestudy-relay.<your-name>.workers.dev`.
That address (as `wss://…`) is the built-in relay in `src/BibleStudy.App/MainWindow.Collab.cs` (`DefaultCollabServer`);
anyone else can enter their own under *Study together → Own relay server (optional)*.

Check it: opening the address in a browser shows `BibleStudy relay: ok`.

## Test locally

```
npx wrangler dev                  # starts it on http://127.0.0.1:8787
node test/relay.test.mjs          # in a second window
```

## Protocol

See the comment at the top of `src/index.js`. In short: `GET /r/<64 hex letters>` as a WebSocket; the relay numbers the people of a
room and says `hello`, `join`, `left` and `full` in text; binary messages start with the number of the person they are for (0 = all),
which the relay replaces with the number of the sender.

## Free plan in numbers (checked November 2026)

Durable Objects on the free plan: 100,000 requests a day (reset at 00:00 UTC). Incoming WebSocket messages count 20 to one request,
**whatever their size**; outgoing messages and pings are free, and a room that waits between messages is not billed for time. So the relay
manages about 2 million incoming messages a day, for all rooms together.

BibleStudy gathers what it sends for 100 ms into one message (cut into pieces of 200 KB), so one person sends about 10 a second while
active, camera included: the camera goes as a video stream (H.264), and even Full HD at 60 pictures a second fits in one message per
100 ms. Two people with camera, voice and pointer are about 76,000 messages an hour, so the daily limit holds for about 25 hours of such
a session. (Single JPEG pictures, as before, would split a Full HD batch into 4 or 5 pieces: about 6 hours.)

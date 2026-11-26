// The relay for "Studying together" across networks (see RelayLink.cs in the program).
//
// Up to five BibleStudy programs meet in a room: GET /r/<room> as a WebSocket, where <room> is a hash of the room code (64 hex letters),
// so the relay never learns the code. Everything the programs send is sealed end to end; the relay cannot read it, it only passes it on.
//
// Every person in a room gets a small number (1 … 255). The relay speaks in text:
//   "hello <me> <others>"  to someone who comes in: their own number and those of the others already there (comma separated, may be empty)
//   "join <n>"             someone came in
//   "left <n>"             someone went
//   "full"                 the room already has five people (then the connection is closed)
// and answers the text "ping" with "pong" (without waking up, so a quiet room costs nothing).
// Binary messages: the first byte is the person it is for (0 = everybody else). The relay puts the number of the sender there instead and passes it on.
//
// Protection against misuse: at most five people per room, at most 256 KB per message, rooms hold nothing, and nothing is stored or logged.

const MAX_PEOPLE = 5;
const MAX_MESSAGE = 256 * 1024;
const GONE_AFTER = 70_000;   // the programs ping every 20 seconds: whoever is silent this long has vanished (battery, network)

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/" || url.pathname === "/health") {
      return new Response("BibleStudy relay: ok\n", { headers: { "content-type": "text/plain; charset=utf-8" } });
    }
    const m = url.pathname.match(/^\/r\/([0-9a-f]{64})$/);
    if (!m) return new Response("not found\n", { status: 404 });
    if (request.headers.get("Upgrade")?.toLowerCase() !== "websocket") return new Response("expected a WebSocket\n", { status: 426 });
    return env.ROOMS.get(env.ROOMS.idFromName(m[1])).fetch(request);
  },
};

export class Room {
  constructor(state) {
    this.state = state;
    this.state.setWebSocketAutoResponse(new WebSocketRequestResponsePair("ping", "pong"));
  }

  people() {
    return this.state.getWebSockets().filter(ws => ws.readyState === 1 && ws.deserializeAttachment()?.id);
  }

  async fetch() {
    const now = Date.now();
    for (const o of this.people()) {
      const a = o.deserializeAttachment();
      const last = Math.max(this.state.getWebSocketAutoResponseTimestamp(o)?.getTime() ?? 0, a.since ?? 0);
      if (now - last > GONE_AFTER) {
        try { o.close(4002, "gone"); } catch { }
        o.serializeAttachment({});
        this.tell(o, "left " + a.id);
      }
    }
    const pair = new WebSocketPair();
    const [client, server] = Object.values(pair);
    const others = this.people();
    if (others.length >= MAX_PEOPLE) {
      server.accept();
      server.send("full");
      server.close(4001, "full");
      return new Response(null, { status: 101, webSocket: client });
    }
    const used = new Set(others.map(o => o.deserializeAttachment().id));
    let id = 1;
    while (used.has(id)) id++;
    this.state.acceptWebSocket(server);
    server.serializeAttachment({ id, since: now });
    trySend(server, `hello ${id} ${[...used].sort((x, y) => x - y).join(",")}`);
    this.tell(server, "join " + id);
    return new Response(null, { status: 101, webSocket: client });
  }

  tell(except, text) {
    for (const o of this.people()) if (o !== except) trySend(o, text);
  }

  async webSocketMessage(ws, message) {
    if (typeof message === "string") return;   // only the relay itself speaks in text
    const me = ws.deserializeAttachment();
    if (!me?.id) return;
    if (message.byteLength > MAX_MESSAGE || message.byteLength < 2) { ws.close(1009, "too big"); return; }
    const now = Date.now();
    if (now - (me.since ?? 0) > 10_000) ws.serializeAttachment({ id: me.id, since: now });   // a message counts as a sign of life too
    const data = new Uint8Array(message.slice(0));
    const target = data[0];
    data[0] = me.id;
    for (const o of this.people()) {
      if (o === ws) continue;
      if (target === 0 || o.deserializeAttachment().id === target) trySend(o, data);
    }
  }

  async webSocketClose(ws, code) {
    const me = ws.deserializeAttachment();
    if (me?.id) this.tell(ws, "left " + me.id);
    ws.serializeAttachment({});
    try { ws.close(code === 1005 || code === 1006 ? 1000 : code, "bye"); } catch { }
  }

  async webSocketError(ws) {
    await this.webSocketClose(ws, 1011);
  }
}

function trySend(ws, data) {
  try { ws.send(data); } catch { }
}

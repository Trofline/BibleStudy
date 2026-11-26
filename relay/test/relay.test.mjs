// Checks the relay against a running copy: start it with "npx wrangler dev", then "node test/relay.test.mjs [ws://127.0.0.1:8787]".
import assert from "node:assert/strict";
import { createHash, randomBytes } from "node:crypto";

const base = process.argv[2] ?? "ws://127.0.0.1:8787";
const room = createHash("sha256").update("test " + randomBytes(8).toString("hex")).digest("hex");

function open(r = room) {
  const ws = new WebSocket(`${base}/r/${r}`);
  ws.binaryType = "arraybuffer";
  const inbox = [], waiters = [];
  const put = v => { const w = waiters.shift(); w ? w(v) : inbox.push(v); };
  ws.onmessage = e => put(typeof e.data === "string" ? e.data : Buffer.from(e.data));
  ws.onclose = e => put(`<closed ${e.code}>`);
  const next = (ms = 3000) => inbox.length ? Promise.resolve(inbox.shift()) : new Promise((res, rej) => { waiters.push(res); setTimeout(() => rej(new Error("timeout")), ms); });
  const quiet = async (ms = 300) => { await new Promise(r => setTimeout(r, ms)); assert.equal(inbox.length, 0, "nothing more arrived: " + inbox[0]); };
  return new Promise((res, rej) => { ws.onopen = () => res({ ws, next, quiet }); ws.onerror = rej; });
}
const bin = (...b) => new Uint8Array(b);

const a = await open();
assert.equal(await a.next(), "hello 1 ");
const b = await open();
assert.equal(await b.next(), "hello 2 1");
assert.equal(await a.next(), "join 2");

a.ws.send(bin(0, 1, 2, 3));                       // to everybody
assert.deepEqual([...await b.next()], [1, 1, 2, 3], "the relay puts the sender in the first byte");
const big = randomBytes(200 * 1024); big[0] = 0;
b.ws.send(big);
const got = await a.next();
assert.equal(got[0], 2); assert.ok(got.subarray(1).equals(big.subarray(1)), "a large message arrives whole");

a.ws.send("ping");
assert.equal(await a.next(), "pong");
a.ws.send("text from a program is not passed on");
await b.quiet();

const c = await open();
assert.equal(await c.next(), "hello 3 1,2");
assert.equal(await a.next(), "join 3");
assert.equal(await b.next(), "join 3");
c.ws.send(bin(2, 42));                            // only to person 2
assert.deepEqual([...await b.next()], [3, 42]);
await a.quiet();

const d = await open(); await d.next();
const e = await open(); await e.next();
for (const x of [a, b, c]) { assert.equal(await x.next(), "join 4"); assert.equal(await x.next(), "join 5"); }
assert.equal(await d.next(), "join 5");
const f = await open();
assert.equal(await f.next(), "full");

b.ws.close();
for (const x of [a, c, d, e]) assert.equal(await x.next(), "left 2");
const g = await open();                            // the place and the number are free again
assert.equal(await g.next(), "hello 2 1,3,4,5");

const tooBig = new Uint8Array(300 * 1024);
g.ws.send(tooBig);
assert.equal(await g.next(), "<closed 1009>");
assert.equal(await a.next(), "join 2");
assert.equal(await a.next(), "left 2", "a message that is too big closes the connection");

const other = await open(createHash("sha256").update("another room " + Date.now()).digest("hex"));
assert.equal(await other.next(), "hello 1 ");
other.ws.send(bin(0, 7));
await a.quiet();

assert.equal((await fetch(base.replace(/^ws/, "http") + "/r/not-a-room")).status, 404);
for (const x of [a, c, d, e, other]) x.ws.close();
console.log("relay: all checks passed");
process.exit(0);

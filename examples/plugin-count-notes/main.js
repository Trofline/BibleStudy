// Example plugin for BibleStudy: commands that work with the program itself (see PLUGINS.md).
// BibleStudy starts this program and sends it one JSON line per request. For its own work the plugin calls the program's
// local interface (the same one the AI connection uses) at BIBLESTUDY_API with the secret BIBLESTUDY_TOKEN.
const readline = require('readline');

async function call(tool, args = {}) {
  const response = await fetch(process.env.BIBLESTUDY_API, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Token': process.env.BIBLESTUDY_TOKEN },
    body: JSON.stringify({ tool, args }),
  });
  const result = await response.json();
  if (result.error) throw new Error(result.error);
  return result;
}

const commands = {
  'count-notes': async () => {
    const { notes } = await call('get_notes');
    return { message: `Du hast ${notes.length} Notizen zu Versen.` };
  },
  'open-john-3-16': async () => {
    await call('navigate', { book: 'JHN', chapter: 3, verse: 16 });
    return { message: 'Johannes 3,16 aufgeschlagen.' };
  },
};

const rl = readline.createInterface({ input: process.stdin });
rl.on('line', async (line) => {
  let request;
  try { request = JSON.parse(line); } catch { return; }
  try {
    if (request.method !== 'command') throw new Error('unsupported');
    const run = commands[request.params?.id];
    if (!run) throw new Error('unknown command');
    process.stdout.write(JSON.stringify({ id: request.id, result: await run() }) + '\n');
  } catch (e) {
    process.stdout.write(JSON.stringify({ id: request.id, error: String(e.message || e) }) + '\n');
  }
});

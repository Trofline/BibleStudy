// Example plugin for BibleStudy. Protocol: one JSON object per line on stdin, one answer per line on stdout (see PLUGINS.md).
// This one answers from the file bible.json next to it. A real plugin would fetch the text from wherever its author has the right to.
const fs = require('fs');
const path = require('path');
const readline = require('readline');

const data = JSON.parse(fs.readFileSync(path.join(__dirname, 'bible.json'), 'utf8'));
const find = (id) => data.translations.find((t) => t.id === id);

const methods = {
  translations: () => data.translations.map(({ books, ...info }) => info),
  books: ({ translation }) => {
    const t = find(translation);
    if (!t) throw new Error('unknown translation');
    return Object.entries(t.books).map(([id, b]) => ({ id, name: b.name, chapters: Math.max(...Object.keys(b.chapters).map(Number)) }));
  },
  chapter: ({ translation, book, chapter }) => {
    const verses = find(translation)?.books?.[book]?.chapters?.[String(chapter)];
    if (!verses) throw new Error('chapter not available');
    return { verses: verses.map((text, i) => ({ number: i + 1, text })) };
  },
  // "all" is optional (needed for the full-text search). Not offered here: unknown methods answer with an error.
};

const rl = readline.createInterface({ input: process.stdin });
rl.on('line', (line) => {
  let request;
  try { request = JSON.parse(line); } catch { return; }
  try {
    const handler = methods[request.method];
    if (!handler) throw new Error('unsupported');
    process.stdout.write(JSON.stringify({ id: request.id, result: handler(request.params || {}) }) + '\n');
  } catch (e) {
    process.stdout.write(JSON.stringify({ id: request.id, error: String(e.message || e) }) + '\n');
  }
});

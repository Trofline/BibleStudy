# Plugins for BibleStudy

A plugin is a small program that **extends BibleStudy**. It can deliver **Bible translations**, bring **commands** to the search box (Ctrl+K), and – through the program's local interface – **work with BibleStudy itself**: read and change notes, highlights and documents, open passages, search and more. It can be written in any language (Python, Node.js, C#, a shell script …). BibleStudy starts it, sends one request per line on its standard input and reads one answer per line from its standard output.

> **Important – read this first**
>
> * A plugin runs **with your rights on your computer** and can use the program's interface, which means it can **read and change your data in BibleStudy**. Allow only plugins from people you trust.
> * The BibleStudy project **does not review plugins and does not review their content**. It takes no responsibility for any plugin or for the texts it delivers.
> * Whether a plugin may fetch and show a text is a question of that text's rights holder, the source's terms of use and the law. That is the responsibility of the plugin's author and of whoever uses it. **Please do not write plugins that break the terms of a website or ignore a copyright.**
> * Texts of plugin translations are held in memory only, are never written to disk by BibleStudy and are never sent to other people (not in "Study together").

## Installing a plugin

1. In BibleStudy: *Settings → Plugins → Open plugin folder*.
2. Put the plugin's folder in there (it contains a `plugin.json`).
3. Click *Reload*, read what the plugin says about itself and tick *Allow this plugin*.
4. The translations of the plugin appear in the translation list like any other.

If the `plugin.json` of an allowed plugin changes, BibleStudy asks for permission again.

## plugin.json

```json
{
  "id": "my-plugin",
  "name": "My plugin",
  "version": "1.0",
  "author": "Your name",
  "description": "What it does.",
  "contentNotice": "Where the texts come from and under which rights – the user reads this before allowing the plugin.",
  "provides": ["bibles", "commands"],
  "commands": [ { "id": "do-it", "title": "Do the thing" } ],
  "command": "node",
  "args": ["main.js"]
}
```

* `provides`: `"bibles"` (the plugin delivers translations, the default if nothing is given) and/or `"commands"`.
* `commands`: entries for the search box (Ctrl+K); when one is picked, BibleStudy sends the plugin the request `command`.
* `id`: a–z, 0–9 and `-`, at most 40 characters, unique.
* `command`: a program on the PATH (`python`, `node` …) or a file inside the plugin folder (`./run.sh`, `plugin.exe`). The plugin folder is the working directory.
* `contentNotice`: please be honest and specific. It is shown in the permission dialog.

## Protocol

Each request is one line of JSON, each answer one line of JSON (UTF-8). Lines on standard output that do not start with `{` are ignored, so log output is harmless; standard error is dropped.

Request: `{"id": 7, "method": "chapter", "params": {…}}`  
Answer: `{"id": 7, "result": …}` or `{"id": 7, "error": "what went wrong"}` — the `id` must be copied from the request. A request that takes longer than 30 seconds is cancelled and the plugin is restarted.

### `translations` (required)

No parameters. Result: a list of

```json
{ "id": "xyz", "name": "Full name", "shortName": "XYZ", "language": "de", "languageName": "Deutsch",
  "attribution": "who has to be named", "licenseUrl": "https://…", "website": "https://…", "textDirection": "ltr" }
```

Only `id` and `name` are required.

### `books` (required)

Params `{"translation": "xyz"}`. Result: a list of `{"id": "GEN", "name": "1. Mose", "chapters": 50}`. `id` is the USFM book id (GEN, EXO … REV, 66 books); other books are ignored.

### `chapter` (required)

Params `{"translation": "xyz", "book": "GEN", "chapter": 1}`. Result: `{"verses": [{"number": 1, "text": "…"}, …]}`.

### `all` (optional)

Params `{"translation": "xyz"}`. Result: a list of `{"book": "GEN", "chapter": 1, "number": 1, "text": "…"}` — the whole text at once. Only needed for BibleStudy's full-text search. Without it the translation can be read, but not searched; BibleStudy never fetches a whole translation chapter by chapter through a plugin.

### `command` (for plugins with `commands`)

Params `{"id": "do-it"}`. Result: `{"message": "shown in the status bar"}` (optional).

## Working with BibleStudy itself

While it runs, a plugin finds the program's local interface in its environment:

| Variable | Meaning |
|---|---|
| `BIBLESTUDY_API` | URL for POST requests, `http://127.0.0.1:<port>/call` |
| `BIBLESTUDY_TOKEN` | secret, sent as header `X-Token` with every call |
| `BIBLESTUDY_DATA` | the folder with BibleStudy's data |
| `BIBLESTUDY_VERSION` | program version |

A call is `POST` with `{"tool": "<name>", "args": {…}}` and answers JSON. It is the same interface the AI connection (MCP) uses, with tools such as `get_passage`, `search_text`, `navigate`, `get_notes`, `set_note`, `list_marks`, `add_mark`, `list_documents`, `create_document`, `queue_add`, `todo_add` and `get_app_state`. Anything the program can do through it, an allowed plugin can do – this is why a plugin must be allowed explicitly and why you should only allow plugins you trust.

## Examples

* `examples/plugin-local-file` – a translation plugin (Node.js) that reads a small file next to it.
* `examples/plugin-count-notes` – commands that work with the program itself: counts your notes, opens a passage. Copy one as a starting point.

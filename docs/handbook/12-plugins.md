# 12. Plugins

A plugin is a small program that **extends** BibleStudy. It can supply translations, bring commands for the search box (Ctrl+K), and work with the program through the local interface.

> **Important:** A plugin runs **with your permissions** on your computer and can **read and change your data in BibleStudy**. Only allow plugins from people you trust. The project does not review plugins and takes no responsibility for them or for the texts they provide.

## 12.1 Installing a plugin

1. *Settings → Plugins → Open plugin folder*.
2. Put the plugin's folder (containing a `plugin.json`) in there.
3. Click **Reload**, read what the plugin says about itself, and tick **Allow this plugin**.
4. The plugin's translations appear in the translation list like all the others.

If the `plugin.json` of an allowed plugin changes, the program asks for permission again.

## 12.2 Bundled plugins

The project's `plugins` folder and every release include ready-made plugins, for example **Add Strong's numbers**. It adds an **estimated** numbering to translations that have no Strong's numbers. No guarantee; please double-check.

## 12.3 Plugin texts

Texts from plugin translations are kept in memory only, are never written to disk, and are never sent to anyone else (not even in "Study together").

## 12.4 Writing your own plugins

The guide is in [PLUGINS.md](../../PLUGINS.md) (also available in the program under *Settings → Guide for plugin authors*). In short:

- A plugin is a folder with a `plugin.json` (id, name, version, author, description, **contentNotice**, `provides`, `command`, `args`).
- Requests and responses are **one line of JSON each**, over standard input and output. A request that takes longer than 30 seconds is aborted and the plugin is restarted.
- The `translations` method is required and returns the translations.
- Examples are in the `examples` folder.

Please don't write plugins that violate a website's terms of use or ignore a copyright.

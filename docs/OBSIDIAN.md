---
status: live
owner: Maestro
type: guide
---

# Obsidian: start here

Obsidian is optional. It is a reader and editor for the markdown files that are already in this repo. Nothing depends on it: the tools, tests and agents read the files directly.

## The five things you need first

1. **Open the repo root as the vault.** In Obsidian choose "Open folder as vault" and pick the `CluckWars` folder. Big folders (`Library/`, `Assets/`, `Builds/`, `graphify-out/` and so on) are hidden by the setting "Excluded files" in `.obsidian/app.json`, so search and the graph stay clean.
2. **Links are plain relative markdown links**, like `[GDD](GDD.md)`. Do not type `[[wikilinks]]`: GitHub and the repo tools cannot follow them. The setting "Automatically update internal links" is off on purpose. If you rename or move a doc in Obsidian, nothing rewrites the other docs, so search the repo for the old name and fix links by hand.
3. **Properties are the block at the top of a note** between `---` lines. Live docs carry `status`, `owner` and `type`. Archived docs say `status: archived` and name what replaced them in `superseded-by`. The table in [docs.base](docs.base) lists docs by these fields.
4. **Images** live next to the doc that uses them (for example `docs/design/...`) or in `docs/attachments/`. Embed with `![alt](relative/path.png)`. Obsidian shows them inline and GitHub does too. Keep each image in one place and link to it; do not copy it.
5. **Graph, backlinks and Canvas.** The Backlinks panel shows who links to the open note. Graph view maps the links. [architecture.canvas](architecture.canvas) is a hand-made map of how the main docs fit together. The graph is sparse until the docs link to each other more.

## Rules of the house

- `docs/` and the code are the source of truth. The old Notion pages were not kept: they were stale copies of the GDD, TDD and ART docs.
- Install no community plugins. The core plugins switched on are in `.obsidian/core-plugins.json`.
- Commit only `.obsidian/app.json`, `core-plugins.json` and `appearance.json`. Workspace, cache and graph files are per machine and ignored by git.
- The rules docs and `docs/FILE_STRUCTURE.md` are edited by the `librarian` agent. Edit them by hand only for a quick fix.
- Very large notes are slow in Obsidian. `docs/STATE.md` is about 880 KB. If it hangs when opened, close that tab and read it in an editor instead.

## Not confirmed yet

These settings were written from documentation, not tested in the app. Open Obsidian once and check each one:

- Settings, Files and links: "Use Wikilinks" is off, "New link format" is "Relative path to file", "Automatically update internal links" is off, "Default location for new attachments" is `docs/attachments`.
- Settings, Files and links, Excluded files: the folder list from `app.json` is shown.
- `docs.base` opens as a table. If Obsidian reports a YAML error, fix the filter line.
- Image resize syntax (`![alt|400](path)`) works in your version.

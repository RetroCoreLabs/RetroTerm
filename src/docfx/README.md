# The documentation site

Live at <https://retrocorelabs.github.io/RetroTerm/>, published by `.github/workflows/docs.yml`
on every push to `main`. DocFX, like the other RetroCore Labs .NET repositories: the API
reference is generated from the XML documentation comments of the eight projects, and the
documents under `docs/` are published beside it as articles.

## What is here

| File | What it is |
|---|---|
| `docfx.json` | The DocFX project: which projects to document, which documents to publish, the theme. The article list in it is written by `gen_toc.py`. |
| `toc.yml` | The page tree. Written by `gen_toc.py`; do not edit. |
| `gen_toc.py` | Reads `git ls-files` and the policy in the script, and writes the article list and the tree. `--check` fails when either is stale. |
| `index.md` | The landing page. |
| `api/index.md` | The hand-written front of the API reference. Everything else under `api/` is generated and git-ignored. |

The articles are the repository's own files under `docs/`: the guides and references, and the
transcribed manuals under `docs/spec/`. Plans, bug lists, dated reviews and the run sheets stay
on GitHub; `gen_toc.py` names each one it keeps off and why. The scanned PDFs are not shipped.

## Building it

From the repository root. DocFX is a global tool: `dotnet tool install -g docfx`.

```powershell
python src/docfx/gen_toc.py          # after adding or removing a document under docs/
docfx src/docfx/docfx.json           # must end "Build succeeded." with 0 warnings, 0 errors
```

DocFX restores and compiles the projects itself to read the comments, so the first build takes
a few minutes. The site lands in `src/docfx/_site/`; `docfx src/docfx/docfx.json --serve` previews
it. CI runs `gen_toc.py --check` before the build, so a document added without rerunning the
script fails the workflow rather than silently missing from the tree.

## Adding a document

Put the Markdown file under `docs/`, track it with git, run `gen_toc.py`, and commit the file
with the updated `docfx.json` and `toc.yml`. It lands in the section its name matches (see
`SECTIONS` in the script) or under "Documents". To keep one off the site, add it to
`EXCLUDED_DOCS` with the reason.

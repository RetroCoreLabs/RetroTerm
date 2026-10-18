# The documentation site

Live at <https://retrocorelabs.github.io/RetroTerm/>, published by `.github/workflows/docs.yml`
on every push to `main`.

## What is here

| File | What it is |
|---|---|
| `mkdocs.yml` | The hand-written settings: theme, extensions, validation. No nav, no exclusions. |
| `gen_nav.py` | Reads `git ls-files` and the policy written in the script, and writes the generated child config. |
| `hooks.py` | Rewrites links to files that are not on the site (PDF scans, source, working notes) to github.com. |
| `requirements.txt` | Pinned Python packages. |
| `generated/` | Output of `gen_nav.py`: `mkdocs.yml` (nav, exclusions) and `site-files.txt`. Git-ignored, remade on every build. |

The pages are the repository's own files: `README.md` is the home page, the documents under
`docs/` are the guides and references, and `docs/spec/` is the transcribed manuals. Nothing is
copied. Plans, bug lists, dated reviews and the run sheets stay on GitHub; `gen_nav.py` names
each one it keeps off and why.

## Building it

From the repository root, with Python 3.11 or newer:

```powershell
python -m pip install -r src/docs-site/requirements.txt
python src/docs-site/gen_nav.py
python -m mkdocs build --strict -f src/docs-site/generated/mkdocs.yml -d "$env:TEMP/retroterm-site"
```

The output folder must be outside the checkout: `docs_dir` is the whole repository and MkDocs
refuses to write a site inside it. `--strict` turns every warning into a failed build; CI runs
the same command, so a build that is not clean here is not clean there either.

To preview: `python -m mkdocs serve -f src/docs-site/generated/mkdocs.yml`, then open the
address it prints.

## Adding a document

Put the Markdown file under `docs/` and track it with git; it is on the site after the next
push, in the section its name matches (see `SECTIONS` in `gen_nav.py`) or under "Documents".
To keep one off the site, add it to `EXCLUDED_DOCS` with the reason.

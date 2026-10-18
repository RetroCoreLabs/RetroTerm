"""Generate the navigation and exclusion list for the RetroTerm documentation site.

    python src/docs-site/gen_nav.py            # from the repository root (any cwd works)

Writes src/docs-site/generated/mkdocs.yml (a child config that INHERITs ../mkdocs.yml and
adds docs_dir, nav and exclude_docs) and src/docs-site/generated/site-files.txt (the pages
that are on the site, one repo-relative path per line, read by hooks.py). Both files are
git-ignored and remade on every build, locally and in CI.

WHY A SCRIPT: the house standard (repo-standards, standards/DOCS-SITE.md, section 0 rule 4)
says the nav comes from the tracked files, never from a hand-kept list, because a hand-kept
list is stale the day a file is added. The only hand-written part is the POLICY below: which
documents are reference material for the site and which are working notes that stay on
GitHub. A new document under docs/ lands on the site automatically; keeping one off the
site takes a line here with a reason.

WHY ONLY TRACKED FILES: an untracked note in a working tree once made a strict CI build fail
on a file the clean checkout did not have. `git ls-files` is the list of what a clone has.
"""

from __future__ import annotations

import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = os.path.join(HERE, "generated")

# ---------------------------------------------------------------------------------- policy

# Documents under docs/ that stay OFF the site, each with the reason. Everything else that is
# tracked under docs/*.md and docs/spec/**/*.md goes on. The decision "reference only" was
# the owner's, 18 October 2026: plans, bug lists, dated reviews and the by-hand run sheets
# are working material and read as such on GitHub, not as documentation of the product.
EXCLUDED_DOCS = {
    "docs/PLAN.md": "outstanding work, changes every session",
    "docs/BUGS.md": "open defects, changes every session",
    "docs/TEXT-BITMAP-STUDY.md": "a design study asking a question, not an answer a user needs",
    "docs/TDV-KEYBOARD-HARDWARE-VALIDATION.md": "a checklist of what still has to be confirmed on hardware",
    "docs/tdv-246-no-image.md": "dated analysis notes; docs/KBD-ND-246.md is the reference",
}

# A file name carrying a date is a dated review, gap study, survey or sweep: a record of what
# was found on that day, not a document that is kept current. docs/ARCHITECTURE-REVIEW-...-2026-08-08.md
# and friends match this; so does everything in docs/manual-tests/ by the folder rule below.
DATED = re.compile(r"-20\d\d-\d\d-\d\d\.md$")

# Folders under docs/ that are working material as a whole.
EXCLUDED_FOLDERS = {
    "docs/manual-tests/": "the by-hand test run sheets and their findings",
    "docs/images/readme/": "nothing tracked here; the README's demo.gif was never committed",
}

# Sections of the nav, in display order. A document under docs/ is placed by the first rule
# whose prefix matches its file name; one that matches nothing goes to "Documents", so a new
# file is never silently dropped from the tree.
SECTIONS = [
    ("Using RetroTerm", ("docs/DISTRIBUTION-README.md", "docs/MCP-AND-SCRIPTING.md", "docs/ND100-GATEWAY.md")),
    ("Terminal references", ("docs/TDV-", "docs/KBD-")),
    ("OPCOM", ("docs/OPCOM-", "docs/ND110-OPCOM-")),
    ("Project", ("docs/LOGO-DESIGN-GUIDE.md",)),
]
FALLBACK_SECTION = "Documents"

# Nav labels come from each document's own H1. The one exception: DISTRIBUTION-README.md is
# the README that ships beside the exe, so its H1 is rightly "RetroTerm" - which says nothing
# in a tree of pages that are all RetroTerm. The label below is the file's own first sentence
# of purpose, not an invented title.
TITLE_OVERRIDES = {
    "docs/DISTRIBUTION-README.md": "What ships with the exe",
}

# docs/spec holds the transcribed manuals, one folder per terminal family. Folder names are
# the terminal names; this table only supplies a readable label where the folder name alone
# does not read well. A folder not listed here is shown under its own name.
SPEC_FOLDER_TITLES = {
    "DEC": "DEC VT terminals",
    "ECMA": "ECMA-48",
    "Keyboards": "ND keyboards",
    "TDV1200": "ND Display Terminal 1200",
    "TDV2115": "TDV 2115",
    "TDV2200": "TDV 2200",
    "TDV2215": "TDV 2215",
    "Tektronix": "Tektronix and ND graphics",
}

# What is copied to the site besides the pages: pictures the pages show. Anything else that is
# tracked (PDF scans, .txt, .c, .zip, .url, ...) is linked to github.com by hooks.py instead,
# so the site stays small and a 40 MB scan is not shipped on every deploy. The owner's choice,
# 18 October 2026.
IMAGE_EXT = {".png", ".jpg", ".jpeg", ".gif", ".svg", ".bmp", ".ico"}

# Paths outside docs/ that the site needs: the home page and the logo it shows.
HOME = "README.md"
EXTRA_PREFIXES = ("src/assets/",)

# ------------------------------------------------------------------------------ helpers


def tracked_files() -> list[str]:
    out = subprocess.run(["git", "ls-files", "-z"], cwd=REPO, check=True, capture_output=True).stdout
    return [p for p in out.decode("utf-8").split("\0") if p]


def first_heading(path: str) -> str | None:
    """The first Markdown H1 of a file, or None. Titles come from the documents themselves."""
    with open(os.path.join(REPO, path), encoding="utf-8", errors="replace") as f:
        for _ in range(60):
            line = f.readline()
            if not line:
                break
            if line.startswith("# "):
                return line[2:].strip()
    return None


def title_for(path: str) -> str:
    if path in TITLE_OVERRIDES:
        return TITLE_OVERRIDES[path]
    h = first_heading(path)
    if h:
        # Markdown emphasis and code marks do not belong in a nav label.
        h = re.sub(r"[`*_]", "", h)
        return h
    # No H1: the file name, with dashes and underscores as spaces. Not invented text - it is
    # the name the file already has.
    return os.path.splitext(os.path.basename(path))[0].replace("-", " ").replace("_", " ")


def yaml_str(s: str) -> str:
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def section_for(path: str) -> str:
    for name, prefixes in SECTIONS:
        for prefix in prefixes:
            if path == prefix or path.startswith(prefix):
                return name
    return FALLBACK_SECTION


def excluded_reason(path: str) -> str | None:
    if path in EXCLUDED_DOCS:
        return EXCLUDED_DOCS[path]
    for folder, reason in EXCLUDED_FOLDERS.items():
        if path.startswith(folder):
            return reason
    if DATED.search(path):
        return "dated review or study"
    return None


# --------------------------------------------------------------------------------- main


def main() -> int:
    files = tracked_files()

    pages: list[str] = []          # markdown files on the site
    shipped: list[str] = []        # non-page files copied to the site (images)
    excluded_md: list[tuple[str, str]] = []
    other: list[str] = []          # tracked files that are neither: excluded from the build

    for p in files:
        ext = os.path.splitext(p)[1].lower()
        in_docs = p.startswith("docs/")
        in_extra = p.startswith(EXTRA_PREFIXES)

        if p == HOME:
            pages.append(p)
        elif in_docs and ext == ".md":
            reason = excluded_reason(p)
            if reason:
                excluded_md.append((p, reason))
            else:
                pages.append(p)
        elif (in_docs or in_extra) and ext in IMAGE_EXT:
            shipped.append(p)
        else:
            other.append(p)

    # ---- nav
    sections: dict[str, list[tuple[str, str]]] = {}
    spec: dict[str, list[tuple[str, str]]] = {}
    for p in pages:
        if p == HOME:
            continue
        if p.startswith("docs/spec/"):
            rel = p[len("docs/spec/"):]
            folder = rel.split("/")[0] if "/" in rel else ""
            spec.setdefault(folder, []).append((title_for(p), p))
        else:
            sections.setdefault(section_for(p), []).append((title_for(p), p))

    lines: list[str] = []
    w = lines.append
    w("# GENERATED by src/docs-site/gen_nav.py from `git ls-files` - do not edit; rerun the script.")
    w("INHERIT: ../mkdocs.yml")
    w("docs_dir: ../../..")
    w("nav:")
    w("  - Home: " + HOME)

    ordered = [name for name, _ in SECTIONS] + [FALLBACK_SECTION]
    for name in ordered:
        if name not in sections:
            continue
        w("  - " + yaml_str(name) + ":")
        for title, p in sorted(sections[name], key=lambda t: t[0].lower()):
            w("      - " + yaml_str(title) + ": " + p)

    if spec:
        w('  - "Manuals and source material":')
        for folder in sorted(spec, key=lambda f: (f != "", f.lower())):
            entries = sorted(spec[folder], key=lambda t: (t[0].lower(), t[1]))
            # Several transcriptions of one manual share an H1 ("TDV 2215" three times): the
            # second and later carry their file name so the reader can tell them apart.
            seen: dict[str, int] = {}
            labelled = []
            for title, p in entries:
                n = seen.get(title, 0)
                seen[title] = n + 1
                labelled.append((title if n == 0 else title + " (" + os.path.basename(p) + ")", p))
            if folder == "":
                for title, p in labelled:
                    w("      - " + yaml_str(title) + ": " + p)
                continue
            w("      - " + yaml_str(SPEC_FOLDER_TITLES.get(folder, folder)) + ":")
            for title, p in labelled:
                w("          - " + yaml_str(title) + ": " + p)

    # ---- exclusions: everything tracked that is not a page or a shipped picture, plus the
    # untracked folders a working tree may hold (build output, the local publish folder,
    # fetched corpora, machine-local files). gitignore syntax; one pattern per line.
    w("exclude_docs: |")
    w("  # untracked or ignored folders a checkout may contain")
    for pat in ("/src/", "!/src/assets/", "/.github/", "/.claude/", "/.vs/", "/publish/", "/garbage/",
                "/CLAUDE.md", "/README.template.md", "/LICENSE", "/.gitignore", "/.gitattributes",
                "/docs/images/readme/"):
        w("  " + pat)
    # gitignore syntax has no trailing comments: "/docs/BUGS.md   # reason" is ONE pattern and
    # matches nothing (measured: all 24 excluded documents were built the first time). The
    # reason goes on its own line above the pattern.
    w("  # working notes kept on GitHub, not on the site (see EXCLUDED_DOCS in gen_nav.py)")
    for p, reason in sorted(excluded_md):
        w("  # " + reason)
        w("  /" + p)
    w("  # tracked files that are neither pages nor pictures: scans, text dumps, tool files")
    for p in sorted(other):
        if p.startswith("docs/"):
            w("  /" + p)

    os.makedirs(OUT_DIR, exist_ok=True)
    with open(os.path.join(OUT_DIR, "mkdocs.yml"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    with open(os.path.join(OUT_DIR, "site-files.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(sorted(pages) + sorted(shipped)) + "\n")

    print("pages on the site: " + str(len(pages)) + " (" + str(len(excluded_md)) + " documents kept off, "
          + str(len(shipped)) + " pictures shipped)")
    for p, reason in sorted(excluded_md):
        print("  off: " + p + " - " + reason, file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())

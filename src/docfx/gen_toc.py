"""Write the article list and the page tree of the RetroTerm documentation site.

    python src/docfx/gen_toc.py            # rewrite docfx.json's article list and toc.yml
    python src/docfx/gen_toc.py --check    # exit 1 if either is stale (CI runs this)

WHY A SCRIPT: the house standard (repo-standards, standards/DOCS-SITE.md, section 0 rule 4)
says the page tree comes from the tracked files, never from a hand-kept list, because a
hand-kept list is stale the day a file is added. DocFX reads a static docfx.json and toc.yml,
so this script writes those two from `git ls-files` and the POLICY below, and both are
committed; --check in CI catches a document added without rerunning it.

The only hand-written part is the policy: which documents are reference material for the
site and which are working notes that stay on GitHub. The owner's decision, 18 October 2026:
plans, bug lists, dated reviews and the by-hand run sheets are working material.

WHY ONLY TRACKED FILES: an untracked note in a working tree once made a strict CI build fail
on a file the clean checkout did not have. `git ls-files` is the list of what a clone has.
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
DOCFX_JSON = os.path.join(HERE, "docfx.json")
TOC_YML = os.path.join(HERE, "toc.yml")

# ---------------------------------------------------------------------------------- policy

EXCLUDED_DOCS = {
    "docs/PLAN.md": "outstanding work, changes every session",
    "docs/BUGS.md": "open defects, changes every session",
    "docs/TEXT-BITMAP-STUDY.md": "a design study asking a question, not an answer a user needs",
    "docs/TDV-KEYBOARD-HARDWARE-VALIDATION.md": "a checklist of what still has to be confirmed on hardware",
    "docs/tdv-246-no-image.md": "dated analysis notes; docs/KBD-ND-246.md is the reference",
}

# A file name carrying a date is a dated review, gap study, survey or sweep: a record of what
# was found that day, not a document kept current.
DATED = re.compile(r"-20\d\d-\d\d-\d\d\.md$")

EXCLUDED_FOLDERS = {
    "docs/manual-tests/": "the by-hand test run sheets and their findings",
}

# Sections of the tree, in display order. A document under docs/ goes to the first section
# whose prefix matches; one that matches nothing goes to "Documents", so a new file is never
# silently dropped.
SECTIONS = [
    ("Using RetroTerm", ("docs/DISTRIBUTION-README.md", "docs/MCP-AND-SCRIPTING.md", "docs/ND100-GATEWAY.md")),
    ("Terminal references", ("docs/TDV-", "docs/KBD-")),
    ("OPCOM", ("docs/OPCOM-", "docs/ND110-OPCOM-")),
    ("Project", ("docs/LOGO-DESIGN-GUIDE.md",)),
]
FALLBACK_SECTION = "Documents"

# Labels come from each document's own H1. One exception: DISTRIBUTION-README.md is the README
# that ships beside the exe, so its H1 is rightly "RetroTerm" - which says nothing in a tree
# where every page is RetroTerm.
TITLE_OVERRIDES = {
    "docs/DISTRIBUTION-README.md": "What ships with the exe",
}

# docs/spec holds the transcribed manuals, one folder per terminal family.
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

# ------------------------------------------------------------------------------ helpers


def tracked_files() -> list[str]:
    out = subprocess.run(["git", "ls-files", "-z"], cwd=REPO, check=True, capture_output=True).stdout
    return [p for p in out.decode("utf-8").split("\0") if p]


def first_heading(path: str) -> str | None:
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
        return re.sub(r"[`*_]", "", h)
    # No H1: the file's own name, dashes and underscores as spaces. Not invented text.
    return os.path.splitext(os.path.basename(path))[0].replace("-", " ").replace("_", " ")


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


def yq(s: str) -> str:
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


# ------------------------------------------------------------------------------- compose


def compose() -> tuple[list[str], str, list[tuple[str, str]]]:
    """The article list for docfx.json, the toc.yml text, and what was kept off with why."""
    pages: list[str] = []
    off: list[tuple[str, str]] = []
    for p in tracked_files():
        if p.startswith("docs/") and p.lower().endswith(".md"):
            reason = excluded_reason(p)
            if reason:
                off.append((p, reason))
            else:
                pages.append(p)
    pages.sort()

    sections: dict[str, list[tuple[str, str]]] = {}
    spec: dict[str, list[tuple[str, str]]] = {}
    for p in pages:
        if p.startswith("docs/spec/"):
            rel = p[len("docs/spec/"):]
            folder = rel.split("/")[0] if "/" in rel else ""
            spec.setdefault(folder, []).append((title_for(p), p))
        else:
            sections.setdefault(section_for(p), []).append((title_for(p), p))

    # toc.yml hrefs are relative to this folder (src/docfx); the articles are mapped from the
    # repository root by the "src": "../.." entry in docfx.json.
    def href(p: str) -> str:
        return "../../" + p

    out: list[str] = []
    w = out.append
    w("# GENERATED by gen_toc.py from `git ls-files` - do not edit; rerun the script.")
    w("- name: Home")
    w("  href: index.md")
    w("- name: Documentation")
    w("  items:")
    ordered = [name for name, _ in SECTIONS] + [FALLBACK_SECTION]
    for name in ordered:
        if name not in sections:
            continue
        w("    - name: " + yq(name))
        w("      items:")
        for title, p in sorted(sections[name], key=lambda t: t[0].lower()):
            w("        - name: " + yq(title))
            w("          href: " + yq(href(p)))
    w("- name: Manuals and source material")
    w("  items:")
    for folder in sorted(spec, key=lambda f: (f != "", f.lower())):
        entries = sorted(spec[folder], key=lambda t: (t[0].lower(), t[1]))
        # Several transcriptions of one manual share an H1 ("TDV 2215" three times): the second
        # and later carry their file name so the reader can tell them apart.
        seen: dict[str, int] = {}
        labelled = []
        for title, p in entries:
            n = seen.get(title, 0)
            seen[title] = n + 1
            labelled.append((title if n == 0 else title + " (" + os.path.basename(p) + ")", p))
        if folder == "":
            for title, p in labelled:
                w("    - name: " + yq(title))
                w("      href: " + yq(href(p)))
            continue
        w("    - name: " + yq(SPEC_FOLDER_TITLES.get(folder, folder)))
        w("      items:")
        for title, p in labelled:
            w("        - name: " + yq(title))
            w("          href: " + yq(href(p)))
    w("- name: API Reference")
    w("  href: api/")
    w("  topicHref: api/index.md")
    return pages, "\n".join(out) + "\n", off


def docfx_with_articles(pages: list[str]) -> str:
    with open(DOCFX_JSON, encoding="utf-8") as f:
        cfg = json.load(f)
    # The article group is the one mapped from the repository root. Its dest is "" on purpose:
    # the source paths already start with docs/, and a dest of "docs" doubled it to docs/docs/
    # on the first published site (measured 18 October 2026).
    hit = [c for c in cfg["build"]["content"] if c.get("src") == "../.."]
    if len(hit) != 1:
        raise SystemExit("docfx.json: expected exactly one build.content entry with src '../..'")
    hit[0]["files"] = pages
    return json.dumps(cfg, indent=2, ensure_ascii=False) + "\n"


def read(path: str) -> str:
    with open(path, encoding="utf-8") as f:
        return f.read()


def main(argv: list[str]) -> int:
    check = "--check" in argv
    pages, toc, off = compose()
    docfx = docfx_with_articles(pages)

    stale = []
    if read(TOC_YML) != toc if os.path.exists(TOC_YML) else True:
        stale.append("toc.yml")
    if read(DOCFX_JSON) != docfx:
        stale.append("docfx.json")

    if check:
        if stale:
            print("stale: " + ", ".join(stale) + " - run python src/docfx/gen_toc.py and commit the result",
                  file=sys.stderr)
            return 1
        print("gen_toc --check: docfx.json and toc.yml match the tracked files (" + str(len(pages)) + " articles)")
        return 0

    with open(TOC_YML, "w", encoding="utf-8", newline="\n") as f:
        f.write(toc)
    with open(DOCFX_JSON, "w", encoding="utf-8", newline="\n") as f:
        f.write(docfx)
    print("articles on the site: " + str(len(pages)) + " (" + str(len(off)) + " documents kept off); wrote "
          + ", ".join(stale or ["nothing new"]))
    for p, reason in sorted(off):
        print("  off: " + p + " - " + reason, file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

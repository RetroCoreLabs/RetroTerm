"""MkDocs hooks for the RetroTerm site: links to files that are not on the site go to GitHub.

The site holds the Markdown pages and the pictures they show (see gen_nav.py). The documents
also link to things that are deliberately NOT on the site: the scanned PDF manuals in
docs/spec, source files under src/, the LICENSE, the working notes kept on GitHub, text dumps.
Left alone, each of those is a broken link that fails the strict build - or worse, a link
that quietly 404s on the live site. This hook rewrites every such relative link to the file
on github.com, where it exists.

A relative link to a Markdown page that IS on the site is left for MkDocs to resolve and
check, so a genuinely broken link still fails the build.

Raw HTML (<a href>, <img src>) is not touched by MkDocs' own link handling; the README's logo
is raw HTML pointing into src/assets/, which gen_nav.py ships, so it needs no rewrite.
"""

from __future__ import annotations

import os
import posixpath
import re

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
BLOB = "https://github.com/RetroCoreLabs/RetroTerm/blob/main/"

_site_files: set[str] | None = None

# [text](target) and [text](target "title"), but not images (![...]) - those must stay
# relative so MkDocs copies and checks them. The text may itself be an image, as in a badge:
# [![licence](https://img.shields.io/...)](LICENSE) - the first version of this pattern
# stopped at the inner "]" and left the README's LICENSE link dead on the built page.
LINK = re.compile(
    r"(?<!!)\["                                   # opening bracket of a link, not an image
    r"((?:[^\[\]]|!\[[^\]]*\]\([^)]*\))*)"        # text: plain characters or one nested image
    r"\]\(([^)\s]+)((?:\s+\"[^\"]*\")?)\)")


def _site_files_set() -> set[str]:
    global _site_files
    if _site_files is None:
        path = os.path.join(HERE, "generated", "site-files.txt")
        with open(path, encoding="utf-8") as f:
            _site_files = {line.strip() for line in f if line.strip()}
    return _site_files


def on_page_markdown(markdown: str, page, config, files):
    src = page.file.src_uri                     # repo-relative, forward slashes
    base = posixpath.dirname(src)
    on_site = _site_files_set()

    def rewrite(m: re.Match) -> str:
        text, target, title = m.group(1), m.group(2), m.group(3)
        if re.match(r"^[a-zA-Z][a-zA-Z0-9+.-]*:", target) or target.startswith("#") or target.startswith("/"):
            return m.group(0)                   # absolute URL, data: URI, anchor, site-absolute
        path, _, fragment = target.partition("#")
        resolved = posixpath.normpath(posixpath.join(base, path)) if path else src
        if resolved.startswith("../"):
            return m.group(0)                   # climbs out of the repo; MkDocs will warn
        if resolved in on_site:
            return m.group(0)                   # a page or picture on the site: MkDocs checks it
        if not os.path.exists(os.path.join(REPO, resolved)):
            return m.group(0)                   # does not exist anywhere: leave it, the build fails on it
        url = BLOB + resolved + (("#" + fragment) if fragment else "")
        return "[" + text + "](" + url + title + ")"

    return LINK.sub(rewrite, markdown)

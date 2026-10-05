# Makefile for building and installing RetroTerm on Linux.
#
# TARGETS
#   make             Build only: publish the desktop app (self-contained, single-file).
#   make install     Build AND install for your user, no root needed. Puts the program,
#                    the application-menu entry and the icon under ~/.local. Start it from
#                    the application menu so the desktop shows the icon (log out and in once
#                    if it is not listed yet).
#   make && sudo make install
#                    Install system-wide under /usr/local. Build as yourself first, then
#                    let sudo only copy the result. Never run plain `sudo make install`
#                    on an unbuilt tree: it refuses, because a build run as root leaves
#                    root-owned files in bin/ and obj/ that break your next normal build.
#   make uninstall   Remove what install put there, icon and menu entry included. After
#                    a system-wide install: sudo make uninstall. Installed with a custom
#                    PREFIX? Pass the same PREFIX again.
#   make clean       Remove build output.
#
# WHAT GETS INSTALLED (PREFIX is ~/.local, or /usr/local when run as root; override it with
# `make install PREFIX=/some/where`; DESTDIR is honoured for packaging)
#   $(PREFIX)/lib/retroterm/RetroTerm.Desktop            the program
#   $(PREFIX)/bin/retroterm                              command on your PATH, runs the program
#   $(PREFIX)/share/applications/retroterm.desktop       the application-menu entry
#   $(PREFIX)/share/icons/hicolor/{scalable,512x512}/apps/retroterm.{svg,png}   the icon
#
# The desktop app is a self-contained .NET publish. The menu entry sets StartupWMClass to
# "retroterm", which is the window class Program.cs gives the window; the desktop uses that
# match to show the icon on the taskbar or dock, so the two must stay in step.

# Not root: install into the home folder, so a plain `make install` just works.
# Root (sudo make install): install system-wide, and only copy what `make` already built -
# the build itself must never run as root, or it leaves root-owned files in bin/ and obj/.
ifeq ($(shell id -u),0)
PREFIX      ?= /usr/local
else
PREFIX      ?= $(HOME)/.local
endif
BINDIR      ?= $(PREFIX)/bin
LIBDIR      ?= $(PREFIX)/lib
APPDIR      := $(LIBDIR)/retroterm
DATADIR     ?= $(PREFIX)/share

CONFIG      ?= Release
RID         ?= linux-x64
PROJECT     := src/RetroTerm.Desktop/RetroTerm.Desktop.csproj
PUBLISH_DIR := src/RetroTerm.Desktop/bin/$(CONFIG)/net10.0/$(RID)/publish
APP_BIN     := RetroTerm.Desktop

DOTNET      ?= dotnet
INSTALL     ?= install

.PHONY: all build publish check-built install uninstall clean

all: publish

build: publish

publish:
	$(DOTNET) publish $(PROJECT) -c $(CONFIG) -r $(RID) --self-contained true
	@test -x "$(PUBLISH_DIR)/$(APP_BIN)" || { echo "ERROR: $(PUBLISH_DIR)/$(APP_BIN) not found after publish" >&2; exit 1; }

ifeq ($(shell id -u),0)
INSTALL_DEP := check-built
else
INSTALL_DEP := publish
endif

check-built:
	@test -x "$(PUBLISH_DIR)/$(APP_BIN)" || { echo "Not built yet. Run 'make' as your normal user first, then 'sudo make install'. The build must not run as root." >&2; exit 1; }

install: $(INSTALL_DEP)
	$(INSTALL) -d "$(DESTDIR)$(APPDIR)"
	$(INSTALL) -d "$(DESTDIR)$(BINDIR)"
	cp -a --remove-destination "$(PUBLISH_DIR)/." "$(DESTDIR)$(APPDIR)/"
	chmod +x "$(DESTDIR)$(APPDIR)/$(APP_BIN)"
	printf '#!/bin/sh\nexec "%s/%s" "$$@"\n' "$(APPDIR)" "$(APP_BIN)" > "$(DESTDIR)$(BINDIR)/retroterm"
	chmod +x "$(DESTDIR)$(BINDIR)/retroterm"
	$(INSTALL) -d "$(DESTDIR)$(DATADIR)/applications"
	sed 's|^Exec=.*|Exec=$(BINDIR)/retroterm|' packaging/linux/retroterm.desktop > "$(DESTDIR)$(DATADIR)/applications/retroterm.desktop"
	$(INSTALL) -d "$(DESTDIR)$(DATADIR)/icons/hicolor/scalable/apps" "$(DESTDIR)$(DATADIR)/icons/hicolor/512x512/apps"
	$(INSTALL) -m 644 assets/RetroTerm.svg "$(DESTDIR)$(DATADIR)/icons/hicolor/scalable/apps/retroterm.svg"
	$(INSTALL) -m 644 assets/RetroTerm-512.png "$(DESTDIR)$(DATADIR)/icons/hicolor/512x512/apps/retroterm.png"
	-update-desktop-database "$(DESTDIR)$(DATADIR)/applications" 2>/dev/null
	-gtk-update-icon-cache -q -t "$(DESTDIR)$(DATADIR)/icons/hicolor" 2>/dev/null
	@echo "Installed: $(DESTDIR)$(BINDIR)/retroterm -> $(DESTDIR)$(APPDIR)/$(APP_BIN), with menu entry and icon"

uninstall:
	rm -f  "$(DESTDIR)$(BINDIR)/retroterm"
	rm -rf "$(DESTDIR)$(APPDIR)"
	rm -f  "$(DESTDIR)$(DATADIR)/applications/retroterm.desktop"
	rm -f  "$(DESTDIR)$(DATADIR)/icons/hicolor/scalable/apps/retroterm.svg" "$(DESTDIR)$(DATADIR)/icons/hicolor/512x512/apps/retroterm.png"

clean:
	$(DOTNET) clean $(PROJECT) -c $(CONFIG) || true
	rm -rf src/RetroTerm.Desktop/bin src/RetroTerm.Desktop/obj

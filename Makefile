# Makefile for building and installing RetroTerm on Linux.
#
#   make             - publish the desktop app (self-contained, single-file)
#   make install     - install to $(PREFIX) (run as root for /usr/local)
#   make uninstall   - remove the installed files
#   make clean       - remove build output
#
# The desktop app is a self-contained .NET publish: one launcher executable
# plus several native shared libraries (Skia, HarfBuzz, ANGLE, Avalonia
# native) that must sit next to it. We follow the FHS:
#
#   $(LIBDIR)/retroterm/   - launcher + native libs (the "binaries")
#   $(BINDIR)/retroterm    - small wrapper script on $$PATH
#
# So `make install` does land the user-facing `retroterm` command in
# /usr/local/bin, while keeping the rest of the payload out of it.

PREFIX      ?= /usr/local
BINDIR      ?= $(PREFIX)/bin
LIBDIR      ?= $(PREFIX)/lib
APPDIR      := $(LIBDIR)/retroterm

CONFIG      ?= Release
RID         ?= linux-x64
PROJECT     := src/RetroTerm.Desktop/RetroTerm.Desktop.csproj
PUBLISH_DIR := src/RetroTerm.Desktop/bin/$(CONFIG)/net9.0/$(RID)/publish
APP_BIN     := RetroTerm.Desktop

DOTNET      ?= dotnet
INSTALL     ?= install

.PHONY: all build publish install uninstall clean

all: publish

build: publish

publish:
	$(DOTNET) publish $(PROJECT) -c $(CONFIG) -r $(RID) --self-contained true
	@test -x "$(PUBLISH_DIR)/$(APP_BIN)" || { echo "ERROR: $(PUBLISH_DIR)/$(APP_BIN) not found after publish" >&2; exit 1; }

install: publish
	$(INSTALL) -d "$(DESTDIR)$(APPDIR)"
	$(INSTALL) -d "$(DESTDIR)$(BINDIR)"
	cp -a "$(PUBLISH_DIR)/." "$(DESTDIR)$(APPDIR)/"
	chmod +x "$(DESTDIR)$(APPDIR)/$(APP_BIN)"
	printf '#!/bin/sh\nexec "%s/%s" "$$@"\n' "$(APPDIR)" "$(APP_BIN)" > "$(DESTDIR)$(BINDIR)/retroterm"
	chmod +x "$(DESTDIR)$(BINDIR)/retroterm"
	@echo "Installed: $(DESTDIR)$(BINDIR)/retroterm -> $(DESTDIR)$(APPDIR)/$(APP_BIN)"

uninstall:
	rm -f  "$(DESTDIR)$(BINDIR)/retroterm"
	rm -rf "$(DESTDIR)$(APPDIR)"

clean:
	$(DOTNET) clean $(PROJECT) -c $(CONFIG) || true
	rm -rf src/RetroTerm.Desktop/bin src/RetroTerm.Desktop/obj

#!/bin/sh
# macOS Finder launcher; use sh if executable permissions were lost in the ZIP.
ANIMA_INSTALL_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
sh "$ANIMA_INSTALL_DIR/Install-AnimaSupport.sh" "$@"
ANIMA_INSTALL_EXIT=$?
printf '\nPress Return to close. '
read -r ANIMA_INSTALL_REPLY
exit "$ANIMA_INSTALL_EXIT"

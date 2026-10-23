#!/usr/bin/env bash
# Builds the AppImage from a self-contained linux-x64 publish folder, bundling libvlc, its plugins and
# their shared-library dependencies from the build machine (so it runs without VLC installed).
# Usage: packaging/linux/build-appimage.sh <version> <publish-dir> <output-dir>
# Requires: appimagetool on PATH, and libvlc5 + vlc-plugin-base + vlc-plugin-video-output installed.
# Build on the oldest supported distro (glibc compatibility goes forward only). Run from the repository root.
set -euo pipefail

version="$1"
publish_dir="$(realpath "$2")"
out_dir="$(mkdir -p "$3" && realpath "$3")"
multiarch="$(gcc -print-multiarch 2>/dev/null || echo x86_64-linux-gnu)"
syslib="/usr/lib/$multiarch"

appdir="$(mktemp -d)/Lumen.AppDir"
mkdir -p "$appdir/usr/lib/lumen" "$appdir/usr/lib/vlc" "$appdir/usr/share/vlc"

# App
cp -a "$publish_dir/." "$appdir/usr/lib/lumen/"
chmod +x "$appdir/usr/lib/lumen/Lumen"
install -m 755 packaging/linux/AppRun "$appdir/AppRun"
install -m 644 packaging/linux/lumen.desktop "$appdir/lumen.desktop"
install -m 644 packaging/assets/lumen-256.png "$appdir/lumen.png"
ln -s lumen.png "$appdir/.DirIcon"

# libvlc + plugins (skip GUI interface plugins: Lumen is the interface)
cp -a "$syslib"/libvlc.so.5* "$syslib"/libvlccore.so.9* "$appdir/usr/lib/"
cp -a "$syslib"/vlc/libvlc_*.so* "$appdir/usr/lib/vlc/"
cp -a "$syslib/vlc/plugins" "$appdir/usr/lib/vlc/"
rm -rf "$appdir/usr/lib/vlc/plugins/gui"
[ -d "$syslib/vlc/lua" ] && cp -a "$syslib/vlc/lua" "$appdir/usr/share/vlc/"
if [ -x "$syslib/vlc/vlc-cache-gen" ]; then
  rm -f "$appdir/usr/lib/vlc/plugins/plugins.dat"
  "$syslib/vlc/vlc-cache-gen" "$appdir/usr/lib/vlc/plugins" || true
fi

# ICU, which .NET loads at runtime with dlopen (so ldd does not see it).
cp -a "$syslib"/libicuuc.so.* "$syslib"/libicui18n.so.* "$syslib"/libicudata.so.* "$appdir/usr/lib/"

# Shared-library dependencies of libvlc and every plugin, minus libraries that must come from the host
# (glibc, the C++ runtime, graphics drivers, X11/Wayland, audio servers, fonts, D-Bus). Based on the AppImage excludelist.
exclude='^(ld-linux|linux-vdso|libc|libm|libdl|libpthread|librt|libresolv|libutil|libnsl|libanl|libgcc_s|libstdc\+\+|libGL|libGLX|libGLdispatch|libEGL|libGLES|libOpenGL|libgbm|libdrm|libva|libvdpau|libX11|libX11-xcb|libxcb|libXau|libXdmcp|libXext|libXrender|libXfixes|libXi|libXrandr|libXcursor|libXinerama|libXss|libSM|libICE|libwayland-[a-z]+|libxkbcommon|libasound|libpulse|libjack|libpipewire|libfontconfig|libfreetype|libharfbuzz|libexpat|libz|libdbus-1|libsystemd|libudev|libuuid|libcom_err|libgpg-error|libselinux)\.so'
collect() {
  ldd "$1" 2>/dev/null | awk '/=> \//{print $3}' | while read -r lib; do
    name="$(basename "$lib")"
    if ! echo "$name" | grep -Eq "$exclude" && [ ! -e "$appdir/usr/lib/$name" ] && [ ! -e "$appdir/usr/lib/vlc/$name" ]; then
      cp -L "$lib" "$appdir/usr/lib/$name"
    fi
  done
}
export -f collect; export appdir exclude
# Two passes so dependencies of newly copied libraries are picked up too.
for pass in 1 2; do
  find "$appdir/usr/lib" -maxdepth 1 -name '*.so*' -type f -print0 | xargs -0 -I{} bash -c 'collect "$1"' _ {}
  find "$appdir/usr/lib/vlc" -name '*.so*' -type f -print0 | xargs -0 -I{} bash -c 'collect "$1"' _ {}
done

target="$out_dir/Lumen-$version-linux-x86_64.AppImage"
ARCH=x86_64 VERSION="$version" appimagetool --no-appstream "$appdir" "$target"
chmod +x "$target"
echo "Built $target"

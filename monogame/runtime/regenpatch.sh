#!/bin/bash
# Regenerates monogame-v3.8.6.patch after a change to src/: recovers the upstream files by reverse-applying the
# committed patch to the committed sources (HEAD), then diffs them against the working tree.
#
#   monogame/runtime/regenpatch.sh
set -eu
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
files=(premake5.lua vulkan/MGG_Vulkan.cpp sdl/MGP_sdl.cpp include/api_enums.h faudio/MGA_faudio.cpp)
mkdir -p "$tmp/a/native/monogame/faudio" "$tmp/b/native/monogame/faudio" "$tmp/a/native/monogame/vulkan" "$tmp/b/native/monogame/vulkan" "$tmp/a/native/monogame/sdl" "$tmp/b/native/monogame/sdl" "$tmp/a/native/monogame/include" "$tmp/b/native/monogame/include"
for f in "${files[@]}"; do
  git -C "$root" show "HEAD:monogame/runtime/src/$f" > "$tmp/a/native/monogame/$f"
  cp "$here/src/$f" "$tmp/b/native/monogame/$f"
done
git -C "$root" show "HEAD:monogame/runtime/monogame-v3.8.6.patch" > "$tmp/committed.patch"
(cd "$tmp/a" && patch -s -R -p1 < "$tmp/committed.patch")
(cd "$tmp" && for f in "${files[@]}"; do diff -u --label "a/native/monogame/$f" --label "b/native/monogame/$f" "a/native/monogame/$f" "b/native/monogame/$f" || true; done) > "$here/monogame-v3.8.6.patch"
echo "wrote $here/monogame-v3.8.6.patch ($(wc -l < "$here/monogame-v3.8.6.patch") lines)"

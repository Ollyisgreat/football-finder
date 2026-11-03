#!/usr/bin/env bash
# Builds upload-ready plugin zips. Needs the .NET SDK (8, 9 and/or 10).
# Usage: bash build.sh            -> builds every Jellyfin version your SDKs allow
#        bash build.sh 10.11      -> just one
set -uo pipefail
cd "$(dirname "$0")"
mkdir -p dist
want="${1:-all}"
sdks="$(dotnet --list-sdks 2>/dev/null | cut -d. -f1 | sort -u | tr '\n' ' ')"
echo "Installed .NET SDK majors: ${sdks:-none}"

build() {
  local jf="$1" tfm="$2" abi="$3" nugetver="$4" need="$5" ver="$6"
  if [ "$want" != "all" ] && [ "$want" != "$jf" ]; then return 0; fi
  if ! echo " $sdks " | grep -q " $need "; then
    echo "!! Skipping Jellyfin $jf: needs .NET SDK $need"; return 0
  fi
  echo "==> Building for Jellyfin $jf ($tfm)"
  rm -rf bin obj "out/$jf"
  if ! dotnet publish XtreamFootball.csproj -c Release -p:PluginTfm="$tfm" -p:JellyfinVersion="$nugetver" -p:AssemblyVersion="$ver" -p:FileVersion="$ver" -o "out/$jf/bin"; then
    echo "!! Build for Jellyfin $jf FAILED"; FAILED="$FAILED $jf"; return 0
  fi
  local dir="out/$jf/XtreamFootball_$ver"
  mkdir -p "$dir" dist/repo
  cp "out/$jf/bin/XtreamFootball.dll" "$dir/"
  sed -e "s/__ABI__/$abi/" -e "s/\"version\": \"1.0.0.0\"/\"version\": \"$ver\"/" meta.json > "$dir/meta.json"
  # Manual-install zip (contains the folder)
  rm -f "dist/XtreamFootball-jellyfin-$jf.zip"
  (cd "out/$jf" && zip -qr "../../dist/XtreamFootball-jellyfin-$jf.zip" "XtreamFootball_$ver")
  # Catalogue zip (files at the root, as Jellyfin's installer expects)
  rm -f "dist/repo/xtreamfootball_$ver.zip"
  (cd "$dir" && zip -qj "../../../dist/repo/xtreamfootball_$ver.zip" XtreamFootball.dll meta.json)
  echo "$ver $abi xtreamfootball_$ver.zip" >> dist/repo/builds.txt
  echo "    -> dist/XtreamFootball-jellyfin-$jf.zip"
}

FAILED=""
rm -rf dist/repo
# Version numbers rise with the Jellyfin version so the catalogue offers each server the right build.
build 12    net10.0 12.0.0.0  "12.0.0"  10 1.2.1.0
build 10.11 net9.0  10.11.0.0 "10.11.0" 9  1.1.1.0
build 10.10 net8.0  10.10.0.0 "10.10.0" 8  1.0.1.0
rm -rf bin obj
ls -l dist
[ -z "$FAILED" ] || { echo "Failed:$FAILED"; exit 1; }

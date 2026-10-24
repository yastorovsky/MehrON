#!/usr/bin/env bash
# PattN: bundles the Aether core and Psiphon's server list into the bin folder of a release (design 2.4.3,
# critique 28), for package-zip.yml, package-debian.sh, package-rhel.sh, package-osx.sh and build-WebAPI.yml (the
# WebAPI's release zips and its container):
#
#   bin/aether/aether, lyrebird and psiphon-tunnel-core (.exe on Windows), side by side, from the latest
#     patterniha/Aether release, where the core finds both helpers beside itself (AetherBinaries);
#   beside them the license texts of the three programs: AGPL-3.0.txt for aether and GPL-3.0.txt for
#     psiphon-tunnel-core, which PattN carries in licenses/aether, and lyrebird-LICENSE.txt, lyrebird's own license
#     file, taken from lyrebird's source at the commit that the release's pt-build.sh builds lyrebird from;
#   bin/aether/LICENSES, the release archive's own folder of license files, when the archive has one;
#   bin/aether/NOTICE.txt, which names the three programs, their licenses, each of these files and their source code;
#   bin/psiphon_servers.dat, the signed server list Psiphon's client starts with, and bin/psiphon_servers.stamp, when
#     it was published (the Unix seconds of the download's Last-Modified header), which PattN copies into place at
#     start (PsiphonServerList.InstallBundled).
#
# Usage: package-aether.sh <os>-<arch> <bin folder>
#   <os>-<arch> as package-zip.yml names its builds: windows-64, windows-arm64 (it gets the x64 core, which Windows 11
#   on ARM runs), linux-64, linux-arm64, macos-64 or macos-arm64. Any other build (Windows x86, Linux riscv64 and
#   loong64) has no Aether archive: nothing is bundled there, and PattN offers no Aether core on such a system.
#
# The archive is checked against the .sha256 file the release publishes beside it, and the list against Psiphon's
# signature, with the key PattN carries (PsiphonServerList.SigningKey), and the rule of at least 50 server entries,
# as PattNG's fetch-psiphon-servers.sh checks the list it ships. A failed check, or a license text that cannot be
# fetched, fails the packaging, as it fails PattNG's build, so a broken or foreign download, or a program without its
# license, never ships.
#
# Needs bash, curl, python3 (or python) and openssl.
set -o errexit
set -o pipefail
set -o nounset

AETHER_REPO_URL="https://github.com/patterniha/Aether"
# The files of a release's source, read at the release's tag.
AETHER_RAW_URL="https://raw.githubusercontent.com/patterniha/Aether"
# Where pt-build.sh takes lyrebird's source from when the project's GitLab cannot be reached.
GO_PROXY_URL="https://proxy.golang.org"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Where the app keeps the address of the list and Psiphon's key, which the list is checked with here.
GLOBAL_CS="$SCRIPT_DIR/v2rayN/ServiceLib/Global.cs"
KEY_SOURCE="$SCRIPT_DIR/v2rayN/ServiceLib/Aether/PsiphonServerList.cs"
# The license texts PattN carries for aether (AGPL-3.0) and psiphon-tunnel-core (GPL-3.0).
LICENSE_SOURCE="$SCRIPT_DIR/licenses/aether"

# The release archive of the Aether core for a build, as CoreInfoManager names it for the check-update page: the
# static musl builds on Linux, which run whatever the C library. Fails for a build that has none.
aether_asset() {
  case "$1" in
    windows-64|windows-arm64) echo "aether-windows-x86_64.zip" ;;
    linux-64)                 echo "aether-linux-x86_64-musl.tar.gz" ;;
    linux-arm64)              echo "aether-linux-aarch64-musl.tar.gz" ;;
    macos-64)                 echo "aether-macos-x86_64.tar.gz" ;;
    macos-arm64)              echo "aether-macos-arm64.tar.gz" ;;
    *)                        return 1 ;;
  esac
}

# The first python that runs: on Windows, "python3" may be a store stub that only prints a hint.
find_python() {
  local candidate
  for candidate in python3 python; do
    if "$candidate" -c "import zlib" >/dev/null 2>&1; then
      echo "$candidate"
      return 0
    fi
  done
  echo "[aether] python not found" >&2
  return 1
}

# The tag of the latest release, read from the release redirect (the anonymous GitHub API is rate-limited on CI
# runners), so that the archive and its checksum are taken from the same release.
aether_latest_tag() {
  local url tag
  url="$(curl -fsSLI --retry 3 --retry-delay 5 -o /dev/null -w '%{url_effective}' "$AETHER_REPO_URL/releases/latest")" || return 1
  tag="${url##*/releases/tag/}"
  if [[ "$tag" == "$url" || ! "$tag" =~ ^v?[0-9][0-9A-Za-z.+-]*$ ]]; then
    echo "[aether] the latest release names no tag: $url" >&2
    return 1
  fi
  echo "$tag"
}

# Checks <archive>, the release file <asset>, against <sums> (sha256sum's "<hex>  <name>"), then writes the three
# programs out of it, from whatever folder of the archive they are in, side by side into <folder>, flat as
# AetherBinaries.Extract unpacks the archive the check-update page downloads, leaving out folders, links and macOS's
# AppleDouble files ("._name"). The archive's LICENSES folder, when it has one, is written as <folder>/LICENSES with
# the files and subfolders it holds, as Extract keeps it too. Unlike Extract, which writes every other file of the
# archive as well (run-aether.bat on Windows), it writes nothing else. Outside Windows the programs get their execute
# bits. Fails, writing nothing, when the checksum differs, a program is missing or twice in the archive, or a file of
# LICENSES is there twice or has a name no file can have on every system.
aether_unpack() {
  local archive="$1" sums="$2" asset="$3" folder="$4"
  local py
  py="$(find_python)" || return 1
  mkdir -p "$folder" || return 1
  "$py" - "$archive" "$sums" "$asset" "$folder" <<'EOF' || return 1
import hashlib, os, re, shutil, sys, tarfile, zipfile
archive, sums, asset, folder = sys.argv[1:5]
windows = asset.endswith(".zip")
wanted = [name + (".exe" if windows else "") for name in ("aether", "lyrebird", "psiphon-tunnel-core")]

fields = open(sums, encoding="utf-8", errors="replace").read().split()
if not fields or not re.fullmatch(r"[0-9A-Fa-f]{64}", fields[0]):
    sys.exit("[aether] %s holds no SHA-256 checksum" % os.path.basename(sums))
if len(fields) > 1 and fields[1].lstrip("*") != asset:
    sys.exit("[aether] %s is the checksum of %s, not of %s" % (os.path.basename(sums), fields[1].lstrip("*"), asset))
digest = hashlib.sha256()
with open(archive, "rb") as f:
    for block in iter(lambda: f.read(1 << 20), b""):
        digest.update(block)
if digest.hexdigest() != fields[0].lower():
    sys.exit("[aether] %s does not match the checksum the release publishes for it" % asset)

def flat(path):
    # The last part of the path, whether the archive separates the parts with '/' or, as a zip made by Windows
    # PowerShell can, with '\'; None for a folder or an AppleDouble file.
    name = path.replace("\\", "/").rsplit("/", 1)[-1]
    return None if name in ("", ".", "..") or name.startswith("._") else name

def license_path(path):
    # The path of an entry inside the archive's LICENSES folder, as "name/name"; "" for an entry outside that folder;
    # None for one that is left out: the folder itself, or an AppleDouble file (in __MACOSX or as "._name").
    names = [name for name in path.replace("\\", "/").split("/") if name not in ("", ".")]
    if "LICENSES" not in names:
        return ""
    at = names.index("LICENSES")
    rest = names[at + 1:]
    if not rest or "__MACOSX" in names[:at] or rest[-1].startswith("._"):
        return None
    for name in rest:
        if name == ".." or re.search(r'[<>:"/\\|?*\x00-\x1f]', name) or name.endswith((" ", ".")):
            sys.exit("[aether] %s holds a file in LICENSES whose name no file can have: %s" % (asset, path))
    return "/".join(rest)

found = {}
licenses = {}
def take(path, read):
    inside = license_path(path)
    if inside is None:
        return
    if inside:
        # A license file, kept in its folder; never taken for a program of the same name.
        if inside.lower() in (key.lower() for key in licenses):
            sys.exit("[aether] %s holds LICENSES/%s twice" % (asset, inside))
        licenses[inside] = read()
        return
    name = flat(path)
    if name in wanted:
        if name in found:
            sys.exit("[aether] %s holds %s twice" % (asset, name))
        found[name] = read()

try:
    if windows:
        with zipfile.ZipFile(archive) as z:
            for info in z.infolist():
                if not info.is_dir():
                    take(info.filename, lambda: z.read(info))
    else:
        with tarfile.open(archive, "r:gz") as tar:
            for member in tar.getmembers():
                if member.isreg():
                    take(member.name, lambda: tar.extractfile(member).read())
except (tarfile.TarError, zipfile.BadZipFile, OSError, EOFError) as e:
    sys.exit("[aether] %s cannot be read: %s" % (asset, e))
missing = [name for name in wanted if name not in found]
if missing:
    sys.exit("[aether] %s lacks %s" % (asset, ", ".join(missing)))
keys = sorted(licenses)
for key in keys:
    if any(other.startswith(key + "/") for other in keys):
        sys.exit("[aether] %s holds LICENSES/%s both as a file and as a folder" % (asset, key))
for name in wanted:
    path = os.path.join(folder, name)
    with open(path, "wb") as f:
        f.write(found[name])
    if not windows:
        os.chmod(path, 0o755)
    print("[aether] %s (%d bytes)" % (path, len(found[name])))
if licenses:
    target = os.path.join(folder, "LICENSES")
    shutil.rmtree(target, ignore_errors=True)
    for key in keys:
        path = os.path.join(target, *key.split("/"))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as f:
            f.write(licenses[key])
    print("[aether] %s: %d license files from the archive" % (target, len(keys)))
else:
    print("[aether] %s has no LICENSES folder" % asset)
EOF
}

# Copies the license texts PattN carries, AGPL-3.0.txt (aether) and GPL-3.0.txt (psiphon-tunnel-core), into <folder>.
aether_license_texts() {
  local folder="$1" name
  for name in AGPL-3.0.txt GPL-3.0.txt; do
    if [[ ! -s "$LICENSE_SOURCE/$name" ]]; then
      echo "[aether] $LICENSE_SOURCE/$name is missing" >&2
      return 1
    fi
    cp "$LICENSE_SOURCE/$name" "$folder/$name" || return 1
  done
  echo "[aether] AGPL-3.0.txt and GPL-3.0.txt from $LICENSE_SOURCE"
}

# Writes the license text taken from <source> into <out>: <source> is the file itself, or a zip with <member> in it
# (a Go module zip). The text must be a BSD license, with a copyright line, the conditions and the disclaimer, so that
# an error page or a challenge page never ships as lyrebird's license. Fails, writing nothing, otherwise.
lyrebird_take() {
  local source="$1" member="$2" out="$3"
  local py
  py="$(find_python)" || return 1
  "$py" - "$source" "$member" "$out" <<'EOF'
import sys, zipfile
source, member, out = sys.argv[1:4]
try:
    if member:
        with zipfile.ZipFile(source) as z:
            data = z.read(member)
    else:
        data = open(source, "rb").read()
except (KeyError, OSError, zipfile.BadZipFile) as e:
    sys.exit("[aether] lyrebird's license cannot be read: %s" % e)
try:
    text = data.decode("utf-8")
except UnicodeDecodeError:
    sys.exit("[aether] lyrebird's license is not text")
lower = text.lower()
if not 200 <= len(data) <= 256 * 1024 or "<html" in lower or "<!doctype" in lower:
    sys.exit("[aether] what was fetched as lyrebird's license is not a license text")
# A BSD license: the copyright line, the conditions and the disclaimer.
if "copyright" not in lower or "redistribution and use in source and binary forms" not in lower \
        or "this software is provided by" not in lower or "warranties" not in lower:
    sys.exit("[aether] what was fetched as lyrebird's license is not lyrebird's BSD license")
with open(out, "wb") as f:
    f.write(data)
EOF
}

# The Go module proxy's form of a module path or version: each capital letter as '!' and the small one.
go_proxy_escape() {
  local text="$1" escaped="" char i
  for ((i = 0; i < ${#text}; i++)); do
    char="${text:i:1}"
    if [[ "$char" == [[:upper:]] ]]; then
      escaped+="!$(printf '%s' "$char" | tr '[:upper:]' '[:lower:]')"
    else
      escaped+="$char"
    fi
  done
  printf '%s' "$escaped"
}

# Fetches lyrebird's license file from lyrebird's source at the commit that the release <tag>'s pt-build.sh builds
# lyrebird from (its module, commit and Go module version), into <folder>/lyrebird-LICENSE.txt, using <work> for the
# downloads. As pt-build.sh takes the source, it is read from the project's GitLab first, and from the Go module proxy
# at the pinned version when GitLab does not give it. Sets LYREBIRD_MODULE and LYREBIRD_COMMIT for the notice. Fails,
# writing nothing, when pt-build.sh pins no commit, or neither place gives the license.
lyrebird_license() {
  local folder="$1" tag="$2" work="$3"
  local recipe="$work/pt-build.sh" module commit version
  if ! curl -fsSL --retry 3 --retry-delay 5 -o "$recipe" "$AETHER_RAW_URL/refs/tags/$tag/pt-build.sh"; then
    echo "[aether] pt-build.sh of $tag cannot be fetched" >&2
    return 1
  fi
  module="$(sed -n 's/^module="\([^"]*\)"[[:space:]]*$/\1/p' "$recipe" | head -n 1)"
  commit="$(sed -n 's/^commit="\([^"]*\)"[[:space:]]*$/\1/p' "$recipe" | head -n 1)"
  version="$(sed -n 's/^version="\([^"]*\)"[[:space:]]*$/\1/p' "$recipe" | head -n 1)"
  if [[ ! "$module" =~ ^[a-z0-9.-]+(/[A-Za-z0-9._~-]+)+$ || ! "$commit" =~ ^[0-9a-f]{7,40}$ \
    || ! "$version" =~ ^v[0-9][0-9A-Za-z.+-]*$ ]]; then
    echo "[aether] pt-build.sh of $tag names no lyrebird module, commit and version this script can read" >&2
    return 1
  fi
  echo "[aether] lyrebird's license at $commit ($module $version, pinned by pt-build.sh of $tag)"
  local out="$folder/lyrebird-LICENSE.txt"
  if [[ "$module" == gitlab.* ]] \
    && curl -fsSL --retry 3 --retry-delay 5 --max-time 60 -o "$work/lyrebird-LICENSE" "https://$module/-/raw/$commit/LICENSE" \
    && lyrebird_take "$work/lyrebird-LICENSE" "" "$out"; then
    echo "[aether] lyrebird-LICENSE.txt from https://$module/-/raw/$commit/LICENSE"
  else
    echo "[aether] lyrebird's GitLab gave no license at $commit; trying the Go module proxy" >&2
    local url
    url="$GO_PROXY_URL/$(go_proxy_escape "$module")/@v/$(go_proxy_escape "$version").zip"
    if ! { curl -fsSL --retry 3 --retry-delay 5 --max-time 180 -o "$work/lyrebird.zip" "$url" \
      && lyrebird_take "$work/lyrebird.zip" "$module@$version/LICENSE" "$out"; }; then
      echo "[aether] lyrebird's license could not be fetched" >&2
      return 1
    fi
    echo "[aether] lyrebird-LICENSE.txt from $url"
  fi
  LYREBIRD_MODULE="$module"
  LYREBIRD_COMMIT="$commit"
}

# The notice beside the programs (critique 28): what they are, their licenses, the files in <folder> that hold the
# license texts, and their source code, at the release <tag> they come from, whose archive is <asset>; lyrebird's
# source is <module> at <commit>. The Help menu's "Aether core licenses" opens it (AetherBinaries.NoticeName).
aether_notice() {
  local folder="$1" tag="$2" asset="$3" module="$4" commit="$5"
  local archive_licenses=""
  if [[ -d "$folder/LICENSES" ]]; then
    archive_licenses="$(cd "$folder/LICENSES" && find . -type f | sed 's|^\./|  LICENSES/|' | LC_ALL=C sort)" || return 1
  fi
  {
    cat <<EOF
Programs in this folder
=======================

PattN ships these three programs from the patterniha/Aether release ${tag} and runs
them as programs of their own. Each is under its own license, given below with
the file in this folder that holds its text and where its source code is.

aether (aether.exe on Windows)
  The Aether WARP client by CluvexStudio, as forked by patterniha.
  License: GNU Affero General Public License, version 3 only (AGPL-3.0-only).
  License text: AGPL-3.0.txt
  Source code: https://github.com/patterniha/Aether
  This release: https://github.com/patterniha/Aether/tree/${tag}
  Upstream: https://github.com/CluvexStudio/Aether

psiphon-tunnel-core (psiphon-tunnel-core.exe on Windows)
  Psiphon's console client, which aether runs for profiles that use Psiphon.
  License: GNU General Public License, version 3 (GPL-3.0).
  License text: GPL-3.0.txt
  Source code: https://github.com/Psiphon-Labs/psiphon-tunnel-core
  The repository and commit this build comes from are named in
  https://github.com/patterniha/Aether/blob/${tag}/psiphon-build.sh

lyrebird (lyrebird.exe on Windows)
  The Tor Project's pluggable transport, which aether runs for Tor bridges.
  License: a BSD license, as lyrebird's own license file states it.
  License text: lyrebird-LICENSE.txt, the LICENSE file of lyrebird's source at
  commit ${commit}, which this build comes from.
  Source code: https://${module}
  The commit this build comes from is named in
  https://github.com/patterniha/Aether/blob/${tag}/pt-build.sh
EOF
    if [[ -n "$archive_licenses" ]]; then
      cat <<EOF

LICENSES (a folder)
  The license files of the release archive ${asset},
  as the archive carries them beside aether:
${archive_licenses}
EOF
    fi
  } > "$folder/NOTICE.txt" || return 1
}

# Downloads the archive of <os>-<arch> and its checksum from the latest release into <work>, unpacks it into
# <bin>/aether, and puts the license texts and the notice beside the programs.
aether_bundle() {
  local platform="$1" bin="$2" work="$3"
  local asset tag
  asset="$(aether_asset "$platform")" || return 1
  tag="$(aether_latest_tag)" || return 1
  echo "[aether] $asset of $tag"
  curl -fsSL --retry 3 --retry-delay 5 -o "$work/$asset" "$AETHER_REPO_URL/releases/download/$tag/$asset" || return 1
  curl -fsSL --retry 3 --retry-delay 5 -o "$work/$asset.sha256" "$AETHER_REPO_URL/releases/download/$tag/$asset.sha256" || return 1
  aether_unpack "$work/$asset" "$work/$asset.sha256" "$asset" "$bin/aether" || return 1
  aether_license_texts "$bin/aether" || return 1
  lyrebird_license "$bin/aether" "$tag" "$work" || return 1
  aether_notice "$bin/aether" "$tag" "$asset" "$LYREBIRD_MODULE" "$LYREBIRD_COMMIT" || return 1
}

# Checks Psiphon's server list <list>, downloaded with the response headers <headers>, against the key written in
# <key source> (PsiphonServerList.SigningKey, a C# string in parts), and puts it at <bin>/psiphon_servers.dat, with
# the time it was published in <bin>/psiphon_servers.stamp. The port of PattNG's fetch-psiphon-servers.sh: the package
# is unpacked for the check (zlib, then JSON with the entry text, the digest of the key and the signature); it must
# name the key PattN carries by the digest of its text as written, hold at least 50 server entries
# (PsiphonServerList.MinimumEntries), and carry a signature that checks out (RSA PKCS#1 v1.5 with SHA-256 over the
# entry text, as the client checks it). Fails, staging nothing, when a check fails.
psiphon_stage() {
  local list="$1" headers="$2" key_source="$3" bin="$4"
  local py check
  py="$(find_python)" || return 1
  check="$(mktemp -d)" || return 1
  if ! "$py" - "$list" "$headers" "$key_source" "$check" <<'EOF'
import base64, binascii, email.utils, hashlib, io, json, re, sys, time, zlib
listfile, headers, key_source, check = sys.argv[1:5]
published = None
for line in io.open(headers, encoding="latin-1"):
    if line.lower().startswith("last-modified:"):
        parsed = email.utils.parsedate_tz(line.split(":", 1)[1].strip())
        if parsed:
            published = email.utils.mktime_tz(parsed)
if published is None:
    published = int(time.time())
    print("[psiphon-servers] no Last-Modified header; the fetch time stands for the publication time")
print("[psiphon-servers] published %s" % time.strftime("%Y-%m-%d %H:%M:%S UTC", time.gmtime(published)))
source = io.open(key_source, encoding="utf-8").read()
found = re.search(r"public const string SigningKey\s*=(.*?);", source, re.S)
if not found:
    sys.exit("[psiphon-servers] the signing key was not found in " + key_source)
key = "".join(re.findall(r'"([^"]*)"', found.group(1)))
try:
    package = json.loads(zlib.decompress(open(listfile, "rb").read()))
except zlib.error:
    sys.exit("[psiphon-servers] the list is not compressed the way Psiphon writes it")
except ValueError:
    sys.exit("[psiphon-servers] the list is not the package Psiphon writes")
if not isinstance(package, dict):
    sys.exit("[psiphon-servers] the list is not the package Psiphon writes")
for field in ("data", "signingPublicKeyDigest", "signature"):
    if not isinstance(package.get(field), str):
        sys.exit("[psiphon-servers] the list has no %s" % field)
try:
    named = base64.b64decode(package["signingPublicKeyDigest"])
    signature = base64.b64decode(package["signature"])
except binascii.Error:
    sys.exit("[psiphon-servers] the list is not encoded the way Psiphon writes it")
if named != hashlib.sha256(key.encode("ascii")).digest():
    sys.exit("[psiphon-servers] the list is signed with another key than the one PattN carries")
data = package["data"]
entries = [line for line in data.split("\n") if line.strip()]
if len(entries) < 50:
    sys.exit("[psiphon-servers] only %d server entries in the list; that is not the list" % len(entries))
io.open(check + "/stamp", "w", newline="\n").write(str(published) + "\n")
open(check + "/data.bin", "wb").write(data.encode("utf-8"))
open(check + "/sig.bin", "wb").write(signature)
pem = "-----BEGIN PUBLIC KEY-----\n" + "\n".join(key[i:i + 64] for i in range(0, len(key), 64)) + "\n-----END PUBLIC KEY-----\n"
io.open(check + "/key.pem", "w", newline="\n").write(pem)
print("[psiphon-servers] %d server entries" % len(entries))
EOF
  then
    rm -rf "$check"
    return 1
  fi
  # The signature: RSA PKCS#1 v1.5 with SHA-256 over the entry text, as the client checks it.
  if ! openssl dgst -sha256 -verify "$check/key.pem" -signature "$check/sig.bin" "$check/data.bin"; then
    echo "[psiphon-servers] the signature of the list does not check out" >&2
    rm -rf "$check"
    return 1
  fi
  if ! { mkdir -p "$bin" && cp "$list" "$bin/psiphon_servers.dat" && cp "$check/stamp" "$bin/psiphon_servers.stamp"; }; then
    rm -rf "$check"
    return 1
  fi
  rm -rf "$check"
  echo "[psiphon-servers] staged $bin/psiphon_servers.dat"
}

# Downloads Psiphon's server list from the address the app uses (Global.PsiphonServersUrl) into <work> and stages it
# into <bin>.
psiphon_bundle() {
  local bin="$1" work="$2"
  local url
  url="$(grep -o 'PsiphonServersUrl = "[^"]*"' "$GLOBAL_CS" | cut -d'"' -f2)" || true
  if [[ -z "$url" ]]; then
    echo "[psiphon-servers] PsiphonServersUrl not found in $GLOBAL_CS" >&2
    return 1
  fi
  echo "[psiphon-servers] fetching $url"
  curl -fsSL --retry 3 --retry-delay 5 --max-time 180 -D "$work/psiphon.headers" -o "$work/psiphon.list" "$url" || return 1
  psiphon_stage "$work/psiphon.list" "$work/psiphon.headers" "$KEY_SOURCE" "$bin" || return 1
}

main() {
  if [[ $# -ne 2 ]]; then
    echo "usage: $0 <os>-<arch> <bin folder>" >&2
    return 2
  fi
  local platform="$1" bin="$2"
  if ! aether_asset "$platform" >/dev/null; then
    echo "[aether] patterniha/Aether publishes no archive for '$platform': neither the Aether core nor Psiphon's server list is bundled"
    return 0
  fi
  WORK="$(mktemp -d)"
  trap 'rm -rf "$WORK"' EXIT
  aether_bundle "$platform" "$bin" "$WORK"
  psiphon_bundle "$bin" "$WORK"
}

# Runs unless sourced: the packaging runs it, and a dry run sources it for its functions.
if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi

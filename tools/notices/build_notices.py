"""Builds THIRD-PARTY-NOTICES.txt in the repo root (plan 27).

From the repo root:

    py tools/notices/build_notices.py --godot "<path to the Godot console exe>"

What goes in:
    Godot Engine    licence, copyright list and licence texts from the engine itself
                    (tools/notices/godot_licenses.gd, run headless in a temporary project)
    .NET runtime    the MIT licence of the .NET Foundation (text below)
    NuGet packages  every <PackageReference> under src/, with the LICENSE file from the
                    local NuGet cache (~/.nuget/packages/<id>/<version>/)

Python 3.9+, standard library only. Run it again after adding a package or updating Godot;
ThirdPartyNoticesTests fails when a package is missing.
"""

import argparse
import os
import re
import subprocess
import sys
import tempfile

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(REPO, "THIRD-PARTY-NOTICES.txt")
RULE = "=" * 72

# Copied on 2026-11-23 from LICENSE.TXT of a .NET Foundation NuGet package on this PC
# (system.reflection.metadata 1.6.0). The runtime pack that a Windows export bundles carries its own
# LICENSE.TXT and THIRD-PARTY-NOTICES.TXT; plan 32 (Windows export) puts those next to the exe.
DOTNET_MIT = """The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
"""

HEADER = """FactorySim - third-party notices

FactorySim uses the software below. Each part keeps its own licence; the texts follow.
FactorySim itself contains no code or assets of Factory I/O or CODESYS.
CODESYS is a trademark of CODESYS GmbH. Factory I/O is a product of Real Games.
FactorySim is not affiliated with, endorsed by or connected to CODESYS GmbH or Real Games.

Contents
  1. Godot Engine {godot} (with the components it bundles, including Jolt Physics)
  2. .NET runtime
  3. NuGet packages: {packages}
"""


def read(path):
    with open(path, encoding="utf-8") as handle:
        return handle.read()


def godot_version():
    project = read(os.path.join(REPO, "src", "FactorySim.App", "FactorySim.App.csproj"))
    match = re.search(r"Godot\.NET\.Sdk/([0-9.]+)", project)
    if not match:
        sys.exit("Godot.NET.Sdk version not found in FactorySim.App.csproj")
    return match.group(1)


def packages():
    found = []
    for root, _dirs, files in os.walk(os.path.join(REPO, "src")):
        if os.sep + "obj" in root or os.sep + ".godot" in root:
            continue
        for name in files:
            if name.endswith(".csproj"):
                text = read(os.path.join(root, name))
                for match in re.finditer(r'<PackageReference\s+Include="([^"]+)"\s+Version="([^"]+)"', text):
                    if match.groups() not in found:
                        found.append(match.groups())
    return sorted(found)


def package_licence(package_id, version):
    folder = os.path.join(os.path.expanduser("~"), ".nuget", "packages", package_id.lower(), version)
    for name in ("LICENSE.txt", "LICENSE.TXT", "LICENSE.md", "LICENSE", "license.txt"):
        path = os.path.join(folder, name)
        if os.path.isfile(path):
            return read(path)
    sys.exit(f"No licence file for {package_id} {version} in {folder}; run 'dotnet restore' first")


def godot_notices(godot):
    with tempfile.TemporaryDirectory() as temp:
        with open(os.path.join(temp, "project.godot"), "w", encoding="utf-8") as handle:
            handle.write('config_version=5\n[application]\nconfig/name="notices"\n')
        out = os.path.join(temp, "godot_licenses.txt")
        script = os.path.join(REPO, "tools", "notices", "godot_licenses.gd")
        subprocess.run([godot, "--headless", "--path", temp, "--script", script, "--", out],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=120)
        return read(out)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--godot", required=True, help="path to the Godot console exe")
    args = parser.parse_args()

    godot = godot_version()
    refs = packages()
    parts = [HEADER.format(godot=godot, packages=", ".join(f"{i} {v}" for i, v in refs) or "(none)")]
    parts.append(f"{RULE}\n1. Godot Engine {godot}\n   https://godotengine.org\n{RULE}\n\n{godot_notices(args.godot).rstrip()}\n")
    parts.append(f"{RULE}\n2. .NET runtime\n   https://github.com/dotnet/runtime\n{RULE}\n\n{DOTNET_MIT}")
    for index, (package_id, version) in enumerate(refs, start=1):
        parts.append(f"{RULE}\n3.{index} {package_id} {version} (NuGet)\n{RULE}\n\n{package_licence(package_id, version).rstrip()}\n")

    with open(OUT, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n\n".join(parts))
    print(f"wrote {OUT} ({godot}, {len(refs)} package(s))")


if __name__ == "__main__":
    main()

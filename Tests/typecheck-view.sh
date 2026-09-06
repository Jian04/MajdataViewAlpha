#!/bin/bash
# Type-checks the View's scripts the way Unity would, without opening Unity.
#
# Unity generates Assembly-CSharp.csproj with Windows paths, so on any other machine
# it resolves nothing, compiles nothing and reports success. Every Unity-side type
# error then had to be found by building a player, which is slow and easy to skip.
# This compiles the same source set against a real set of Unity assemblies taken from
# an already built player, so a wrong argument type is caught here instead.
#
# Usage: Tests/typecheck-view.sh [path to a player's *_Data/Managed folder]
set -u

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root" || exit 1

managed="${1:-$HOME/Desktop/MajdataViewAlpha/App/MajdataView/MajdataView_Data/Managed}"
if [ ! -f "$managed/UnityEngine.CoreModule.dll" ]; then
    echo "No Unity assemblies at: $managed"
    echo "Point this script at the Managed folder of any built MajdataView player."
    exit 2
fi

bincore="$(ls -d "$HOME"/.dotnet/sdk/*/Roslyn/bincore 2>/dev/null | tail -1)"
if [ -z "$bincore" ]; then
    echo "Roslyn not found under ~/.dotnet/sdk/*/Roslyn/bincore."
    exit 2
fi

# The same set Unity puts in Assembly-CSharp: everything under Assets that is not in
# an Editor folder. There are no asmdefs in this project, so there is nothing else.
sources="$(find Assets -name '*.cs' -not -path '*/Editor/*' -not -name '._*' | sort)"

# Assembly-CSharp.dll is the output of these very sources; referencing it would make
# every type in it ambiguous.
refs="$(ls "$managed"/*.dll | grep -v '/Assembly-CSharp.dll$' | sed 's/^/-r:/')"

log="$(mktemp)"
trap 'rm -f "$log"' EXIT

# No UNITY_EDITOR: a player build skips those blocks, and the editor assemblies are
# not shipped in a player anyway.
# shellcheck disable=SC2086
"$HOME/.dotnet/dotnet" "$bincore/csc.dll" \
    -nologo -noconfig -nostdlib+ -target:library -langversion:9 \
    -define:UNITY_2021_3_OR_NEWER \
    -out:/dev/null \
    $refs \
    $sources >"$log" 2>&1

grep -E "error CS" "$log" | sort -u
errors=$(grep -cE "error CS" "$log")
echo "---"
echo "$(echo "$sources" | wc -l | tr -d ' ') files, $errors error(s)"
[ "$errors" -eq 0 ] || exit 1

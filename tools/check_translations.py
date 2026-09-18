"""Cross-references every Tr("...") call in the C# source against Assets/Localization/strings.csv,
and reports any key the code actually uses that the CSV doesn't have a row for.

    python tools/check_translations.py

Deliberately NOT a `tools/build_assets.py` generator (see that file's own docstring for why: its
GENERATORS list is for deterministic content regenerated from a recipe -- same inputs, byte-identical
outputs). A translation's English text is hand-authored prose, not derivable from anything else in
the repo, so there's nothing here to regenerate. This is a linter: it can tell you a key is *missing*,
it can never write the English for you.

See docs/localization.md for the convention this checks: the CSV's key column is the ORIGINAL SPANISH
text as it already appears in the source (gettext-style), not an invented identifier -- so a "missing
key" here means "this exact Spanish string, typed exactly as the C# has it, needs an English row."
"""

import csv
import io
import os
import re
import sys

# Windows' console defaults to cp1252, which can't encode the Spanish keys this script prints
# (accents, ¡¿, etc.) -- force UTF-8 on stdout so a genuine missing-key report doesn't itself crash.
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(TOOLS_DIR)
SCRIPTS_DIR = os.path.join(REPO_ROOT, "Scripts")
CSV_PATH = os.path.join(REPO_ROOT, "Assets", "Localization", "strings.csv")

# Matches Tr("...") and TranslationServer.Translate("...") call sites. Doesn't try to parse full C#
# syntax -- just enough to pull out the string literal argument, same "good enough for this repo's
# actual patterns" spirit as the rest of tools/.
TR_CALL = re.compile(r'(?:\bTr|TranslationServer\.Translate)\(\s*"((?:[^"\\]|\\.)*)"\s*\)')

# The regex above captures the literal C# SOURCE text between the quotes, escape sequences and all
# (e.g. \n as the two characters backslash-n). The CSV key is the actual runtime string (a real
# newline), so these need unescaping before comparing -- otherwise every multi-line/quoted key would
# incorrectly show up as "missing" just because of how it's spelled in the .cs file.
def unescape_csharp_string(s: str) -> str:
    return (
        s.replace('\\n', '\n')
        .replace('\\t', '\t')
        .replace('\\"', '"')
        .replace('\\\\', '\\')
    )


def find_used_keys() -> set[str]:
    used = set()
    for root, _dirs, files in os.walk(SCRIPTS_DIR):
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(root, name)
            with open(path, encoding="utf-8") as f:
                text = f.read()
            for match in TR_CALL.finditer(text):
                used.add(unescape_csharp_string(match.group(1)))
    return used


def load_csv_keys() -> set[str]:
    if not os.path.exists(CSV_PATH):
        print(f"No CSV found at {CSV_PATH}")
        return set()
    with open(CSV_PATH, encoding="utf-8", newline="") as f:
        rows = list(csv.reader(f))
    return {row[0] for row in rows[1:] if row}


def main() -> int:
    used_keys = find_used_keys()
    csv_keys = load_csv_keys()

    missing = sorted(used_keys - csv_keys)
    if missing:
        print(f"{len(missing)} key(s) used in code but missing from {os.path.relpath(CSV_PATH, REPO_ROOT)}:")
        for key in missing:
            print(f"  - {key!r}")
        return 1

    print(f"OK -- {len(used_keys)} Tr()/Translate() call site(s) checked, all present in the CSV "
          f"({len(csv_keys)} rows total).")
    return 0


if __name__ == "__main__":
    sys.exit(main())

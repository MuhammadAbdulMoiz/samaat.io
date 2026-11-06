"""Turns the Cobertura files from `dotnet test --collect "XPlat Code Coverage"` into a Markdown table.

Usage: python coverage-summary.py <results-dir> <output.md>. One row per assembly, with a line counted as covered when
any test project hit it, plus the total.
"""
import glob
import os
import sys
import xml.etree.ElementTree as ET


def main(results: str, output: str) -> None:
    # Per assembly, every (file, line) with whether any test project hit it. Compiler-generated classes (async state
    # machines, lambdas) list the same source lines again, so lines are keyed by file and number, not counted per class.
    hits: dict[str, dict[tuple[str, str], bool]] = {}
    for path in glob.glob(os.path.join(results, "**", "coverage.cobertura.xml"), recursive=True):
        for package in ET.parse(path).getroot().iter("package"):
            seen = hits.setdefault(package.get("name", "?"), {})
            for cls in package.iter("class"):
                for line in cls.iter("line"):
                    key = (cls.get("filename", ""), line.get("number", ""))
                    seen[key] = seen.get(key, False) or int(line.get("hits", "0")) > 0
    packages = {name: (sum(seen.values()), len(seen)) for name, seen in hits.items() if seen}

    rows = ["| Assembly | Lines covered | Line coverage |", "|---|---:|---:|"]
    total_covered = total_lines = 0
    for name, (covered, lines) in sorted(packages.items()):
        rows.append(f"| {name} | {covered} / {lines} | {covered / lines:.1%} |")
        total_covered += covered
        total_lines += lines
    if total_lines:
        rows.append(f"| **Total** | **{total_covered} / {total_lines}** | **{total_covered / total_lines:.1%}** |")
    else:
        rows.append("| (no coverage files found) | | |")

    text = "## Line coverage\n\n" + "\n".join(rows) + "\n"
    with open(output, "w", encoding="utf-8") as f:
        f.write(text)
    print(text)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])

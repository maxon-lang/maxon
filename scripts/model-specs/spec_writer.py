
from __future__ import annotations

from dataclasses import dataclass

FENCE = "```"


@dataclass
class SpecCase:
    name: str
    prose: str
    source: str
    error_text: str
    unsupported: tuple = ()


def case_text(case):
    lines = [f"<!-- test: {case.name} -->"]
    if case.unsupported:
        lines.append(f"<!-- unsupported-targets: {', '.join(case.unsupported)} -->")
    lines += [case.prose, "", f"{FENCE}maxon", case.source.rstrip("\n"), FENCE]
    if case.error_text:
        lines += [f"{FENCE}maxoncstderr", case.error_text.rstrip("\n"), FENCE]
    else:
        lines += [f"{FENCE}exitcode", "0", FENCE]
    return "\n".join(lines) + "\n"


def spec_text(feature, title, keywords, documentation, cases):
    head = [
        "---",
        f"feature: {feature}",
        "status: experimental",
        f"keywords: [{', '.join(keywords)}]",
        "category: semantics",
        "---",
        "",
        f"# {title}",
        "",
        "## Documentation",
        "",
        documentation.strip("\n"),
        "",
        "## Tests",
        "",
    ]
    return "\n".join(head) + "\n" + "\n".join(case_text(c) for c in cases)

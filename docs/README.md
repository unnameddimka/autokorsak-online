# Documentation index

## Living documentation (describes the current state; kept up to date)

| Document | Purpose |
|---|---|
| [../README.md](../README.md) | What the product is, how to run it, tests, demo |
| [../CLAUDE.md](../CLAUDE.md) | Working rules for agents and contributors |
| [architecture.md](architecture.md) | Components, boundaries, persistence, XML import/export, invariants |
| [decisions.md](decisions.md) | Confirmed product/architecture decisions and open questions |
| [status.md](status.md) | What works, test results, known limitations, next steps |
| [development-workflow.md](development-workflow.md) | Spec → implementation → review → demo cycle |

## Historical specification series (immutable once approved)

| Spec | Title | Status |
|---|---|---|
| [devel-specs/0000-ui-prototype.md](devel-specs/0000-ui-prototype.md) | Functional UI prototype | Approved 2026-10-16; implemented (see [status.md](status.md)) |

New scope and changed requirements go into new numbered specs (`0001-…`). Approved specs are never rewritten.

## Static reference evidence (do not modify)

- [reference/legacy-ui/](reference/legacy-ui/README.md): 15 screenshots of AutoKorsak 1.38. They are functional
  references, not design targets.
- `../legacy-autokorsak/`: legacy XML/TXT files and the original executable. `А_11.12.21.xml` (Cyrillic А) is
  the primary acceptance fixture.

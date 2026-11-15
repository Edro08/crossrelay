# Agent Instructions

Read the applicable specifications in `docs/` before modifying code:
- `docs/specs/spec_protocol.md`: network protocol specification (TLS 1.3, P2P `/api/v1/*`, local IPC `/local/v1/*`, streaming and mandatory implementation rules).
- `docs/mockups/ui_mockup.html`: official interactive UI mockup and design specification.

## Context and Progress Tracking

Agents may use the `agents/context/` directory to deposit and maintain context `.md` files for task tracking, reporting, and knowledge retention:
- `history.md`: chronicle of project milestones, phases, and snapshots. Must start with `# Historial de [project_name]` and list phases/milestones in reverse chronological order (most recent first / cortes en orden cronológico descendente).
- `phases.md` / `fases.md`: implementation phases, roadmaps, and step-by-step checklists.
- `reports.md` / `reportes.md`: analysis reports, technical audits, and benchmark summaries.
- Additional markdown files as needed to retain execution context across sessions.

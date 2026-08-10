---
name: release-docs
description: Finalize repository documentation for an upcoming release. Use when preparing release notes, marking a version released, or synchronizing AGENTS.md, README.md, CHANGELOG.MD, and ROADMAP.md with completed functionality while minimizing persistent instruction tokens.
---

# Release Docs

1. Determine the upcoming version from the request or repository version metadata. If
   multiple versions are plausible, ask instead of inventing one.
2. Inspect the complete release diff and current public behavior.
3. Find every applicable `AGENTS.md` and `AGENTS.override.md`. Update instructions for
   new commands, architecture, or conventions. Remove stale and duplicated prose; point
   to skills and harness tools instead of embedding long procedures. Preserve scoped
   overrides. Optimize for minimum tokens without losing actionable constraints.
4. Update `README.md` so setup, usage, guarantees, and examples match released behavior.
5. Update `CHANGELOG.MD`: move upcoming entries into `## <version> - YYYY-MM-DD`, using
   the actual execution date. Treat that version as released that day. Keep entries
   user-visible, factual, and grouped by change type; create a fresh empty `Unreleased`
   section for later work.
6. Remove every completed item from `ROADMAP.md`. Remove milestones that have no planned
   items left; never copy completed work back into the roadmap.
7. Check links, versions, dates, and terminology across all four documentation surfaces.
   Run the harness when docs affect executable examples or configuration.
8. Summarize the released version and documentation changes. Do not create a tag,
   commit, package, or external release unless explicitly requested.

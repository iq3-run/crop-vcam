---
name: style-agent-memory-path-in-comments
description: Shipped C#/C++ comments in this repo have started citing this agent-memory directory by file path — flag as a rotting external reference, same as an issue-number comment
metadata:
  type: project
---

In the `feat/input-size-output-and-preview` PR (2026-08-15), `SharedFrameProtocol.cs` and `SharedFrameProtocol.h` both added a comment sentence like "see finding_shared_region_size_mismatch in .claude/agent-memory/code-reviewer for what happens when the two drift" pointing at this very code-reviewer memory directory.

**Why:** CLAUDE.md's comment policy says never to reference the current task, a caller, or an issue number in comments, because that context belongs in the PR description and rots as the codebase evolves. A pointer to an AI agent's memory file is the same failure mode — the memory system's own instructions say to prune entries once they stop being true, and a human contributor without agent tooling has no way to resolve the reference at all. The underlying WHY (region size fixed at creation, C#/C++ constants must match exactly) is genuinely worth keeping in the comment; only the specific file-path pointer is the problem.

**How to apply:** If a future PR adds another comment that names an `.claude/agent-memory/...` path, flag it as a Warning (rots-as-code-evolves reference) and suggest keeping the WHY inline instead of pointing at the memory file. Do not flag the underlying practice of writing a multi-line WHY comment about the size-sync invariant itself — that part is fine, see [[style-multiline-why-comments]] and [[finding-shared-region-size-mismatch]].

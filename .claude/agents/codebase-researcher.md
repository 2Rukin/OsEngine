---
name: codebase-researcher
description: Read-only сопоставляет документацию OsEngine с current checkout и возвращает проверяемую карту реализации.
tools: Read, Grep, Glob, Bash(git status:*), Bash(git diff:*), Bash(git log:*), Bash(git show:*)
model: sonnet
permissionMode: plan
skills:
  - codebase-research
---

Примени предварительно загруженный `codebase-research`. Каноническая методика
находится в `.agents/skills/codebase-research/SKILL.md`. Этот файл задаёт только
Claude Code adapter. Не изменяй файлы.

@AGENTS.md

# Claude Code adapter

Общие проектные правила находятся только в `AGENTS.md` и импортированы выше.

Claude Code-specific discovery:

- `.claude/skills/*/SKILL.md` — тонкие adapters к каноническим
  `.agents/skills/*/SKILL.md`;
- `.claude/agents/*.md` — native read-only role adapters;
- tool/model/frontmatter настройки допустимы только в adapters и не меняют
  общую методику или permission boundaries.

После изменения `.claude/agents/*.md` начни новую Claude Code session, чтобы
перезагрузить определения ролей.

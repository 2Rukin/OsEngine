#!/usr/bin/env bash
set -u
set -o pipefail

failures=()
checks=0
strict_clean=false
baseline=""

while (($# > 0)); do
    case "$1" in
        --strict-clean)
            strict_clean=true
            shift
            ;;
        --baseline)
            if (($# < 2)); then
                echo "ERROR: --baseline requires a commit" >&2
                exit 2
            fi
            baseline="$2"
            shift 2
            ;;
        --help)
            echo "Usage: bash .agents/validation/validate-agent-system.sh [--strict-clean] [--baseline COMMIT]"
            echo "Offline structural validation only; does not build or launch OsEngine."
            exit 0
            ;;
        *)
            echo "ERROR: unknown option: $1" >&2
            exit 2
            ;;
    esac
done

repo_root="$(git rev-parse --show-toplevel 2>/dev/null)" || {
    echo "ERROR: run inside a Git checkout" >&2
    exit 2
}
cd "$repo_root" || exit 2

pass() {
    checks=$((checks + 1))
    echo "PASS $1"
}

fail() {
    checks=$((checks + 1))
    failures+=("$1")
    echo "FAIL $1"
}

require_file() {
    if [[ -f "$1" ]]; then
        pass "file $1"
    else
        fail "missing file $1"
    fi
}

require_contains() {
    local file="$1"
    local token="$2"
    local label="$3"
    if [[ -f "$file" ]] && grep -Fq -- "$token" "$file"; then
        pass "$label"
    else
        fail "$label (missing: $token)"
    fi
}

reject_contains() {
    local file="$1"
    local token="$2"
    local label="$3"
    if [[ -f "$file" ]] && grep -Fq -- "$token" "$file"; then
        fail "$label (forbidden: $token)"
    else
        pass "$label"
    fi
}

required_files=(
    AGENTS.md
    CLAUDE.md
    project/AGENTS.md
    project/Documentation/DOCUMENTATION_MAP.md
    project/Documentation/AgentSystem/README.md
    project/Documentation/AgentSystem/ADR-0001_AGENT_WORKFLOW.md
    project/Documentation/AgentSystem/ACTIVE_TASK.md
    project/Documentation/AgentSystem/FULL_REVIEW_INDEX.md
    .agents/CLI_WORKFLOW_CONTRACT.md
    .agents/workflow/SCOPED_REVIEW.md
    .agents/workflow/REPOSITORY_WIDE_REVIEW.md
    .agents/workflow/IMPACT_AND_HANDOFF.md
    .agents/workflow/EXTERNAL_AND_LIVE_EVIDENCE.md
    .agents/templates/FULL_CODE_REVIEW_REPORT.md
)

for path in "${required_files[@]}"; do
    require_file "$path"
done

skills=(
    codebase-research
    documentation-review
    csharp-xml-doc-style
    production-code-review
    production-review-checklist
)

for skill in "${skills[@]}"; do
    skill_file=".agents/skills/$skill/SKILL.md"
    policy_file=".agents/skills/$skill/agents/openai.yaml"
    require_file "$skill_file"
    require_contains "$skill_file" "name: $skill" "skill name $skill"
    require_contains "$skill_file" "Внутрен" "internal skill marker $skill"
    require_file "$policy_file"
    require_contains "$policy_file" "allow_implicit_invocation: false" "implicit invocation disabled $skill"
done

roles=(codebase-researcher documentation-reviewer production-code-reviewer)

role_count="$(find .codex/agents -maxdepth 1 -type f -name '*.toml' 2>/dev/null | wc -l | tr -d ' ')"
if [[ "$role_count" == "3" ]]; then
    pass "exactly three Codex roles"
else
    fail "expected exactly three Codex roles, got $role_count"
fi

for role in "${roles[@]}"; do
    role_file=".codex/agents/$role.toml"
    require_file "$role_file"
    require_contains "$role_file" "name = \"$role\"" "Codex role name $role"
    require_contains "$role_file" "sandbox_mode = \"read-only\"" "Codex role sandbox $role"
    require_contains "$role_file" "approval_policy = \"never\"" "Codex role approval $role"
    require_contains "$role_file" ".agents/CLI_WORKFLOW_CONTRACT.md" "Codex role workflow route $role"
    if grep -Eq '^[[:space:]]*model(_reasoning_effort)?[[:space:]]*=' "$role_file"; then
        fail "Codex role pins model: $role"
    else
        pass "Codex role does not pin model: $role"
    fi
    require_file ".claude/agents/$role.md"
done

require_contains AGENTS.md ".agents/CLI_WORKFLOW_CONTRACT.md" "root routes compact workflow"
require_contains AGENTS.md "project/AGENTS.md" "root routes project supplement"
require_contains AGENTS.md "2Rukin/OsEngine" "fork is current repository"
require_contains project/AGENTS.md "../AGENTS.md" "project scope routes root"
require_contains .agents/workflow/SCOPED_REVIEW.md "PRIMARY -> FIX -> VALIDATION_1 -> TERMINAL" "finite scoped review route"
require_contains .agents/workflow/REPOSITORY_WIDE_REVIEW.md "PRIMARY_FULL_REVIEW -> COVERAGE_VALIDATION -> TERMINAL" "finite full review route"
require_contains .agents/workflow/EXTERNAL_AND_LIVE_EVIDENCE.md "REQUIRES LIVE CONNECTOR" "live evidence fail-closed verdict"
require_contains .agents/skills/csharp-xml-doc-style/SKILL.md "Нетронутый legacy-код не получает массовый backfill" "legacy XML-doc boundary"
require_contains .agents/skills/production-review-checklist/SKILL.md "Causality и mode parity" "OsEngine mode-parity checklist"

if [[ -f .codex/config.toml ]] && grep -Eq 'danger-full-access|approval_policy[[:space:]]*=[[:space:]]*"never"' .codex/config.toml; then
    fail "project Codex config escalates Main permissions"
else
    pass "no project-wide Codex permission escalation"
fi

if grep -R -n -E 'src/main/java|mvnw|javadoc-style|TRANSAQ SERVER|txml-market' \
    .agents/CLI_WORKFLOW_CONTRACT.md .agents/workflow .agents/skills >/dev/null 2>&1; then
    fail "stale TXML/Java route remains in canonical agent layer"
else
    pass "canonical layer has no stale TXML/Java route"
fi

active="project/Documentation/AgentSystem/ACTIVE_TASK.md"
active_lines="$(wc -l < "$active" | tr -d ' ')"
active_chars="$(wc -c < "$active" | tr -d ' ')"
if ((active_lines <= 180 && active_chars <= 14000)); then
    pass "active task compact bounds"
else
    fail "active task exceeds bounds: ${active_lines} lines, ${active_chars} bytes"
fi

for token in "**ID:**" "**Статус:**" "**Фаза:**" "**Ветка:**" \
    "**Completed transition IDs:**" "**Next transition ID:**" \
    "## Frozen scope" "## Verification status" "## Blockers" "## Next action"; do
    require_contains "$active" "$token" "active task field $token"
done

active_count="$(find project/Documentation -type f -name ACTIVE_TASK.md | wc -l | tr -d ' ')"
if [[ "$active_count" == "1" ]]; then
    pass "single authoritative active task"
else
    fail "expected one ACTIVE_TASK.md, got $active_count"
fi

docmap="project/Documentation/DOCUMENTATION_MAP.md"
for id in AGENT-WORKFLOW-001 ADR-AGENT-001 ACTIVE-TASK-001 \
    FULL-REVIEW-INDEX-001 FULL-REVIEW-TEMPLATE-001 SKILL-XMLDOC-001 \
    ORDER-FLOW-ROADMAP-001; do
    require_contains "$docmap" "$id" "documentation registration $id"
done

review_fixture=".agents/validation/fixtures/review-cases.tsv"
if awk -F '\t' '
    NR == 1 {
        expected = "id\tscope_relation\ttechnical_severity\tbusiness_impact\tevidence_status\treachability\tevidence\trecommended_action\towner_decision\tterminal\tmax_validation_pass"
        if ($0 != expected) exit 10
        next
    }
    NF != 11 { exit 11 }
    {
        rows++
        if ($11 < 0 || $11 > 2) exit 12
        if ($6 == "UNREACHABLE" && ($8 != "DO_NOT_FIX" || $10 != "NO_CHANGE")) exit 13
        if ($6 == "NOT_PROVEN" && ($8 != "REQUIRES_EVIDENCE" || $10 != "NO_CHANGE")) exit 14
        if ($2 == "ADJACENT" && $10 != "SEPARATE_TASK") exit 15
        if (($5 != "PROVEN" || $7 == "MISSING") && $8 != "REQUIRES_EVIDENCE") exit 16
    }
    END { if (rows < 7) exit 17 }
' "$review_fixture"; then
    pass "review decision fixtures"
else
    fail "review decision fixtures"
fi

impact_fixture=".agents/validation/fixtures/impact-cases.tsv"
if awk -F '\t' '
    NR == 1 { next }
    NF != 10 { exit 20 }
    $1 == "state_only" {
        found_state = 1
        if ($7 != "NO_CHANGE" || $8 != "NO_CHANGE" || $9 != "STATE_ONLY" || $10 != "NO_CHANGE") exit 21
    }
    $1 == "runtime_failure" {
        found_runtime = 1
        if ($7 != "REQUIRED" || $8 != "REQUIRED" || $9 != "FINAL_BUILD" || $10 != "REQUIRED") exit 22
    }
    END { if (!found_state || !found_runtime) exit 23 }
' "$impact_fixture"; then
    pass "impact/evidence-reuse fixtures"
else
    fail "impact/evidence-reuse fixtures"
fi

resume_fixture=".agents/validation/fixtures/resume-cases.tsv"
if awk -F '\t' '
    NR == 1 { next }
    NF != 7 { exit 30 }
    {
        rows++
        split($5, completed, ",")
        duplicate = 0
        for (i in completed) if (completed[i] == $6) duplicate = 1
        expected = duplicate ? "false" : "true"
        if ($7 != expected) exit 31
    }
    END { if (rows < 5) exit 32 }
' "$resume_fixture"; then
    pass "resume transition fixtures"
else
    fail "resume transition fixtures"
fi

report_fixture=".agents/validation/fixtures/full-review-report.md"
for field in "**Baseline commit:**" "## Coverage matrix" "## Finding registry" \
    "**Technical severity:**" "**Business impact:**" "**Affected modes/components:**" \
    "**Strongest counterevidence:**" "**Evidence status:**" "**Reachability:**" \
    "**Recommended action:**" "**Owner decision:**" "## Owner triage"; do
    require_contains "$report_fixture" "$field" "full-review fixture field $field"
done

finding_count="$(grep -c '^### Finding ' "$report_fixture" || true)"
sequence_count="$(grep -c '^```mermaid$' "$report_fixture" || true)"
if ((finding_count >= 2 && sequence_count >= finding_count)); then
    pass "per-finding Mermaid sequences"
else
    fail "expected Mermaid sequence for each finding"
fi

coverage_block="$(sed -n '/^## Coverage matrix$/,/^## Finding registry$/p' "$report_fixture")"
if grep -Fq 'PENDING' <<<"$coverage_block"; then
    fail "terminal coverage contains PENDING"
else
    pass "terminal coverage has no PENDING"
fi

if git diff --check && git diff --cached --check; then
    pass "git diff whitespace"
else
    fail "git diff whitespace"
fi

if [[ -n "$baseline" ]]; then
    if git cat-file -e "$baseline^{commit}" 2>/dev/null && git merge-base --is-ancestor "$baseline" HEAD; then
        pass "baseline is ancestor of HEAD"
    else
        fail "baseline is not a resolvable ancestor: $baseline"
    fi
fi

if [[ "$strict_clean" == true ]]; then
    if [[ -z "$(git status --porcelain=v1)" ]]; then
        pass "working tree clean"
    else
        fail "working tree is not clean"
    fi
fi

if ((${#failures[@]} > 0)); then
    echo "AGENT SYSTEM VALIDATION FAILED (${#failures[@]} of $checks checks):" >&2
    for failure in "${failures[@]}"; do
        echo "- $failure" >&2
    done
    exit 1
fi

echo "PASS: agent-system offline acceptance ($checks checks). OsEngine, MCP, connectors and network were not started."

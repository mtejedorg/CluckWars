# Antigravity CLI (agy) Guide

Welcome to the comprehensive guide for using **Antigravity CLI** (`agy`), Google DeepMind's terminal-based autonomous coding agent interface. This guide covers how to set up, operate, configure, and automate the Antigravity agent directly from your terminal.

---

## 1. Overview & Setup

The Antigravity CLI (`agy`) runs the same core agentic reasoning engine as the desktop version, optimized for a keyboard-first, shell-integrated workspace.

### Installation
For Windows, you can install or update the CLI using PowerShell:
```powershell
# Run the installation / update script
agy update
```
*(For macOS/Linux, run the corresponding setup shell script from [antigravity.google](https://antigravity.google)).*

### Authentication & Non-Interactive Mode
To allow an external runner (e.g., CI/CD system, cron jobs, or another orchestration agent) to run `agy` in a headless environment without manual browser authentication, set the following environment variable:

```powershell
# Windows PowerShell
$env:ANTIGRAVITY_API_KEY="your-api-key-here"

# Linux / macOS / Bash
export ANTIGRAVITY_API_KEY="your-api-key-here"
```

---

## 2. Command Line Interface Usage

When starting the CLI, you can provide flags to customize the execution mode.

### Available CLI Flags

| Flag | Description |
| :--- | :--- |
| `--dangerously-skip-permissions` | **Extreme YOLO Mode.** Disables all interactive tool approvals and command execution confirmations, allowing the agent to run completely unattended. |
| `-p` / `--print` | Attempts to output non-interactive results to the console (useful for scripting, but subject to TTY detection limits). |
| `--print-timeout <duration>` | Sets a timeout for the print mode (e.g., `30s`, `5m`). |
| `--version` | Displays the current version of the installed CLI. |

### Running a Direct Goal
You can start a task immediately from your shell:
```powershell
agy "refactor the player movement logic in PlayerController.cs to use new Input System"
```

---

## 3. In-App Slash Commands

Once inside the interactive TUI (Terminal User Interface), you communicate with the agent using inputs and specific `/` commands.

* **`/goal`**
  Instructs the agent to work autonomously toward a specific objective, executing loops of planning, coding, and verification until completed.
* **`/inspect`**
  Prints the active environment status, including loaded configuration files, detected workspace tools, and active Model Context Protocol (MCP) servers.
* **`/permissions`**
  Opens the interactive permission manager where you can whitelist or deny commands (e.g., allow `git` but block system commands) and file directories.
* **`/context`**
  Visualizes the current project structure, open files, and active session history.
* **`/btw`**
  Appends background information or nuance to the agent's context without starting a new task action.
* **`/settings` / `/config`**
  Displays configuration overlays to adjust theme preferences, language models, or target settings.
* **`/mcp`**
  Opens the interactive MCP server manager.

---

## 4. Configuring MCP (Model Context Protocol)

The Antigravity ecosystem supports MCP to dynamically load external tools. You can register custom MCP servers globally by modifying the configuration file.

### Configuration Path
* **Windows:** `%USERPROFILE%\.gemini\config\mcp_config.json`
* **macOS/Linux:** `~/.gemini/config/mcp_config.json`

### Example `mcp_config.json`
```json
{
  "mcpServers": {
    "graphify": {
      "command": "npx",
      "args": [
        "-y",
        "@mtejedorg/graphify"
      ]
    },
    "custom-database-tool": {
      "command": "node",
      "args": [
        "C:/tools/db-mcp/index.js"
      ],
      "env": {
        "DB_CONNECTION_STRING": "Server=localhost;Database=Test;"
      }
    }
  }
}
```

---

## 5. Extensibility with Custom Skills

**Skills** are reusable instructions, checklists, and scripts that extend your agent's capabilities.

### Skill Directories
1. **Global Skills:** Saved in `~/.gemini/antigravity-cli/skills/`
2. **Project-Local Skills:** Saved in the `.agents/skills/` folder at your project's root directory.

### Defining a Custom Skill
A skill is represented by a Markdown file containing YAML frontmatter. For example, `.agents/skills/lint-check.md`:

```markdown
---
name: lint-check
description: Run code quality linter and automatically fix formatting issues.
---
# Lint Check Skill
1. Execute the project's linter using `npm run lint`.
2. Inspect output and resolve syntax or formatting issues.
3. Commit lint fixes if any changes were made.
```

Once saved, the skill registers automatically. You can invoke it within the `agy` TUI by typing `/lint-check`.

---

## 6. Automation & Scripting Integration

> [!WARNING]
> Running the CLI headlessly (without a pseudo-TTY allocated) can cause standard output streams to be suppressed. When calling `agy` from other agents or subprocesses, ensure you have allocated a PTY/TTY if you need to parse the stdout logs dynamically. Track issue progress on the official repository for updates on non-TTY compatibility.

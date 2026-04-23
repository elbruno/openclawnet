---
name: file-system
description: "Read, write, list, and manipulate files and directories on the local machine."
category: system
tags:
  - files
  - directories
  - file-management
  - io
examples:
  - "Read the content of config.json"
  - "List all files in the current directory"
  - "Create a new file with text content"
enabled: true
---

# File System Skill

You have access to file system tools that allow you to read, write, list, and manipulate files and directories on the local machine.

## Capabilities

- **Read files**: Read the content of text files
- **Write files**: Create or overwrite files with new content  
- **List directories**: List files and subdirectories in a given path
- **Delete files**: Remove files from the file system
- **Move/Copy files**: Reorganize files and directories

## Guidelines

- Always confirm destructive operations (delete, overwrite) before proceeding
- Use relative paths when possible; if absolute paths are needed, prefer paths under the current workspace
- When reading large files, consider summarizing or reading relevant sections only
- Respect user privacy — do not read files outside the intended workspace without explicit permission

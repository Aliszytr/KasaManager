WORKFLOW: /safeops

Safety locks (non-negotiable):
- Do not execute destructive commands without explicit confirmation:
  rm -rf, del /s, rmdir, git clean -fdx, format/diskpart, Remove-Item -Recurse, etc.
- If cleanup is needed:
  1) show the exact command
  2) explain scope (which folders/files)
  3) wait for explicit "OK" from the user
- Prefer read-only inspection first.
- No credential exfiltration, no secrets output.
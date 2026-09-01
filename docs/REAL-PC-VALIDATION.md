# Real-PC Validation Checklist

Run this after GitHub CI is green and before treating a release as locally validated.

1. On Windows 11 PowerShell, run `powershell -ExecutionPolicy Bypass -File .\install.ps1` from a clean checkout.
2. Double-click **Terminal Command** and confirm exactly one usable terminal window opens.
3. Run `/doctor`; confirm core is healthy and optional missing tools are reported as optional.
4. Run `cd <known-folder>`, then `pwd`/`cd` and a typed capability such as `show git status`; confirm the same session directory is used.
5. Disconnect/stop Ollama and confirm shell commands, `/help`, `/project`, `/history`, and deterministic natural-language routes still work.
6. With Ollama available, confirm natural language can select a typed capability and `/explain <request>` shows the capability/policy before execution.
7. Confirm a read-only operation runs without approval, a mutating operation requests approval, and a catastrophic test string is denied without execution.
8. Run `/benchmark`; confirm it reports routing scores and does not execute commands.
9. Run a local daily operation (file search/hash) and an engineering inspection (Git diff/log or process inspection).
10. Invoke a defensive security or remote action and confirm approval is required before it runs. Use only an authorized local/test target.
11. Configure a test HTTPS update manifest, run `/update check` and `/update prepare`, and confirm the active release does not change during preparation.
12. Apply a test newer release; confirm it becomes current only after its own `--doctor` succeeds. Test `/update rollback` and confirm the prior release becomes current again.
13. Close and reopen from the desktop shortcut; confirm project/history/config state persists.
14. Run `.\uninstall.ps1`; confirm the install root/shortcut are removed while `~/.terminal-command` state remains. Remove state only with explicit `-RemoveState`.

Record any hardware-, terminal-, WSL-, Ollama-, antivirus-, or permission-specific issue as a release blocker if it affects normal launch, policy, execution, update safety, or state integrity.

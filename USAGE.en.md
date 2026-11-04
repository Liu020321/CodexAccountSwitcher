# Codex Settings v7.2.7 · Usage

[简体中文](使用说明.md) | [English](USAGE.en.md)

## Requirements and installation

Use Windows 11 x64 with .NET Framework 4.8. For the portable edition, extract to a local folder you can write to and run `CodexSettings.exe`. Alternatively, use the current-user EXE installer. Install the official Codex CLI separately and make its `codex` command available on PATH for CLI launching and quota queries.

Install [Microsoft PowerToys](https://github.com/microsoft/PowerToys) and enable File Locksmith before using the external file lock handling integration. [Official download](https://github.com/microsoft/PowerToys/releases/latest). PowerToys is not bundled.

## First use

1. Open the directory and name manager. Select an account folder or its parent, then assign two sibling account directories and display names. Display names do not rename disk folders.
2. The desktop identity is read from `.codex` under the current Windows user profile. Account emails are read locally, not preset.
3. Exit applications using the relevant configurations before switching. Migration stages and verifies copies. If an error occurs, follow the displayed instructions to resume or roll back.
4. On the CLI page, choose a project folder and terminal. The app enters that folder, sets a temporary `CODEX_HOME` for the other account, then starts Codex. The default project folder is `D:\`.
5. Single-click the tray icon for quotas, double-click for settings, or right-click for the menu. Quotas are queried when the preview opens and cached for three minutes; there is no scheduled background quota polling.

## Interrupted migrations and file locks

Resume continues the same operation with its saved overall percentage, including after an app restart. Existing copies are verified and reused when identical. Changed files are recopied while the source is still complete; a partially copied individual file restarts from its beginning. Verification can leave the progress indicator unchanged temporarily.

Once source cleanup has started, retained backups are verified strictly. An incomplete source must not overwrite the complete backup. Completion clears the operation's recovery record and resets its action state. Verification failures retain source data and recovery information.

For actual lock or permission errors, use the PowerToys button to inspect affected files. Close the relevant process, then return and choose recheck and continue. PowerToys can rescan with administrator privileges when needed. An access-denied error can be a permissions or file-attribute issue, and a content mismatch is a verification error; neither automatically means a process still holds a lock. Finding no process does not guarantee that a file is movable.

When PowerToys is unavailable, the built-in fallback verifies process identity and termination results. Failures are shown; elevation requires an explicit action and Windows UAC confirmation. The app does not bypass file locks or rewrite access-control permissions. Read-only attributes may be handled during cleanup only after verification.

## Portable files, sharing, and removal

The original portable distribution contains program files, documentation, checksums, and empty account directories. It contains no real account credentials, tokens, chats, personal settings, logs, screenshots, audio, or video.

After use, settings and logs may appear beside the program and contain local paths. Your selected account folders contain sensitive credentials and chat data. Share the original release ZIP or installer instead of repackaging your used folder.

The installer defaults to `LocalAppData\Programs\CodexAccountSwitcher`, needs no administrator privileges, and adds no automatic startup. Uninstalling removes distributed program and documentation files; user-created accounts, settings, and logs are retained. Resume or roll back any unfinished migration before removing the app. Do not delete recovery records or staging copies manually.

## Security and further reading

The app runs as a normal user by default. It has no designed remote-control interface, network listener, account-file upload, or telemetry. Quota queries use the local official Codex CLI and its services. Terminating a process may interrupt unsaved work.

Tests use isolated fictional accounts. This is not a comprehensive independent security audit, and the app is unsigned. Check `SHA256SUMS.txt` for integrity and follow your organization's software policies; no software can promise zero vulnerabilities.

See the [illustrated product guide](docs/PRODUCT_GUIDE.en.md) and [third-party notices](THIRD_PARTY_NOTICES.md).

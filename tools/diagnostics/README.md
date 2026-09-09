# Classic / New Classic resize investigation

Verified against the running Steam games on 2026-09-10. This is a temporary diagnostic probe, not production integration.

- Classic: client area 1280x960 -> 1600x1200, held for 25 seconds; full gameplay image filled the resized client area.
- New Classic: client area 1920x1080 -> 1600x900, held for 25 seconds; full title image filled the resized client area after the extra notification described below.
- Both tests restored original outer rectangles and window styles.
- Ordinary SetWindowPos, SWP_NOSENDCHANGING alone, and enabling a resizable window style did not work.

Successful sequence: WM_ENTERSIZEMOVE, WM_SIZING (WMSZ_BOTTOMRIGHT with desired outer rectangle), SetWindowPos, WM_EXITSIZEMOVE. The probe also sends WM_SIZE. For New Classic, a subsequent WM_SIZING using the pre-resize outer rectangle removed the double-scaling/black margins. The precise role of each redundant message has not been isolated; further work is needed before production integration, especially repeated resizing and different original game resolutions.

No executable patch, DLL injection, remote thread, or game memory write was used. Notifications are sent using SendMessageTimeoutW. The try/finally block restores position, dimensions, and style.

Run from Git Bash with the game already running:

```bash
python tools/diagnostics/probe_resize.py --game classic --hold 25
python tools/diagnostics/probe_resize.py --game nc --hold 25
```

The script temporarily changes window dimensions and automatically restores them after the hold. Keep the original game resolution unchanged during the test. Production application code is unchanged.

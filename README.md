# RiotUnlocker

Unlocker for **RIOT - Civil Unrest** (Steam). Unlocks all missions and campaigns instantly.

## Usage

1. Download the binary for your OS from the [latest release](../../releases/latest)
2. Run it — the game is found automatically via Steam
3. Start the game

No installation required. No .NET required.

If the game isn't found automatically, pass the path manually:

```
RiotUnlocker "path/to/Riot_Data/Managed/Assembly-CSharp.dll"
```

## How it works

- **DLL patch** — injects `EVERYTHING_UNLOCKED = true` into `Assembly-CSharp.dll` so all lock checks return true. A `.bak` backup is created automatically.
- **Save file** (Linux) — writes `~/.config/unity3d/DefaultCompany/Riot/prefs` with all 37 missions marked complete for both factions.

## Restore

```
cp Assembly-CSharp.dll.bak Assembly-CSharp.dll
```

## License

Apache 2.0 — see [LICENSE](LICENSE).

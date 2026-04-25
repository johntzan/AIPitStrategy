# Install — PitStrategy plugin for SimHub

## Prerequisites

- Windows 10/11
- iRacing
- [SimHub](https://www.simhubdash.com/) installed at `C:\Program Files (x86)\SimHub\`
- (Build only) [Visual Studio 2022 Community](https://visualstudio.microsoft.com/vs/community/) with the *.NET desktop development* workload, and [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Option A — install a release zip

1. Download `PitStrategy-vX.Y.Z-windows.zip` from the [Releases](../../releases) page.
2. Extract `PitStrategy.dll` into `C:\Program Files (x86)\SimHub\` (alongside SimHub's own DLLs).
3. Restart SimHub.
4. SimHub may prompt you the first time it sees the DLL — click **Yes** to enable.
5. In SimHub, go to **Settings** → **Additional plugins** and confirm **Pit Strategy** is enabled.
6. Build a dashboard yourself in Dash Studio (see ["Building the dashboard in Dash Studio"](#building-the-dashboard-in-dash-studio) below) — a pre-built `PitStrategy.simhubdash` is not yet shipped. The release zip includes `PitStrategy.simhubdash.template.json` documenting the available properties and example NCalc bindings.

## Option B — build from source

1. Install the prerequisites above.
2. Clone this repo.
3. Copy the SimHub SDK DLLs into `lib/simhub-sdk/`:
   ```powershell
   Copy-Item "C:\Program Files (x86)\SimHub\SimHub.Plugins.dll"  lib\simhub-sdk\
   Copy-Item "C:\Program Files (x86)\SimHub\GameReaderCommon.dll" lib\simhub-sdk\
   ```
4. Build the plugin:
   ```powershell
   dotnet build src\PitStrategy.Plugin\PitStrategy.Plugin.csproj -c Release
   ```
   The Release post-build target auto-copies `PitStrategy.dll` into `C:\Program Files (x86)\SimHub\`.
5. Restart SimHub.

## First run

1. Launch iRacing and start a practice session in any car.
2. Go to SimHub → **Available properties** and search for `PitStrategy`. You should see ~30 properties under that namespace.
3. Drive 5 green-flag laps. After that, `PitStrategy.Fuel.HasEnoughData` flips to `true` and `PitStrategy.Decision.Kind` starts emitting recommendations.
4. Open the **Pit Strategy** settings page (left menu in SimHub) and enable *Auto-dump frames* if you want to capture data for debugging.

## Building the dashboard in Dash Studio

Until a pre-built `.simhubdash` ships with the release, you'll build a quick test dashboard manually:

1. Open SimHub Dash Studio → **New dashboard**.
2. Add a Text widget. Bind its text to:
   ```
   [PitStrategy.Decision.Kind]
   ```
   and its background color to:
   ```
   if([PitStrategy.Decision.Kind] = 'PitNow' and [PitStrategy.Decision.Confidence] > 0.7, '#34C759',
     if([PitStrategy.Decision.Kind] = 'PitNow', '#FFCC00',
       if([PitStrategy.Decision.Kind] = 'StayOut', '#1E2329', '#FF9500')))
   ```
3. Add a second Text widget bound to `[PitStrategy.Decision.PrimaryReason]`.
4. Add a third bound to `format([PitStrategy.Fuel.LapsToEmpty], '0.0')`.

That's enough to glance at while driving. Refer to `dashboards/PitStrategy.simhubdash.template.json` for the full set of bindings.

## Troubleshooting

### "PitStrategy" doesn't show in SimHub

- Confirm `PitStrategy.dll` is in `C:\Program Files (x86)\SimHub\` (root, not the `Plugins` subfolder).
- Restart SimHub. On first run, it should pop up a dialog asking to enable the new plugin.
- Check `%LocalAppData%\SimHub\logs\` for any "Failed to load" errors mentioning PitStrategy.

### Properties stuck at zero

- Drive 5+ green laps. The fuel tracker needs samples before it can predict.
- Confirm you're in iRacing — the plugin no-ops on other sims by design.

### "Auto-dump frame" doesn't write files

- Files land in `%TEMP%\PitStrategy\frame-*.json`. Check that path exists and is writable.

### iRacing field-name mismatches

If you see runtime errors about missing fields (e.g. `Telemetry.PitsOpen` not found), the iRacing telemetry contract has shifted between subscription updates. File an issue with the SimHub log and the iRacing build version, and check `docs/IRACING-FIELDS.md` for verified field names.

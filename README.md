# Object Outliner

A free Rhino 8 panel by 9dpbQ for browsing layers, groups, objects, and history
in one tree. Select, rename, filter, or move objects without leaving the panel.

## Install

In Rhino's Package Manager, search for `ObjectOutliner`, install it, and restart
Rhino if prompted. Run `ObjectOutliner` to open the panel.
The plug-in targets Rhino 8.15 or later on macOS and Windows. See the
[compatibility notes](docs/RHINO_COMPATIBILITY.md) for the API baseline and
testing limits. Please keep a backup when working on important models.

## Build from source

On macOS, install the Xcode Command Line Tools, then run from the repository root:

```bash
bash scripts/bootstrap.sh
bash scripts/check-objectmanager.sh
```

This installs the pinned .NET SDK locally, runs the regression suite, and builds
the Mac and Windows compatibility targets. The Mac plug-in is
`ObjectManager/bin/Debug/net7.0/ObjectOutliner.rhp`; keep the other files in
its output directory alongside it. On Windows, with the SDK from
`global.json` installed, run:

```powershell
dotnet build ObjectManager/ObjectManager.csproj -f net48
```

The main panel and document integration are in `ObjectManager/`, the WPF
tree guides in `WindowsTreeGuides/`, and regression tests in
`ObjectManager.RegressionTests/`. Release staging is separate from publishing:
`bash scripts/prepare-object-outliner-release.sh` builds a staging directory
on macOS but does not create or upload a Yak package.

## License

The source and original project artwork in this repository are provided under
the [MIT License](LICENSE.txt). Rhino and its built-in assets are not part of
this license. Object Outliner is independent of McNeel.

Object Outliner 0.9.3 Public Beta
=================================

Object Outliner adds an outliner panel to Rhino 8. It lists layers,
groups, and objects in one tree and supports selection, visibility, locking,
filtering, naming, drag and drop, favorites, and Rhino history relationships.

Requirements
------------

- Rhino 8.15 or later
- macOS or Windows

The minimum version is based on the Rhino APIs used by the plug-in and builds
against the Rhino 8.15 SDK. Runtime testing on Rhino 8.15 itself is still pending.

Getting started
---------------

1. Install ObjectOutliner from Rhino's Package Manager.
2. Restart Rhino when prompted.
3. Run the ObjectOutliner command to open the panel.

Public beta note
----------------

This plug-in was originally made for a personal Rhino workflow and is now
shared as a free public beta. It has been tested on macOS and Windows, but
unexpected issues may still occur. Please keep a backup when working with
important .3dm files.

Privacy
-------

Isolate and IsolateLock are temporary session states. Saved files reopen with
temporary isolation released; the current view is preserved while saving.
ObjectOutlinerDebugMode toggles local performance logging (off at startup).

Object Outliner does not send model data or telemetry over the network.
The plug-in stores its workspace state, favorites, and managed display names
inside the .3dm document. Release builds do not create performance log files
automatically.

License
-------

This build is distributed under the MIT License. See the included LICENSE.txt.

日本語
------

Object Outlinerは、Rhino 8のレイヤー、グループ、オブジェクトを
1つのツリーで確認・管理するための無料Public Betaプラグインです。
インストール後、Rhinoを再起動してObjectOutlinerコマンドを実行してください。

Isolate / IsolateLockは作業中だけの状態となり、ファイルを開き直すと解除されます。
保存直後の作業画面は維持されます。性能ログはObjectOutlinerDebugModeで切り替えます。

対応はRhino 8.15以降です。使用APIと8.15 SDKでのビルド検証を根拠にしています。
Rhino 8.15そのものでの実機確認は、今後行う予定です。

MacとWindowsで動作確認を行っていますが、予期しない不具合が残っている
可能性があります。大切な.3dmファイルを扱う際は、念のためバックアップを
残してからご利用ください。

Object Outliner 1.0.0
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

New in 1.0.0
------------

- Add per-object notes.
- Improve tree updates in large models.
- Make nested relationships easier to see in the tree.

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
1つのツリーで確認・管理するための無料プラグインです。
インストール後、Rhinoを再起動してObjectOutlinerコマンドを実行してください。

Isolate / IsolateLockは作業中だけの状態となり、ファイルを開き直すと解除されます。
保存直後の作業画面は維持されます。性能ログはObjectOutlinerDebugModeで切り替えます。

対応はRhino 8.15以降です。使用APIと8.15 SDKでのビルド検証を根拠にしています。
Rhino 8.15そのものでの実機確認は、今後行う予定です。

1.0.0の変更点

- オブジェクトにメモを追加
- 大きなモデルでのツリー更新を改善
- ネスト関係が見やすくなるようにUIを改善

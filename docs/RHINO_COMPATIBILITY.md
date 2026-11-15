# Rhino最低対応版の根拠

確認日: 2026-09-04 / 適用: Object Outliner 0.9.2以降

## 方針

- 最低要件はRhino 8.15とする。
- ビルドではRhinoCommon `8.15.25013.13001`を参照する。
- `net7.0`と`net48`の両ターゲットを維持する。
- 開発者のRhinoを更新しても参照SDKは自動的に引き上げない。
- 8.15実機での確認は未完了であり、API互換性の検証と区別する。

0.9.0と0.9.1のRhino 8.32要件は、当時の開発SDKを基準にしていた。
0.9.2では使用APIに合わせて下限を下げる。機能や`.3dm`の保存形式は変更しない。

## 下限を決めるAPI

| 使用API | 追加版 | 使用箇所・目的 |
|---|---|---|
| `RhinoObject.HistoryParents()` / `HistoryChildren()` | 8.13 | `OutlinerHistoryReader`と`ObjectNamingCoordinator`のヒストリー参照・派生命名 |
| `RhinoDoc.AfterTransformObjects` / `RhinoAfterTransformObjectsEventArgs` | 8.15 | `ObjectNamingCoordinator`の複製完了追跡 |
| `RhinoTransformObjectsEventArgs.TransformEventId` | 8.15 | ガムボールなどの複製操作の開始・完了を対応付ける |

複製追跡は、コピー名、グループの対応付け、展開状態の継承にも関係する。
8.15より前へ対応させる場合は、この経路の互換実装と追加QAが必要になる。

## ビルドによる確認

リリース済み0.9.1のソースを一時ディレクトリへ複製し、SDK参照だけを変更して
両ターゲットをビルドした。8.15の最初の正式SDKまで遡って確認している。

| RhinoCommon | `net7.0` | `net48` | 結果 |
|---|---|---|---|
| 8.0.23304.9001 | 失敗 | 失敗 | `RhinoAfterTransformObjectsEventArgs`が存在しない |
| 8.13.24317.13001 | 失敗 | 失敗 | 同上 |
| 8.14.24345.15001 | 失敗 | 失敗 | 同上 |
| 8.15.25013.13001 | 成功 | 成功 | コンパイル警告・エラーなし |
| 8.15.25019.13001 | 成功 | 成功 | コンパイル警告・エラーなし |
| 8.16.25042.13001 | 成功 | 成功 | コンパイル警告・エラーなし |

8.15 SDKで作った本体をYakへ渡すと、対応タグは自動的に`rh8_15-any`になった。
タグだけを書き換えて古い版に対応しているように見せる方法は採用しない。
CIも同じ固定SDKを参照するため、それより新しいAPIの直接使用はビルドで検出される。

## 実機で残る確認

ビルド成功は、Rhinoのイベント動作やOS固有UIまで保証するものではない。
とくに内部アイコン取得、ネイティブUIアダプター、コマンド中入力は実機で確認する。

- [ ] MacのRhino 8.15でYakインストール・再起動・パネル表示を確認する。
- [ ] WindowsのRhino 8.15で同様に確認する。
- [ ] 選択同期、表示・ロック、フィルター、並べ替え、改名を確認する。
- [ ] ガムボール複製とコピー・ペーストで名前・グループ・展開状態を確認する。
- [ ] ヒストリー参照と折りたたみ表示を確認する。
- [ ] 保存・再読込、Undo/Redo、Isolate復元を確認する。
- [ ] ダークモード、アイコン、日本語IME、ミドルパンを確認する。

## 公式資料

- [古いRhinoCommonを参照する方針](https://developer.rhino3d.com/en/guides/rhinocommon/using-nuget/)
- [HistoryChildren: Rhino 8.13追加](https://developer.rhino3d.com/api/rhinocommon/rhino.docobjects.rhinoobject/historychildren)
- [TransformEventId: Rhino 8.15追加](https://developer.rhino3d.com/api/rhinocommon/rhino.docobjects.rhinotransformobjectseventargs/transformeventid)
- [RhinoCommon 8.15.25013.13001](https://www.nuget.org/packages/RhinoCommon/8.15.25013.13001)
- [Yakの対応タグは参照SDKから生成される](https://developer.rhino3d.com/en/guides/yak/creating-a-rhino-plugin-package/)

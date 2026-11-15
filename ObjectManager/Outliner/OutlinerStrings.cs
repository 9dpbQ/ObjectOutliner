using System;
using System.Globalization;
using Rhino.UI;

namespace ObjectManager.Outliner
{
    internal static class OutlinerStrings
    {
        private const int JapaneseLanguageId = 1041;

        public static bool IsJapanese
        {
            get
            {
                try
                {
                    return Localization.CurrentLanguageId == JapaneseLanguageId;
                }
                catch
                {
                    return string.Equals(
                        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                        "ja",
                        StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private static string Choose(string japanese, string english)
        {
            return IsJapanese ? japanese : english;
        }

        public static string PanelTitle => Choose("オブジェクトアウトライナー", "Object Outliner");

        public static string Name => Choose("名前", "Name");
        public static string Favorite => Choose("お気に入り", "Favorite");
        public static string Note => Choose("メモ", "Note");
        public static string NotesOnly => Choose("メモありのみ", "With notes only");
        public static string EditNote => Choose("メモを編集", "Edit note");
        public static string DeleteNote => Choose("メモを削除", "Delete note");
        public static string SaveNote => Choose("保存", "Save");
        public static string NoteTitle(string name) => string.Format(Choose("{0} のメモ", "Note for {0}"), name);
        public static string NoteSaveFailed => Choose(
            "メモを保存できませんでした。オブジェクトの編集可否を確認してください。",
            "Could not save the note. Check whether the object can be edited.");
        public static string Current => Choose("現在", "Current");
        public static string CurrentLayer => Choose("現在のレイヤ", "Current layer");
        public static string Visibility => Choose("表示", "Visibility");
        public static string On => Choose("オン", "On");
        public static string Lock => Choose("ロック", "Lock");
        public static string Color => Choose("色", "Color");
        public static string LayerColor => Choose("レイヤ色", "Layer color");
        public static string Material => Choose("マテリアル", "Material");
        public static string Linetype => Choose("線種", "Linetype");
        public static string PrintColor => Choose("印刷色", "Print color");
        public static string PrintWidth => Choose("印刷幅", "Print width");
        public static string SectionStyle => Choose("断面スタイル", "Section style");
        public static string NewDetailOn => Choose("新規詳細オン", "New detail on");
        public static string ModelOn => Choose("モデルオン", "Model on");
        public static string Count => Choose("個数", "Count");
        public static string Search => Choose("検索", "Search");

        public static string NewLayer => Choose("新規レイヤ", "New Layer");
        public static string NewSublayer => Choose("新規サブレイヤ", "New Sublayer");
        public static string DeleteSelectedItems => Choose("選択項目を削除", "Delete selected items");
        public static string ChooseColumns => Choose("列を選択", "Choose columns");
        public static string VisibleColumns => Choose("表示する列", "Visible columns");
        public static string DisplayContents =>
            Choose("表示内容を切り替え", "Choose displayed contents");
        public static string DisplayContentsHeading =>
            Choose("表示内容", "Displayed contents");
        public static string DisplayAll => Choose("すべて", "All");
        public static string DisplayStructure =>
            Choose("構造（レイヤとグループ）", "Structure (layers and groups)");
        public static string DisplayLayersOnly =>
            Choose("レイヤのみ", "Layers only");
        public static string CompactHistory =>
            Choose("ヒストリーを積み重ねて表示", "Stack History inputs");
        public static string CompactHistoryToolTip =>
            Choose(
                "同じ所属先にある再利用されていない参照元を生成結果の中に表示します",
                "Show unshared History inputs inside their result when they share a container");
        public static string ResetColumnLayout => Choose("列レイアウトをリセット", "Reset column layout");

        public static string FilterLayers => Choose("レイヤを絞り込み", "Filter layers");
        public static string LayerFilterHeading =>
            Choose("レイヤフィルター", "Layer filter");
        public static string FilterObjects => Choose("オブジェクトを絞り込み", "Filter objects");
        public static string AllLayers => Choose("すべてのレイヤ", "All layers");
        public static string CurrentLayerTree => Choose("現在のレイヤツリー", "Current layer tree");
        public static string SelectedLayerTrees => Choose("選択レイヤのツリー", "Selected layer trees");
        public static string OnLayers => Choose("オンのレイヤ", "On layers");
        public static string OffLayers => Choose("オフのレイヤ", "Off layers");
        public static string LockedLayers => Choose("ロックレイヤ", "Locked layers");
        public static string UnlockedLayers => Choose("ロックされていないレイヤ", "Unlocked layers");
        public static string LayersWithObjects => Choose("オブジェクトのあるレイヤ", "Layers with objects");
        public static string EmptyLayers => Choose("オブジェクトのないレイヤ", "Empty layers");
        public static string ObjectType => Choose("オブジェクト種類", "Object type");
        public static string AllObjectTypes => Choose("すべてのオブジェクト種類", "All object types");
        public static string ShowOnly => Choose("表示対象", "Show only");
        public static string SelectedObjectsOnly =>
            Choose("選択オブジェクトのみ", "Selected objects only");
        public static string UserNamedOnly =>
            Choose("ユーザー命名のみ", "User-named objects only");
        public static string FavoritesOnly => Choose("お気に入りのみ", "Favorites only");
        public static string Point => Choose("点", "Point");
        public static string PointCloud => Choose("点群", "Point cloud");
        public static string Curve => Choose("曲線", "Curve");
        public static string Surface => Choose("サーフェス", "Surface");
        public static string Polysurface => Choose("ポリサーフェス", "Polysurface");
        public static string Extrusion => Choose("押し出し", "Extrusion");
        public static string Mesh => Choose("メッシュ", "Mesh");
        public static string SubD => "SubD";
        public static string BlockInstance => Choose("ブロックインスタンス", "Block instance");
        public static string Annotation => Choose("注釈", "Annotation");
        public static string Light => Choose("光源", "Light");
        public static string Hatch => Choose("ハッチング", "Hatch");
        public static string Other => Choose("その他", "Other");

        public static string SelectObjectsOnThisLayer =>
            Choose("このレイヤのオブジェクトを選択", "Select Objects on This Layer");
        public static string SelectObjectsInLayerTree =>
            Choose(
                "このレイヤとサブレイヤのオブジェクトを選択",
                "Select Objects in Layer and Sublayers");
        public static string MoveSelectedObjectsToThisLayer =>
            Choose("選択オブジェクトをこのレイヤへ移動", "Move Selected Objects to This Layer");
        public static string CopySelectedObjectsToThisLayer =>
            Choose("選択オブジェクトをこのレイヤへコピー", "Copy Selected Objects to This Layer");
        public static string GroupSelectedObjects =>
            Choose("選択オブジェクトをグループ化", "Group Selected Objects");
        public static string Ungroup => Choose("グループ解除", "Ungroup");
        public static string ExpandChildren => Choose("子要素を展開", "Expand Children");
        public static string CollapseChildren => Choose("子要素を折りたたむ", "Collapse Children");
        public static string ExpandAll => Choose("すべて展開", "Expand All");
        public static string CollapseAll => Choose("すべて折りたたむ", "Collapse All");
        public static string DuplicateLayer => Choose("レイヤを複製", "Duplicate Layer");
        public static string DuplicateLayerAndObjects =>
            Choose("レイヤとオブジェクトを複製", "Duplicate Layer and Objects");
        public static string OneLayerOn => Choose("1レイヤのみオン", "One Layer On");
        public static string MatchProperties => Choose("プロパティをマッチング...", "Match Properties...");
        public static string Delete => Choose("削除", "Delete");
        public static string Rename => Choose("名前を変更 (Enter)", "Rename (Enter)");
        public static string BatchRename =>
            Choose("選択項目の名前を一括変更...", "Batch Rename Selected...");
        public static string Zoom => Choose("ズーム", "Zoom");
        public static string ZoomObjects => Choose("オブジェクトをズーム", "Zoom Objects");
        public static string ZoomMembers => Choose("メンバーをズーム", "Zoom Members");
        public static string ZoomOriginal => Choose("元のオブジェクトをズーム", "Zoom Original");
        public static string HideHistoryInputs =>
            Choose("ヒストリー入力を非表示", "Hide History Inputs");
        public static string ShowHistoryInputs =>
            Choose("ヒストリー入力を表示", "Show History Inputs");
        public static string Properties => Choose("プロパティ", "Properties");
        public static string Isolate => "Isolate";
        public static string ExitIsolate => Choose("Isolateを終了", "Exit Isolate");
        public static string IsolateLock => "Isolate Lock";
        public static string ExitIsolateLock => Choose("Isolate Lockを終了", "Exit Isolate Lock");
        public static string ColorDialog => Choose("色...", "Color...");
        public static string MaterialDialog => Choose("マテリアル...", "Material...");
        public static string LinetypeDialog => Choose("線種...", "Linetype...");
        public static string PrintColorDialog => Choose("印刷色...", "Print Color...");
        public static string PrintWidthDialog => Choose("印刷幅...", "Print Width...");
        public static string SectionStyleDialog => Choose("断面スタイル...", "Section Style...");

        public static string WaitingForDocument => Choose("ドキュメントを待機中", "Waiting for document");
        public static string OpeningDocument => Choose("ドキュメントを開いています...", "Opening document...");
        public static string LoadingObjects => Choose("オブジェクトを読み込んでいます...", "Loading objects...");
        public static string LoadingLayerObjects =>
            Choose("レイヤのオブジェクトを読み込んでいます...", "Loading layer objects...");
        public static string LoadingHistoryObjects =>
            Choose("Historyオブジェクトを読み込んでいます...", "Loading History objects...");
        public static string LoadingSelectedObjects =>
            Choose("選択オブジェクトを読み込んでいます...", "Loading selected objects...");
        public static string RefreshingSelectedObjects =>
            Choose("選択オブジェクトを更新しています...", "Refreshing selected objects...");
        public static string NoActiveDocument =>
            Choose("アクティブなドキュメントがありません", "No active document");
        public static string LoadObjects => Choose("オブジェクトを読み込む", "Load objects");
        public static string LoadObjectsToolTip =>
            Choose(
                "この大規模ドキュメントの全オブジェクトを読み込みます",
                "Load all objects for this large document");
        public static string ShowSelection => Choose("選択項目を表示", "Show selection");
        public static string ShowSelectionToolTip =>
            Choose(
                "フィルターで隠れた選択項目を一時的に表示します",
                "Temporarily reveal selected filtered items");
        public static string NoSelectedObjectsHint =>
            Choose(
                "未選択です。3Dビューで選択するか、このフィルターを解除してください",
                "Nothing selected. Select in the viewport or turn off this filter");
        public static string NoMatchingItemsHint =>
            Choose(
                "一致する項目なし。検索・フィルターを変更してください",
                "No matching items. Adjust search or filters");
        public static string NoMatchingObjectsHint =>
            Choose(
                "一致するオブジェクトなし。検索・フィルターを変更してください",
                "No matching objects. Adjust search or filters");
        public static string RefreshError =>
            Choose(
                "オブジェクトアウトライナーでエラーが発生しました。コマンド履歴を確認してください",
                "Object Outliner error - see command history");
        public static string RefreshErrorLog =>
            Choose("オブジェクトアウトライナーを更新できませんでした:", "Object Outliner could not refresh:");
        public static string DebugModeEnabled =>
            Choose(
                "Object Outlinerのデバッグモードをオンにしました。遅い処理の性能ログをコマンド履歴へ表示します。",
                "Object Outliner debug mode is on. Slow-operation performance logs will appear in command history.");
        public static string DebugModeDisabled =>
            Choose(
                "Object Outlinerのデバッグモードをオフにしました。性能ログは表示されません。",
                "Object Outliner debug mode is off. Performance logs will not appear in command history.");

        public static string PrintWidthPrompt =>
            Choose("このレイヤの印刷幅を選択してください。", "Select the print width for this layer.");
        public static string SelectLayerToMatch => Choose("マッチング元のレイヤを選択", "Select layer to match");
        public static string RenameLayer => Choose("レイヤ名を変更", "Rename layer");
        public static string RenameObject => Choose("オブジェクト名を変更", "Rename object");
        public static string RenameGroup => Choose("グループ名を変更", "Rename group");
        public static string InvalidLayerName =>
            Choose("そのレイヤ名はここでは使用できません。", "That layer name cannot be used here.");
        public static string InvalidGroupName =>
            Choose("そのグループ名は使用できません。", "That group name cannot be used.");
        public static string BatchRenameTitle => Choose("名前を一括変更", "Batch Rename");
        public static string BatchRenameMode => Choose("変更方法", "Mode");
        public static string BatchRenameFormat => Choose("連番", "Format");
        public static string BatchRenameReplace => Choose("置換", "Replace Text");
        public static string BatchRenameAdd => Choose("追加", "Add Text");
        public static string BatchRenameBaseName => Choose("基本名", "Base name");
        public static string BatchRenameStartNumber => Choose("開始番号", "Start number");
        public static string BatchRenameDigits => Choose("桁数", "Digits");
        public static string BatchRenameSeparator => Choose("区切り", "Separator");
        public static string NoSeparator => Choose("なし", "None");
        public static string BatchRenamePlacement => Choose("位置", "Position");
        public static string BatchRenameBefore => Choose("名前の前", "Before name");
        public static string BatchRenameAfter => Choose("名前の後", "After name");
        public static string BatchRenameFind => Choose("検索文字", "Find");
        public static string BatchRenameReplacement => Choose("置換文字", "Replace with");
        public static string BatchRenameMatchCase =>
            Choose("大文字と小文字を区別", "Match case");
        public static string BatchRenameText => Choose("追加文字", "Text");
        public static string BatchRenameOriginal => Choose("変更前", "Original");
        public static string BatchRenameResult => Choose("変更後", "New name");
        public static string BatchRenameApply => Choose("名前を変更", "Rename");
        public static string Cancel => Choose("キャンセル", "Cancel");
        public static string BatchRenameInvalid =>
            Choose(
                "変更後の名前を確認してください。空の名前や重複したレイヤ／グループ名は使用できません。",
                "Check the proposed names. Empty names and duplicate layer or group names are not allowed.");
        public static string ResetToDefaultSectionStyle =>
            Choose("デフォルトの断面スタイルに戻す", "Reset to default section style");
        public static string CreateCustomSectionStyle =>
            Choose("カスタム断面スタイルを作成", "Create custom section style");

        public static string HistoryParents => Choose("参照元", "Parents");
        public static string HistoryChildren => Choose("参照先", "Children");
        public static string ObjectsExpandToLoad =>
            Choose("オブジェクト（展開して読み込み）", "Objects (expand to load)");
        public static string MissingObject => Choose("見つからないオブジェクト", "Missing object");
        public static string Custom => Choose("カスタム", "Custom");
        public static string Default => Choose("デフォルト", "Default");
        public static string Continuous => Choose("実線", "Continuous");
        public static string NoPrint => Choose("印刷しない", "No Print");

        public static string UndoNewLayer => Choose("新規レイヤ", "New layer");
        public static string UndoNewSublayer => Choose("新規サブレイヤ", "New sublayer");
        public static string UndoDeleteSelectedItems => Choose("選択項目を削除", "Delete selected items");
        public static string UndoGroupSelectedObjects =>
            Choose("選択オブジェクトをグループ化", "Group selected objects");
        public static string UndoUngroup => Choose("グループ解除", "Ungroup");
        public static string UndoChangeLayerVisibility => Choose("レイヤの表示を変更", "Change layer visibility");
        public static string UndoChangeLayerLock => Choose("レイヤのロックを変更", "Change layer lock");
        public static string UndoChangeVisibility => Choose("表示状態を変更", "Change visibility");
        public static string UndoHideHistoryInputs =>
            Choose("ヒストリー入力を非表示", "Hide History inputs");
        public static string UndoShowHistoryInputs =>
            Choose("ヒストリー入力を表示", "Show History inputs");
        public static string UndoChangeLock => Choose("ロック状態を変更", "Change lock state");
        public static string UndoChangeLayerColor => Choose("レイヤ色を変更", "Change layer color");
        public static string UndoChangeLayerPrintColor => Choose("レイヤの印刷色を変更", "Change layer print color");
        public static string UndoChangeLayerLinetype => Choose("レイヤの線種を変更", "Change layer linetype");
        public static string UndoChangeLayerPrintWidth => Choose("レイヤの印刷幅を変更", "Change layer print width");
        public static string UndoChangeLayerSectionStyle =>
            Choose("レイヤの断面スタイルを変更", "Change layer section style");
        public static string UndoChangeNewDetailVisibility =>
            Choose("新規詳細の表示を変更", "Change new detail visibility");
        public static string UndoChangeModelVisibility => Choose("モデルの表示を変更", "Change model visibility");
        public static string UndoChangeObjectLayer => Choose("オブジェクトのレイヤを変更", "Change object layer");
        public static string UndoCopyObjectsToLayer => Choose("オブジェクトをレイヤへコピー", "Copy objects to layer");
        public static string UndoDuplicateLayers => Choose("レイヤを複製", "Duplicate layers");
        public static string UndoDuplicateLayersAndObjects =>
            Choose("レイヤとオブジェクトを複製", "Duplicate layers and objects");
        public static string UndoMatchLayerProperties =>
            Choose("レイヤプロパティをマッチング", "Match layer properties");
        public static string UndoDeleteLayers => Choose("レイヤを削除", "Delete layers");
        public static string UndoOneLayerOn => Choose("1レイヤのみオン", "One Layer On");
        public static string UndoMoveGroupToLayer => Choose("グループをレイヤへ移動", "Move group to layer");
        public static string UndoMoveOrReorderObjects =>
            Choose("オブジェクトを移動または並べ替え", "Move or reorder objects");
        public static string UndoMoveOrReorderLayers => Choose("レイヤを移動または並べ替え", "Move or reorder layers");
        public static string UndoRenameLayer => Choose("レイヤ名を変更", "Rename layer");
        public static string UndoRenameObject => Choose("オブジェクト名を変更", "Rename object");
        public static string UndoRenameGroup => Choose("グループ名を変更", "Rename group");
        public static string UndoBatchRename => Choose("名前を一括変更", "Batch rename");
        public static string UndoReorderGroups =>
            Choose("グループを並べ替え", "Reorder groups");
        public static string UndoChangeFavorite => Choose("お気に入りを変更", "Change favorite");
        public static string UndoChangeFavorites => Choose("お気に入りを変更", "Change favorites");
        public static string UndoChangeObjectVisibility =>
            Choose("オブジェクトの表示を変更", "Change object visibility");
        public static string UndoChangeObjectLock =>
            Choose("オブジェクトのロックを変更", "Change object lock");
        public static string UndoChangeGroupVisibility =>
            Choose("グループの表示を変更", "Change group visibility");
        public static string UndoChangeGroupLock =>
            Choose("グループのロックを変更", "Change group lock");
        public static string UndoMoveGroup => Choose("グループを移動", "Move group");
        public static string UndoRemoveGroupFromParent =>
            Choose("親グループからグループを外す", "Remove group from parent");
        public static string UndoMoveObjectsToGroup =>
            Choose("オブジェクトをグループへ移動", "Move objects to group");
        public static string MoveToNewSublayer =>
            Choose("新規サブレイヤへ移動", "Move to New Sublayer");
        public static string UndoMoveToNewSublayer =>
            Choose(
                "新規サブレイヤへオブジェクトを移動",
                "Move objects to new sublayer");
        public static string UndoRemoveObjectsFromGroup =>
            Choose("オブジェクトをグループから外す", "Remove objects from group");
        public static string CurrentLayerChangeFailed =>
            Choose(
                "オブジェクトアウトライナーで現在のレイヤを変更できませんでした。",
                "Object Outliner could not set the requested current layer.");

        public static string FormatFilterLayersToolTip(int count)
        {
            return count > 0
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    Choose("レイヤを絞り込み（{0}件有効）", "Filter layers ({0} active)"),
                    count)
                : FilterLayers;
        }

        public static string FormatFilterObjectsToolTip(int count)
        {
            return count > 0
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    Choose("オブジェクトを絞り込み（{0}件有効）", "Filter objects ({0} active)"),
                    count)
                : FilterObjects;
        }

        public static string FormatDisplayModeToolTip(OutlinerDisplayMode mode)
        {
            string name;
            switch (mode)
            {
                case OutlinerDisplayMode.Structure:
                    name = DisplayStructure;
                    break;
                case OutlinerDisplayMode.LayersOnly:
                    name = DisplayLayersOnly;
                    break;
                default:
                    name = DisplayAll;
                    break;
            }

            return string.Format(
                CultureInfo.CurrentCulture,
                Choose("表示内容: {0}", "Displayed contents: {0}"),
                name);
        }

        public static string FormatObjectLayerStatus(int objectCount, int layerCount)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Choose("{0:N0} オブジェクト  |  {1:N0} レイヤ", "{0:N0} objects  |  {1:N0} layers"),
                objectCount,
                layerCount);
        }

        public static string FormatFilteredStatus(
            int shownObjects,
            int totalObjects,
            int shownLayers,
            int totalLayers)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Choose(
                    "{0:N0} / {1:N0} オブジェクト  |  {2:N0} / {3:N0} レイヤ",
                    "{0:N0} / {1:N0} objects  |  {2:N0} / {3:N0} layers"),
                shownObjects,
                totalObjects,
                shownLayers,
                totalLayers);
        }

        public static string FormatDeferredMatchStatus(int matches, int loadedObjects, int totalObjects)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Choose(
                    "{1:N0} / {2:N0} 読み込み済みオブジェクト内の一致: {0:N0}",
                    "{0:N0} matches in {1:N0} loaded / {2:N0} objects"),
                matches,
                loadedObjects,
                totalObjects);
        }

        public static string FormatDeferredStatus(int loadedObjects, int totalObjects)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Choose(
                    "{0:N0} / {1:N0} オブジェクト読み込み済み  |  大規模ドキュメント",
                    "{0:N0} / {1:N0} objects loaded  |  large document"),
                loadedObjects,
                totalObjects);
        }

        public static string FormatHiddenSelectionStatus(int count)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Choose("  |  選択中の非表示項目: {0:N0}", "  |  {0:N0} selected hidden"),
                count);
        }

        public static string FormatDefaultGroupName(int number)
        {
            return IsJapanese
                ? string.Format(CultureInfo.CurrentCulture, "グループ {0}", number)
                : string.Format(CultureInfo.InvariantCulture, "Group {0}", number);
        }

        public static string FormatBlockComponents(int count)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Choose("構成要素 ({0:N0})", "Components ({0:N0})"),
                count);
        }

        public static string FormatBooleanRelationship(
            DerivedNamingRelationship relationship)
        {
            if (relationship == null)
                return string.Empty;

            var symbol = relationship.Kind ==
                    DerivedNamingRelationshipKind.Difference
                ? "-"
                : relationship.Kind ==
                    DerivedNamingRelationshipKind.Intersection
                    ? "&"
                    : "+";
            var right = relationship.AdditionalInputCount == 1 &&
                !string.IsNullOrWhiteSpace(relationship.SecondaryName)
                    ? relationship.SecondaryName.Trim()
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        relationship.Kind ==
                            DerivedNamingRelationshipKind.Difference
                            ? Choose("差分{0:N0}個", "{0:N0} cutters")
                            : Choose("{0:N0}オブジェクト", "{0:N0} objects"),
                        relationship.AdditionalInputCount);
            return string.Format(
                CultureInfo.CurrentCulture,
                "{0} {1} {2}",
                relationship.PrimaryName,
                symbol,
                right);
        }

        public static string AppendCopySuffix(string sourceName)
        {
            return (sourceName ?? string.Empty).Trim() + Choose("のコピー", " Copy");
        }

        public static string FormatNumberedCopyName(string sourceName, int number)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                "{0}.{1:000}",
                AppendCopySuffix(sourceName),
                number);
        }

        public static string LocalizeLinetypeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Continuous;
            switch (name.ToLowerInvariant())
            {
                case "continuous": case "実線": return Continuous;
                case "dashed": case "破線": return Choose("破線", "Dashed");
                case "dots": case "点線": return Choose("点線", "Dots");
                case "center": case "中心線": return Choose("中心線", "Center");
                case "border": case "境界線": return Choose("境界線", "Border");
                case "dashdot": case "一点鎖線": return Choose("一点鎖線", "DashDot");
                case "hidden": case "隠れ線": return Choose("隠れ線", "Hidden");
                default: return name;
            }
        }
    }
}

# AGENTS (引継ぎ資料)

## 概要

本ディレクトリは、YamaPlayer playlist JSON と TSV を相互変換する Unity Editor ツールの実装です。

- 実装本体:
  - `Editor/YamaPlayerPlaylistJsonTsvConverterWindow.cs`
- 仕様書:
  - `SPEC.md`
- 利用者向け説明:
  - `README.md`

## 現在の仕様要点

- TSV は 7 列固定（`playlist_index`/`track_index` は不使用）
- TSV 行順で playlist 順・track 順を再構築
- URL は不要文字削除で正規化
- URL 形式外は警告扱いで許容
- zero-track playlist を許容
- `TSV -> JSON` 後に Input/Output 差分を Result 表示

## 主な実装ポイント

- 変換処理
  - `BuildTsv(...)`
  - `ParseTsv(...)`
  - `ParseDataLine(...)`
- 正規化
  - `NormalizeUrl(...)`
  - `NormalizeTextForTsv(...)`
  - `NormalizeTextFromTsv(...)`
- 差分
  - `AppendInputOutputDiff(...)`
  - `ComparePlaylistRoots(...)`
- WinMerge
  - `OpenDiffWithWinMerge()`
  - `InstallWinMergeViaWinget()`
  - `FindWinMergeExecutable()`

## 運用メモ

- エラー/警告判定は Result を参照
- 差分詳細は最大 200 件
- Windows 以外では WinMerge ボタンは実質利用不可

## 引継ぎ時チェック項目

- Unity Editor でコンパイルエラーがないこと
- `JSON -> TSV -> JSON` 往復が成功すること
- URL 正規化の期待動作を満たすこと
- 差分表示が意図通り出ること
- WinMerge 連携ボタンの挙動（Windows）

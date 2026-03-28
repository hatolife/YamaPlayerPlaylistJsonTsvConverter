# YamaPlayerPlaylistJsonTsvConverter ユーザーガイド

## 1. 概要

このツールは、YamaPlayer のプレイリスト JSON を TSV に変換し、
表計算ソフトで編集した TSV を JSON に戻すための Unity Editor ツールです。

## 2. 起動方法

Unity メニューから以下を選択します。

- `Tools/PoppoWorks/YamaPlayer Playlist Json TSV Converter`

## 3. 画面構成

上から順に次の入力欄があります。

- `Input JSON`
- `Output TSV`
- `Output JSON`

`Output JSON` には `Input +1` ボタンがあります。

- 例: `playlists_1.json` を入力しているとき、`playlists_2.json` を自動設定
- 末尾連番がない場合は `_1` を付与

## 4. 基本操作

### 4.1 JSON -> TSV

1. `Input JSON` に元の JSON を指定
2. `Output TSV` に出力先を指定
3. `JSON -> TSV` を押す
4. `Result` で完了/警告/エラーを確認

### 4.2 TSV -> JSON

1. `Output TSV` に読み込む TSV を指定
2. `Output JSON` に JSON 出力先を指定
3. `TSV -> JSON` を押す
4. `Result` で完了/警告/エラーを確認
5. 同時に `Input JSON` と `Output JSON` の差分要約が表示されます

## 5. TSV 仕様（重要）

ヘッダは固定です。

```text
playlist_name\tplaylist_active\tyoutube_list_id\tplaylist_is_edit\ttrack_mode\ttrack_title\ttrack_url
```

- `playlist_index` / `track_index` は使いません
- playlist 順・track 順は TSV の行順で決まります
- 連続行の playlist メタが同じ間は同じ playlist として扱われます

## 6. データの扱い

- 空文字は許容
- `Tracks` 0件 playlist は許容
- URL 形式外は警告（取り込みは継続）
- URL は不要文字（空白・tab・改行・制御文字）を削除して正規化

## 7. Result の見方

- `Errors`: 変換失敗要因。出力は信用しない
- `Warnings`: 変換は実行されたが注意が必要
- `Diff`: `TSV -> JSON` 実行時の入力/出力差分

`Result` は選択してコピーできます。

## 8. WinMerge 連携（Windows）

- `Diff with WinMerge`: `Input JSON` と `Output JSON` を WinMerge で比較
- `Install WinMerge (winget)`: winget で WinMerge を導入

インストール確認ダイアログから、公式サイトを開いて手動インストールも可能です。

- 公式サイト: https://winmerge.org/?lang=ja

## 9. よくあるトラブル

### 9.1 ヘッダ不一致

- TSV の列名・順序が固定ヘッダと一致しているか確認してください。

### 9.2 bool/int 変換エラー

- `playlist_active` / `playlist_is_edit` は `true`/`false`（または `1`/`0`）
- `track_mode` は整数

### 9.3 意図しない差分が出る

- TSV で行の並びを変更すると playlist/track の順序が変わります。
- `Diff` セクションで変更内容を確認してください。

## 10. 運用の推奨

- 元 JSON は残し、`Output JSON` は別名で出力して比較する
- `TSV -> JSON` 後に必ず `Diff` を確認する
- 大きな変更前に JSON をバックアップする

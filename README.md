# YamaPlayerPlaylistJsonTsvConverter

YamaPlayer のプレイリスト JSON と TSV を相互変換する Unity Editor ツールです。

## このツールの利点

- YamaPlayer 本体にも JSON の出力機能はありますが、そのままでは「見やすい・編集しやすい」形式とは言いにくいです。
- 本ツールで TSV 化することで、プレイリストをテキストベースかつ表形式で管理できます。
- TSV なら Googleスプレッドシートや Excel で、一覧編集・検索・並び替え・フィルタがしやすくなります。
- JSON/TSV の相互変換により、編集は表計算ソフト、反映は JSON という運用に分離できます。
- 出力 JSON の差分確認機能により、意図した変更かどうかを確認しやすくなります。

## 機能

- JSON -> TSV 変換
- TSV -> JSON 変換
- 中断可能プログレスバー
- Result のコピー可能表示
- `Input JSON` と `Output JSON` の差分要約表示
- WinMerge 連携（Windows）
  - `Diff with WinMerge`
  - `Install WinMerge (winget)`

## UI

- `Input JSON`
- `Output TSV`
- `Output JSON`
  - `Input +1` ボタンで連番を自動設定
    - 例: `playlists_1.json -> playlists_2.json`
    - 末尾連番なしは `_1` を付与

## TSV 形式

ヘッダ（固定）:

```text
playlist_name\tplaylist_active\tyoutube_list_id\tplaylist_is_edit\ttrack_mode\ttrack_title\ttrack_url
```

- `playlist_index` / `track_index` は使用しません
- playlist 順・track 順は TSV の行順を採用します

## データ方針

- 空文字は許容
- `Tracks` 0件 playlist は許容
- URL 形式外は警告で許容
- URL は不要文字（空白・tab・改行・制御文字）を削除して正規化

## 既知の互換処理

- 旧不整合TSV（ヘッダ7列なのにデータ先頭に旧 `playlist_index` が残る8列）を読み込んだ場合、先頭列を自動で無視します。

## 使い方（基本）

1. `Input JSON` を選択
2. `Output TSV` / `Output JSON` を設定
3. `JSON -> TSV` または `TSV -> JSON` を実行
4. `Result` で警告・エラー・差分を確認

## 注意

- 実行結果は `Result` の内容を優先して確認してください。
- 差分詳細は最大 200 件まで表示します。

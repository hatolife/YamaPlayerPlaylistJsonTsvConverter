# YamaPlayerPlaylistJsonTsvConverter

YamaPlayer のプレイリスト JSON と TSV を相互変換する Unity Editor ツールです。

## このツールで得られること

- プレイリストを TSV（表形式）で扱えるため、内容の把握と一括編集がしやすくなります。
- Googleスプレッドシート / Excel で、検索・並び替え・フィルタを使ったメンテナンスができます。
- 編集作業（TSV）と反映データ（JSON）を分離でき、運用手順を整理しやすくなります。
- `Input JSON` と `Output JSON` の差分を確認できるため、変更内容の妥当性を検証しやすくなります。

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
  - `Output JSON` が空欄の状態で `Input JSON` を選択した場合も、同じ連番ルールで自動設定

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

## 使い方（基本）

1. `Input JSON` を選択
2. `Output TSV` / `Output JSON` を設定
3. `JSON -> TSV` または `TSV -> JSON` を実行
4. `Result` で警告・エラー・差分を確認

## 注意

- 実行結果は `Result` の内容を優先して確認してください。
- 差分詳細は最大 200 件まで表示します。
- `WinMerge` での確認を推奨します。

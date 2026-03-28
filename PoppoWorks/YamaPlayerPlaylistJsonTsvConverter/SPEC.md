# SPEC

## 1. 対象

- 入力 JSON: YamaPlayer playlist JSON
- 中間/編集形式: TSV
- 出力 JSON: YamaPlayer playlist JSON

## 2. JSON スキーマ

### 2.1 Root

```json
{
  "playlists": [Playlist]
}
```

### 2.2 Playlist

```json
{
  "Active": true,
  "Name": "string",
  "Tracks": [Track],
  "YoutubeListId": "string",
  "IsEdit": false
}
```

### 2.3 Track

```json
{
  "Mode": 1,
  "Title": "string",
  "Url": "string"
}
```

## 3. TSV スキーマ

ヘッダ固定:

```text
playlist_name\tplaylist_active\tyoutube_list_id\tplaylist_is_edit\ttrack_mode\ttrack_title\ttrack_url
```

列定義:

- `playlist_name` : string
- `playlist_active` : bool (`true`/`false`, `1`/`0` も許容)
- `youtube_list_id` : string
- `playlist_is_edit` : bool (`true`/`false`, `1`/`0` も許容)
- `track_mode` : int
- `track_title` : string
- `track_url` : string

## 4. 変換仕様

### 4.1 JSON -> TSV

- 1 track = 1行
- playlistメタは各行に重複保持
- `Tracks` 0件 playlist は `track_*` 列を空にして 1行出力
- 出力順は JSON 配列順を維持

### 4.2 TSV -> JSON

- TSV 行順を採用して再構築
- 連続行の playlist メタが同一の間は同一 playlist に追加
- playlist メタが変わった時点で次 playlist を開始
- `track_mode/title/url` がすべて空なら zero-track 行として扱う

## 5. バリデーション

エラー:

- ヘッダ不一致
- 必須列不足
- bool/int 変換不能

警告:

- URL 正規化が発生
- URL 形式外（`http://`/`https://` で始まらない）
- 旧不整合TSVの先頭 `playlist_index` 列を無視した場合

許容:

- 空文字
- `Tracks` 0件 playlist

## 6. 文字列正規化

- `track_url`
  - 空白・tab・改行・制御文字を削除
- `track_title` / `playlist_name` / `youtube_list_id`
  - tab・改行を半角スペースへ置換

## 7. UI / UX

- フィールド順
  - `Input JSON`
  - `Output TSV`
  - `Output JSON`
- ボタン
  - `JSON -> TSV`
  - `TSV -> JSON`
  - `Output JSON: Input +1`
  - `Diff with WinMerge` (Windows)
  - `Install WinMerge (winget)` (Windows)
- 自動補完
  - `Output JSON` が空欄の状態で `Input JSON` を選択した場合、`Input +1` と同じ連番ルール（末尾番号を +1 / 番号なしは `_1`）で自動設定する

## 8. 差分表示

- `TSV -> JSON` 成功時に `Input JSON` と `Output JSON` を比較
- 集計
  - Added/Removed playlists
  - Changed playlist meta
  - Added/Removed tracks
  - Changed tracks
- 詳細は最大 200 件表示

## 9. 進捗と中断

- 変換中は中断可能プログレスバーを表示
- キャンセル時は処理を安全に停止し Result に記録
- 終了時は必ずプログレスバーをクリア

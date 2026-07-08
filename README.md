# FileShare

自分のPC(SSD/HDD)内のファイル・フォルダを、インターネット経由で公開して
高速にダウンロードできるようにするWindowsデスクトップアプリです。

- **GUI**: WPF (.NET 10)。Apple Human Interface Guidelines を参照した、
  ライトカラー・ワンアクセントカラーの落ち着いたデザイン([DESIGN.md](DESIGN.md)参照)。
- **公開方式**: [Cloudflare Tunnel](https://github.com/cloudflare/cloudflared)
  のクイックトンネル。アカウント登録・ポート開放不要で、`https://xxxx.trycloudflare.com`
  というURLが自動発行されます。
- **高速・レジューム対応ダウンロード**: HTTP Range リクエストに対応しているため、
  ダウンロードの再開や、ダウンロードマネージャー(IDM/aria2等)による並列分割
  ダウンロードが可能です。
- **アクセス保護**: Basic認証(ユーザー名・パスワード)。初回起動時にパスワードを
  自動生成し、GUIから確認・再生成できます。

## 使い方

1. `FileShare.sln` を Visual Studio で開くか、`dotnet run --project src/FileShare`
   で起動します。
2. 「+ ファイルを追加」「+ フォルダを追加」、またはクイック追加チップ
   (season file / dll / soft / clip)から共有したいものを選びます。
3. 「共有を開始」を押すと、ローカルサーバーが起動し、Cloudflare Tunnel経由で
   公開URLが発行されます(初回はcloudflared.exeの自動ダウンロードが走ります)。
4. 発行されたURL・QRコードを共有相手に伝えます。パスワード保護はデフォルトで
   有効です(オフにもできます)。
5. 「共有を停止」でサーバーとトンネルを終了します。

## 内部構成

- `Services/ShareServerService.cs` — 埋め込みKestrelサーバー。`/`(一覧)、
  `/dl/{id}`(ファイル直DL)、`/browse/{id}/{path}`(フォルダ閲覧)、
  `/file/{id}/{path}`(フォルダ内ファイルDL、Range対応)、
  `/zip/{id}/{path}`(フォルダをZIPでストリーミングDL)を提供。
  すべて `127.0.0.1` のみにバインドし、外部公開はTunnelService経由のみ。
- `Services/TunnelService.cs` — cloudflared.exe の取得・起動・URL捕捉・停止。
- `Services/QrCodeService.cs` — QRCoderで公開URLをQRコード化。
- `Services/ConfigService.cs` — `%AppData%\FileShare\config.json` に
  共有アイテム・認証情報を永続化。

## 注意事項

- 公開中は共有アイテムの追加・削除ができません(先に「共有を停止」してください)。
- クイックトンネル(`trycloudflare.com`)は再起動のたびにURLが変わります。
  固定ドメインが必要な場合は、Cloudflareアカウントで名前付きトンネルを作成し、
  `TunnelService` の起動引数を差し替えてください。

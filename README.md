# FileShare

自分のPC(SSD/HDD)内のファイル・フォルダを、インターネット経由で公開して
高速にダウンロードできるようにするWindowsデスクトップアプリです。

- **GUI**: WPF (.NET 10)。Apple Human Interface Guidelines を参照した、
  ライトカラー・ワンアクセントカラーの落ち着いたデザイン([DESIGN.md](DESIGN.md)参照)。
- **公開方式は2種類から選択可能**:
  - **毎回変わるURL**([Cloudflare](https://github.com/cloudflare/cloudflared) クイックトンネル) —
    アカウント登録・ポート開放不要。`https://xxxx.trycloudflare.com` が毎回自動発行される。
  - **固定URL**([Tailscale Funnel](https://tailscale.com/kb/1223/funnel)) —
    無料のTailscaleアカウントが必要だが、`https://端末名.ターン別名.ts.net` が
    再起動しても変わらない。
- **高速・レジューム対応ダウンロード**: HTTP Range リクエストに対応しているため、
  ダウンロードの再開や、ダウンロードマネージャー(IDM/aria2等)による並列分割
  ダウンロードが可能です。
- **アイテムごとの固有リンク**: 共有中は各ファイル・フォルダの「リンクをコピー」から、
  そのアイテムだけを指す直リンクをコピーできます。
- **アクセス保護**: Basic認証(ユーザー名・パスワード)。初回起動時にパスワードを
  自動生成し、GUIから確認・再生成できます。
- **インストーラー・自動アップデート**: [Velopack](https://velopack.io) でパッケージング。
  `Setup.exe` でインストールすると、起動のたびに
  [GitHub Releases](https://github.com/4uxxx/FileShare/releases) をチェックし、
  新バージョンがあれば確認ダイアログを出して自動更新します。
- **バックグラウンド常駐**: ウィンドウを閉じても(設定でオフにしない限り)アプリは
  終了せず、通知領域(タスクトレイ)に常駐して共有を継続します。トレイアイコンの
  「開く」または再度アイコンをクリックすると復帰します。
- **エクスプローラー右クリックメニュー**: ファイル・フォルダを右クリック→
  「FileShareで共有」で、そのアイテムを共有リストに即座に追加できます
  (フォルダ内の何もない場所を右クリックした場合は、そのフォルダ自体が対象になります)。

## 使い方(インストーラー版)

1. [Releases](https://github.com/4uxxx/FileShare/releases/latest) から
   `FileShare-win-Setup.exe` をダウンロードして実行します。
2. 「+ ファイルを追加」「+ フォルダを追加」、またはクイック追加チップ
   (season file / dll / soft / clip)から共有したいものを選びます。
3. 「公開方法」で毎回変わるURL(Cloudflare)か固定URL(Tailscale)かを選びます。
   固定URLを初めて使う場合は、Tailscaleへのログインが必要になるとアプリ内に
   ログインURLが表示されるので、ブラウザで開いて認証してください
   (Funnel機能が未許可の場合は、案内されるURLでテナントごとに一度だけ有効化が必要です)。
4. 「共有を開始」を押すと、ローカルサーバーが起動し、選んだ方式で公開URLが発行されます
   (Cloudflareモードは初回のみ cloudflared.exe の自動ダウンロードが走ります)。
5. 発行されたURL・QRコードを共有相手に伝えます。パスワード保護はデフォルトで有効です。
6. 「共有を停止」でサーバーとトンネルを終了します。

## 開発者向け(ソースから実行)

```
dotnet run --project src/FileShare
```

## リリースの作り方

```
.\pack.ps1 -Version 1.0.1
```

`dotnet publish` → `vpk pack`(Setup.exe生成)→ `vpk upload github`(GitHub Releasesへ公開)
を一括実行します。`gh auth login` で認証済みであることが前提です。

## 内部構成

- `Services/ShareServerService.cs` — 埋め込みKestrelサーバー。`/`(一覧)、
  `/dl/{id}`(ファイル直DL)、`/browse/{id}/{path}`(フォルダ閲覧)、
  `/file/{id}/{path}`(フォルダ内ファイルDL、Range対応)、
  `/zip/{id}/{path}`(フォルダをZIPでストリーミングDL)を提供。
  すべて `127.0.0.1` のみにバインドし、外部公開はTunnelService経由のみ。
- `Services/TunnelService.cs` — `AppConfig.TunnelMode` に応じて
  `CloudflareTunnelProvider` / `TailscaleFunnelProvider` を切り替えるディスパッチャ。
- `Services/CloudflareTunnelProvider.cs` — cloudflared.exe の取得・起動・URL捕捉・停止。
- `Services/TailscaleFunnelProvider.cs` — tailscale.exe のログイン状態確認・
  ログインURL捕捉・`tailscale funnel --bg` の起動・固定DNS名の取得。
- `Services/QrCodeService.cs` — QRCoderで公開URLをQRコード化。
- `Services/ConfigService.cs` — `%AppData%\FileShare\config.json` に
  共有アイテム・認証情報・公開方式を永続化。
- `Program.cs` — `VelopackApp.Build().Run()` を最初に実行するカスタムエントリポイント。
- `App.xaml.cs` — 起動時にGitHub Releasesへ自動アップデートを確認。

## 注意事項

- 公開中は共有アイテムの追加・削除ができません(先に「共有を停止」してください)。
- Cloudflareのクイックトンネル(`trycloudflare.com`)は再起動のたびにURLが変わります。
  固定URLが必要な場合は「固定URL (Tailscale)」を選んでください。
- Tailscale Funnelは無料アカウントで使えますが、初回のみブラウザでのログインと
  (未許可の場合)管理画面でのFunnel機能の有効化が必要です。

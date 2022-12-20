# TweetCollectionCrawler.NET ![GitHub release (latest by date)](https://img.shields.io/github/v/release/rexent-gx/TweetCollectionCrawler.NET?color=green) ![GitHub Release Date](https://img.shields.io/github/release-date/rexent-gx/TweetCollectionCrawler.NET)  
任意のTwitter Collectionに追加されている画像・動画を落とせるだけ落とすクローラーです

## 使い方
Releaseページから各OS用のビルド済みファイルがダウンロードできます。`TCCrawler`（Windowsの場合は`TCCrawler.exe`）がプログラム本体です。  
動作にはJSON設定ファイルが必要です。書式については`settings.sample.json`を参照してください。

引数`-i`にコレクションのID(`https://twitter.com/hogehoge/timelines/1234567890123456789`の`1234567890123456789`部分)を渡すことで、指定したコレクション以下のメディアをダウンロードします。  
その他に`-o`で出力先ディレクトリ(デフォルト`./fetched/`)、`-s`で設定ファイル(デフォルト`settings.json`)を指定できます。

使用にはTwitter APIの`API Key`・`API Secret`・`Access Token`・`Access Secret`の発行が必要です。  
コレクションの取得に`Twitter API Standard v1.1`、メディアの取得に`Twitter API v2`を使用しています。その為、双方のキー群か、アクセスレベルが`Elevated`な`Twitter API v2`のキー群が必要です。上記キー群はJSON設定ファイルに記述することで読み込まれます。  
`settings.json`に適切なHTTPヘッダーを指定すると、`Twitter API Standard v1.1`が必要な部分をHttpClientにて行います。必要なヘッダーは以下のとおりです。
- Authorization
- Cookie
- x-csrf-token

上記トークンを使用したいアカウントでログイン済みのブラウザ等から取得し、JSON設定ファイルの`ClientHeaders`セクションに設定してください。

`TCCrawler -h`でヘルプが表示されるので詳しくはそちらを参照してください。


## 注意事項
- Collectionの仕様っぽいですが、先頭から約700ツイート以前のツイートは取得できません。


## ビルド
.NET SDKまたはDockerが使用できる環境ではビルドすることも可能です。

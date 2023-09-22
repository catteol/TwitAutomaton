# TwitAutomaton  ![GitHub release (latest by date)](https://img.shields.io/github/v/release/rexent-gx/TwitAutomaton?color=green) ![GitHub Release Date](https://img.shields.io/github/release-date/rexent-gx/TwitAutomaton)  
Twitter（現X）自動化ツールです。個人的な需要に応じて機能は拡充予定です。APIを使用せず使用者のアカウントを用いたWebクライアントとして動作するため、使用は自己責任でお願いします。

## 機能
- 指定したTwitter Collectionに追加されている画像・動画を落とせるだけ落とします。

## 使い方
Releaseページから各OS用のビルド済みファイルがダウンロードできます。`TCCrawler`（Windowsの場合は`TCCrawler.exe`）がプログラム本体です。  
動作にはJSON設定ファイルが必要です。書式については`settings.sample.json`を参照してください。

引数`-i`にコレクションのID(`https://twitter.com/hogehoge/timelines/1234567890123456789`の`1234567890123456789`部分)を渡すことで、指定したコレクション以下のメディアをダウンロードします。  
その他に`-o`で出力先ディレクトリ(デフォルト`./fetched/`)、`-s`で設定ファイル(デフォルト`settings.json`)を指定できます。

以前は`Twitter API Standard v1.1`及び`Twitter API v2`Project等が必要でしたが、クソみたいな料金プランと制限への以降、v1.1APIの停止に伴い、APIを使用せずWebClientとしてGraphQLを叩くように変更しました。ブラウザからのリクエスト同様のヘッダーとクエリパラメータでGETリクエストを飛ばしています。  
動作には`Authorization` `Cookie` `XCSRFToken`のHTTPヘッダーが必要となりますが、適当なブラウザの開発者ツールを使用して要求HTTPヘッダーから抜き出してください。  
Headlessブラウザでログインから行う実装がベターなのかもしれませんが、面倒だったので要望があればやります。

`TCCrawler -h`でヘルプが表示されるので詳しくはそちらを参照してください。

Collectionもいつまで使えるか分かりませんね…


## 注意事項
- Collectionの仕様っぽいですが、先頭から約700ツイート以前のツイートは取得できません。


## ビルド
.NET SDKまたはDockerが使用できる環境ではビルドすることも可能です。

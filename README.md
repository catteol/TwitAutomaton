# TweetCollectionCrawler.NET ![GitHub release (latest by date)](https://img.shields.io/github/v/release/rexent-gx/TweetCollectionCrawler.NET?color=green) ![GitHub Release Date](https://img.shields.io/github/release-date/rexent-gx/TweetCollectionCrawler.NET)  
任意のTwitter Collectionに追加されている画像・動画を落とせるだけ落とすクローラーです

## 使い方
Releaseページから各OS用のビルド済みファイルがダウンロードできます。`TCCrawler`（Windowsの場合は`TCCrawler.exe`）がプログラム本体です。  

Collection APIを叩くにはユーザー認証が必要なので、[TwitterDeveloperPortal](https://developer.twitter.com/)で`Consumer Keys`と`Access Tokens`を発行する必要があります。

    TCCrawler -k <Consumer Keys> -s <Consumer Secret> -t <Access Token> -a <Access Secret> -i <Collection ID> -o <Destination Path>

`TCCrawler -h`でヘルプが表示されるので詳しくはそちらを参照してください。

## 注意事項
- Collectionの仕様っぽいですが、先頭から約700ツイート以前のツイートは取得できません。
  - 古に登録した数多くのツイートがどうやっても掘れず、血の涙を流しながら呪詛を唱えました
- Collectionに画像付きツイートしか存在しないパターンでしか動作を検証していません。動画とか文字だけとかのツイートが混ざっている場合のエラーハンドリングできていないかもしれません。

## ビルド
.NET SDKまたはDockerが使用できる環境ではビルドすることも可能です。

WIP

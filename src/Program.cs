using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoreTweet;
using System.Net;
using Kurukuru;
using CommandLine;
using CommandLine.Text;

namespace TCCrawler
{
    class Options
    {
        [Option('k', "consumerkey", Required = true, HelpText = "Twitter API ConsumerKey.")]
        public string? ConsumerKey { get; set; }


        [Option('s', "consumersecret", Required = true, HelpText = "Twitter API ConsumerSecret.")]
        public string? ConsumerSecret { get; set; }

        [Option('t', "accesstoken", Required = true, HelpText = "Twitter API AccessToken.")]
        public string? AccessToken { get; set; }

        [Option('a', "accesssecret", Required = true, HelpText = "Twitter API AccessSecret.")]
        public string? AccessSecret { get; set; }

        [Option('i', "id", Required = true, HelpText = "Twitter Collection ID. Only ID digits, no need 'custom-'.")]
        public string CollectionID { get; set; } = string.Empty;

        [Option('o', "out", Required = true, HelpText = "Directory path to save images.")]
        public string SavePath { get; set; } = string.Empty;
    }


    class Program
    {
        public class TargetUrl
        {
            public string Url { get; set; } = string.Empty;
            public string ScreenName { get; set; } = string.Empty;
            public long TweetId { get; set; } = 0;
        }

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            var result = (ParserResult<Options>)Parser.Default.ParseArguments<Options>(args);
            if (result.Tag == ParserResultType.Parsed)
            {
                var parsed = (Parsed<Options>)result;

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                Tokens tokens = Tokens.Create(parsed.Value.ConsumerKey, parsed.Value.ConsumerSecret, parsed.Value.AccessToken, parsed.Value.AccessSecret);

                DBController.ExecuteNoneQuery("CREATE TABLE IF NOT EXISTS tweets(id integer not null primary key, url text);");

                (TargetUrl[] mediaUrls, string[] executeSqls) = await getAllMediaUrlsAsync(tokens, parsed.Value.CollectionID);
                await downloadImage(mediaUrls, parsed.Value.SavePath);

                DBController.ExecuteNoneQueryWithTransaction(executeSqls);
            }
            else
            {
                // var notParsed = (NotParsed<Options>)result;
            }
        }

        static async Task<(TargetUrl[], string[])> getAllMediaUrlsAsync(Tokens tokens, string collectionId)
        {
            List<TargetUrl> mediaUrls = new List<TargetUrl>();
            List<string> executeSqls = new List<string>();

            async Task<CollectionEntriesPosition> getMediasWithCursorAsync(CollectionEntriesPosition? pos = null)
            {
                CollectionEntriesResult result;

                if (pos == null) { result = await tokens.Collections.EntriesAsync($"custom-{collectionId}", 150, null, null, TweetMode.Extended); }
                else
                {
                    result = await tokens.Collections.EntriesAsync($"custom-{collectionId}", 150, pos.MinPosition, null, TweetMode.Extended);

                    if (result.Entries.Length == 0)
                    {
                        await Task.Delay(3000);
                        return pos;
                    }
                }

                List<object[]> sqlRes = DBController.ExecuteReader($"SELECT * FROM tweets");
                List<long> recordedIds = new List<long>();

                sqlRes.ForEach(res =>
                {
                    recordedIds.Add((long)res[0]);
                });

                foreach (var entry in result.Entries)
                {
                    // Exclude recorded tweet
                    if (!recordedIds.Contains(entry.Tweet.Id))
                    {
                        foreach (var media in entry.Tweet.ExtendedEntities.Media)
                        {
                            mediaUrls.Add(new TargetUrl() { Url = media.MediaUrlHttps, ScreenName = entry.Tweet.User.ScreenName, TweetId = entry.Tweet.Id });
                        }
                        executeSqls.Add($"INSERT INTO tweets VALUES({entry.Tweet.Id}, 'https://twitter.com/{entry.Tweet.User.ScreenName}/status/{entry.Tweet.Id}')");
                    }
                }

                return (result.Position);
            }

            await Spinner.StartAsync("Fetching image URLs...", async spinner =>
            {
                CollectionEntriesPosition res = await getMediasWithCursorAsync();
                while (res.WasTruncated)
                {
                    await Task.Delay(500);
                    res = await getMediasWithCursorAsync(res);
                    spinner.Text = "Fetching " + mediaUrls.Count + " URLs...";
                }

                spinner.Succeed("Fetched " + mediaUrls.Count + " URLs.");
            });

            return (mediaUrls.ToArray(), executeSqls.ToArray());
        }

        static async Task downloadImage(TargetUrl url, string savePath, Spinner spinner, HttpClient httpClient, int index, DateTime dt)
        {
            // string filePath = $"{savePath}/{url.ScreenName}_{url.TimeStamp}_{index}_{Path.GetFileName(new Uri(url.Url).AbsolutePath)}";
            // To get timestamp from ID, use:
            // (entry.Tweet.Id >> 22) + (long)1288834974657
            string filePath = $"{savePath}/{url.ScreenName}_{url.TweetId}_{index}{Path.GetExtension(new Uri(url.Url).AbsolutePath)}";
            if (File.Exists(filePath))
            {
                spinner.Text = ($"Downloading {Path.GetFileName(filePath)}");
                return;
            }

            using (var res = await httpClient.GetAsync(url.Url + ":orig", HttpCompletionOption.ResponseHeadersRead))
            using (var fileStream = File.Create(filePath))
            using (var httpStream = await res.Content.ReadAsStreamAsync())
            {
                await httpStream.CopyToAsync(fileStream);
            }
            spinner.Text = ($"Downloading {Path.GetFileName(filePath)}");

            TimeSpan ts = new TimeSpan(0, 0, index);
            File.SetCreationTime(filePath, dt - ts);
            File.SetLastWriteTime(filePath, dt - ts);
            File.SetLastAccessTime(filePath, dt - ts);
        }

        static async Task downloadImage(TargetUrl[] urls, string savePath)
        {
            DateTime dt = DateTime.Now;

            await Spinner.StartAsync("Start downloading...", async spinner =>
            {
                using (var httpClient = new HttpClient(new HttpClientHandler
                {
                    MaxConnectionsPerServer = 25
                }))
                {
                    await Task.WhenAll(urls.Select((url, i) => downloadImage(url, savePath, spinner, httpClient, i, dt)));
                    spinner.Succeed("Process finished successfully.");
                }

            });
        }
    }
}
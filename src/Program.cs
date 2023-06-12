using System.Text;
using System.Text.Json;
using CoreTweet;
using Tweetinvi;
using System.Net;
using Kurukuru;
using CommandLine;

namespace TCCrawler
{
  class Options
  {
    [Option('s', "settings", Required = false, HelpText = "Settings JSON file path. Default is './settings.json'.")]
    public string SettingsFilePath { get; set; } = "./settings.json";

    [Option('i', "id", Required = true, HelpText = "Twitter Collection ID. Only ID digits, no need 'custom-'.")]
    public string CollectionID { get; set; } = string.Empty;

    [Option('o', "out", Required = false, HelpText = "Directory path to save images. Default is './fetched/'.")]
    public string SavePath { get; set; } = "./fetched/";
  }

  class Settings
  {
    public APIKeys? V1 { get; set; }
    public APIKeys? V2 { get; set; }

    public ClientHeaders? ClientHeaders { get; set; }
  }

  class APIKeys
  {
    public string APIKey { get; set; } = string.Empty;
    public string APIKeySecret { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string AccessTokenSecret { get; set; } = string.Empty;
  }

  class ClientHeaders
  {
    public string Authorization { get; set; } = string.Empty;
    public string Cookie { get; set; } = string.Empty;
    public string XCSRFToken { get; set; } = string.Empty;
  }

  class TweetMedia
  {
    public long TweetId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string[] Medias { get; set; } = new string[0];
  }

  class MediaPath
  {
    public string Url { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
  }

  class Program
  {
    static async Task Main(string[] args)
    {
      Console.OutputEncoding = Encoding.UTF8;

      var result = (ParserResult<Options>)Parser.Default.ParseArguments<Options>(args);
      if (result.Tag == ParserResultType.Parsed)
      {
        var parsed = result as Parsed<Options>;
        if (parsed == null)
        {
          throw new Exception("Failed to parse command arguments.");
        }

        Settings? settings = new Settings();

        // Load settings json
        try
        {
          using (StreamReader reader = File.OpenText(parsed.Value.SettingsFilePath))
          {
            while (!reader.EndOfStream)
            {
              settings = JsonSerializer.Deserialize<Settings>(reader.ReadToEnd()) ?? new Settings();
              if (settings.V1 == null || settings.V1.APIKey == "")
              {
                settings.V1 = settings.V2;
              }
            }
          }
        }
        catch (System.Exception e)
        {
          Console.WriteLine(e);
          throw new Exception("Failed to load settings.");
        }

        // Create dest directory
        Directory.CreateDirectory(parsed.Value.SavePath);

        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
        Tokens coreTokens = Tokens.Create(settings.V1?.APIKey, settings.V1?.APIKeySecret, settings.V1?.AccessToken, settings.V1?.AccessTokenSecret);
        TwitterClient inviClient = new TwitterClient(settings.V2?.APIKey, settings.V2?.APIKeySecret, settings.V2?.AccessToken, settings.V2?.AccessTokenSecret);
        if (settings.ClientHeaders != null)
          Console.WriteLine("Found 'ClientHeaders' settings. Fetching tweets by HttpClient instead of Twitter API.");

        DBController.ExecuteNoneQuery("CREATE TABLE IF NOT EXISTS tweets(id integer not null primary key, url text);");

        long[] tweetIds = await getAllTweetIdsAsync(coreTokens, parsed.Value.CollectionID, settings.ClientHeaders);
        TweetMedia[] tweetMedias = await getAllMediaUrlsAsync(tweetIds, inviClient, coreTokens, settings.ClientHeaders);
        await downloadMediasAsync(tweetMedias, parsed.Value.SavePath);

        // Update DB
        var sqlQueries = new List<string>();
        foreach (var tweetMedia in tweetMedias)
        {
          sqlQueries.Add($"INSERT INTO tweets VALUES({tweetMedia.TweetId}, 'https://twitter.com/{tweetMedia.UserName}/status/{tweetMedia.TweetId}')");
        }
        DBController.ExecuteNoneQueryWithTransaction(sqlQueries.ToArray());
      }
      else
      {
        throw new Exception("Failed to parse command arguments.");
      }
    }

    static async Task<long[]> getAllTweetIdsAsync(Tokens coreTokens, string collectionId, ClientHeaders? clientHeaders)
    {
      List<long> tweetIds = new List<long>();

      async Task<CollectionEntriesPosition> getIdsWithCursorAsync(CollectionEntriesPosition? pos = null)
      {
        CollectionEntriesResult result;

        // Collections API
        if (pos == null)
        {
          result = await coreTokens.Collections.EntriesAsync($"custom-{collectionId}", 150, null, null, CoreTweet.TweetMode.Extended);
        }
        else
        {
          result = await coreTokens.Collections.EntriesAsync($"custom-{collectionId}", 150, pos.MinPosition, null, CoreTweet.TweetMode.Extended);

          if (result.Entries.Length == 0)
          {
            await Task.Delay(3000);
            return pos;
          }
        }

        // Make saved tweets collection
        List<object[]> sqlRes = DBController.ExecuteReader($"SELECT * FROM tweets");
        List<long> savedIds = new List<long>();

        sqlRes.ForEach(res =>
        {
          savedIds.Add((long)res[0]);
        });


        foreach (var entry in result.Entries)
        {
          // Exclude recorded tweet
          if (!savedIds.Contains(entry.Tweet.Id))
          {
            tweetIds.Add(entry.Tweet.Id);
          }
        }

        return (result.Position);
      }

      async Task<CollectionEntriesPosition> getIdsByClientAsync(ClientHeaders clientHeaders, CollectionEntriesPosition? pos = null)
      {
        System.Text.Json.Nodes.JsonArray result;
        CollectionEntriesPosition position;

        // Collections API
        if (pos == null)
        {
          System.Text.Json.Nodes.JsonNode? jsonNode;
          using (var client = new HttpClient())
          {
            client.DefaultRequestHeaders.Add("ContentType", "application/json;charset=utf-8");
            client.DefaultRequestHeaders.Add("Authorization", clientHeaders.Authorization);
            client.DefaultRequestHeaders.Add("Cookie", clientHeaders.Cookie);
            client.DefaultRequestHeaders.Add("x-csrf-token", clientHeaders.XCSRFToken);
            var res = await client.GetAsync($"https://twitter.com/i/api/1.1/collections/entries.json?tweet_mode=extended&count=150&id=custom-{collectionId}");
            if (res.StatusCode != HttpStatusCode.OK)
            {
              throw new Exception(message: $"{(int)res.StatusCode} {res.ReasonPhrase}");
            }
            var jsonString = await res.Content.ReadAsStringAsync();
            jsonNode = System.Text.Json.Nodes.JsonNode.Parse(jsonString);
          }

          result = jsonNode?["response"]?["timeline"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray();
          position = new CollectionEntriesPosition()
          {
            MaxPosition = long.Parse(jsonNode?["response"]?["position"]?["max_position"]?.ToString() ?? ""),
            MinPosition = long.Parse(jsonNode?["response"]?["position"]?["min_position"]?.ToString() ?? ""),
            WasTruncated = bool.Parse(jsonNode?["response"]?["position"]?["was_truncated"]?.ToString() ?? "")
          };
        }
        else
        {
          System.Text.Json.Nodes.JsonNode? jsonNode;
          using (var client = new HttpClient())
          {
            client.DefaultRequestHeaders.Add("ContentType", "application/json;charset=utf-8");
            client.DefaultRequestHeaders.Add("Authorization", clientHeaders.Authorization);
            client.DefaultRequestHeaders.Add("Cookie", clientHeaders.Cookie);
            client.DefaultRequestHeaders.Add("x-csrf-token", clientHeaders.XCSRFToken);
            var res = await client.GetAsync($"https://twitter.com/i/api/1.1/collections/entries.json?tweet_mode=extended&count=150&max_position={pos.MinPosition}&id=custom-{collectionId}");
            if (res.StatusCode != HttpStatusCode.OK)
            {
              throw new Exception(message: $"{(int)res.StatusCode} {res.ReasonPhrase}");
            }
            var jsonString = await res.Content.ReadAsStringAsync();
            jsonNode = System.Text.Json.Nodes.JsonNode.Parse(jsonString);
          }

          result = jsonNode?["response"]?["timeline"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray();
          position = new CollectionEntriesPosition()
          {
            MaxPosition = long.Parse(jsonNode?["response"]?["position"]?["max_position"]?.ToString() ?? ""),
            MinPosition = long.Parse(jsonNode?["response"]?["position"]?["min_position"]?.ToString() ?? ""),
            WasTruncated = bool.Parse(jsonNode?["response"]?["position"]?["was_truncated"]?.ToString() ?? "")
          };
        }

        // Make saved tweets collection
        List<object[]> sqlRes = DBController.ExecuteReader($"SELECT * FROM tweets");
        List<long> savedIds = new List<long>();

        sqlRes.ForEach(res =>
        {
          savedIds.Add((long)res[0]);
        });


        foreach (var r in result)
        {

          var id = long.Parse(r?["tweet"]?["id"]?.ToString() ?? "");
          // Exclude recorded tweet
          if (!savedIds.Contains(id))
          {
            tweetIds.Add(id);
          }
        }

        return (position);
      }

      await Spinner.StartAsync("Fetching Tweets...", async spinner =>
      {
        try
        {
          CollectionEntriesPosition res;
          if (clientHeaders != null)
          {
            res = await getIdsByClientAsync(clientHeaders);
          }
          else
          {
            res = await getIdsWithCursorAsync();
          }

          while (res.WasTruncated)
          {
            await Task.Delay(500);
            if (clientHeaders != null)
            {
              res = await getIdsByClientAsync(clientHeaders, res);
            }
            else
            {
              res = await getIdsWithCursorAsync(res);
            }
            spinner.Text = "Fetching " + tweetIds.Count + " Tweets...";
          }

          spinner.Succeed("Fetched " + tweetIds.Count + " Tweets.");
        }
        catch (System.Exception e)
        {
          Console.WriteLine(e);
          spinner.Fail("Failed to fetch tweet IDs.");
          Environment.Exit(-1);
        }

      });

      return (tweetIds.ToArray());
    }

    static async Task<TweetMedia[]> getAllMediaUrlsAsync(long[] tweetIds, TwitterClient inviClient, Tokens coreTokens, ClientHeaders? clientHeaders)
    {
      List<TweetMedia> tweetMedias = new List<TweetMedia>();

      await Spinner.StartAsync("Fetching media URLs...", async spinner =>
      {
        int mediaCounts = 0;

        // Legacy API function
        if (clientHeaders == null)
        {
          try
          {
            // For 100 request limit
            int chunkSize = 100;
            var chunks = tweetIds.Select((v, i) => new { v, i })
                  .GroupBy(x => x.i / chunkSize)
                  .Select(g => g.Select(x => x.v));

            foreach (var Ids in chunks)
            {
              var tweetResponses = await inviClient.TweetsV2.GetTweetsAsync(Ids.ToArray());

              foreach (var tweet in tweetResponses.Tweets)
              {
                List<string> mediaUrls = new List<string>();

                // Collect media URLs
                foreach (var mediaKey in tweet.Attachments.MediaKeys)
                {
                  var media = new Tweetinvi.Models.V2.MediaV2();
                  foreach (var m in tweetResponses.Includes.Media)
                  {
                    if (m.MediaKey == mediaKey) media = m;
                  }

                  string url = "";
                  switch (media.Type)
                  {
                    case "animated_gif":
                      {
                        url = Path.ChangeExtension(
                                  media.PreviewImageUrl.Replace("tweet_video_thumb", "tweet_video"), ".mp4"
                              );
                        break;
                      }

                    case "photo":
                      {
                        if (Path.GetExtension(media.Url).ToLower() == ".jpg") url = $"{media.Url}:orig";
                        else url = media.Url;
                        break;
                      }
                    case "video":
                      {
                        var showResponse = coreTokens.Statuses.Show(Convert.ToInt64(tweet.Id), null, null, true, true, CoreTweet.TweetMode.Extended);
                        foreach (var video in showResponse.ExtendedEntities.Media)
                        {
                          int highestBitrate = 0;
                          string videoUrl = "";
                          foreach (var variant in video.VideoInfo.Variants)
                          {
                            if (variant.Bitrate > highestBitrate)
                            {
                              videoUrl = variant.Url;
                              highestBitrate = variant.Bitrate ?? highestBitrate;
                            }
                          }
                          url = videoUrl;
                        }
                        break;
                      }
                    default:
                      {
                        throw new Exception($"Unknown media type '{media.Type}'.");
                      }
                  }

                  if (url == "") Console.WriteLine($"Warning: An empty url detected. Skipped.\nType: {media.Type} Id: {tweet.Id}");
                  else mediaUrls.Add(url);
                }

                // Get author screen name
                var author = await inviClient.UsersV2.GetUserByIdAsync(tweet.AuthorId);

                tweetMedias.Add(new TweetMedia()
                {
                  TweetId = Convert.ToInt64(tweet.Id),
                  UserName = author.User.Username,
                  Medias = mediaUrls.ToArray()
                });
                mediaCounts += mediaUrls.Count();

                spinner.Text = $"Fetching {mediaCounts} media URLs... ";
              }
            }
          }
          catch (System.Exception e)
          {
            Console.WriteLine(e);
            spinner.Fail($"Failed to fetch media URLs.");
            Environment.Exit(-1);
          }
        }

        // ClientHeaders enabled
        else
        {
          System.Text.Json.Nodes.JsonNode? jsonNode = null;
          using (var client = new HttpClient(new HttpClientHandler
          {
            MaxConnectionsPerServer = 25
          }))
          {

            client.DefaultRequestHeaders.Add("ContentType", "application/json;charset=utf-8");
            client.DefaultRequestHeaders.Add("Authorization", clientHeaders.Authorization);
            client.DefaultRequestHeaders.Add("Cookie", clientHeaders.Cookie);
            client.DefaultRequestHeaders.Add("x-csrf-token", clientHeaders.XCSRFToken);

            foreach (var id in tweetIds)
            {
              var res = await client.GetAsync("https://twitter.com/i/api/graphql/Pn68XRZwyV9ClrAEmK8rrQ/TweetDetail?variables={ \"focalTweetId\": \"" + id.ToString() + "\", \"with_rux_injections\": false, \"includePromotedContent\": false, \"withCommunity\": false, \"withQuickPromoteEligibilityTweetFields\": false, \"withBirdwatchNotes\": false, \"withVoice\": false, \"withV2Timeline\": false }&features={ \"rweb_lists_timeline_redesign_enabled\": false, \"responsive_web_graphql_exclude_directive_enabled\": false, \"verified_phone_label_enabled\": false, \"creator_subscriptions_tweet_preview_api_enabled\": false, \"responsive_web_graphql_timeline_navigation_enabled\": false, \"responsive_web_graphql_skip_user_profile_image_extensions_enabled\": false, \"tweetypie_unmention_optimization_enabled\": false, \"responsive_web_edit_tweet_api_enabled\": false, \"graphql_is_translatable_rweb_tweet_is_translatable_enabled\": false, \"view_counts_everywhere_api_enabled\": false, \"longform_notetweets_consumption_enabled\": false, \"tweet_awards_web_tipping_enabled\": false, \"freedom_of_speech_not_reach_fetch_enabled\": false, \"standardized_nudges_misinfo\": false, \"tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled\": false, \"longform_notetweets_rich_text_read_enabled\": false, \"longform_notetweets_inline_media_enabled\": false, \"responsive_web_enhance_cards_enabled\": false }");
              if (res.StatusCode != HttpStatusCode.OK)
              {
                throw new Exception(message: $"{(int)res.StatusCode} {res.ReasonPhrase}");
              }
              var jsonString = await res.Content.ReadAsStringAsync();
              jsonNode = System.Text.Json.Nodes.JsonNode.Parse(jsonString);

              var tweetResult = jsonNode?["data"]?["threaded_conversation_with_injections"]?["instructions"]?[0]?["entries"]?[0]?["content"]?["itemContent"]?["tweet_results"]?["result"];
              if (tweetResult == null)
                throw new Exception($"Failed to fetch tweet result at {id}");
              else
              {
                if (tweetResult["__typename"]?.ToString() != "Tweet") {
                    tweetResult = tweetResult?["tweet"] ?? throw new Exception($"Unrecognized tweetResult at {id}");
                }
              }
              var userInfo = tweetResult?["core"]?["user_results"]?["result"]?["legacy"];
              var medias = tweetResult?["legacy"]?["extended_entities"]?["media"];

              List<string> mediaUrls = new List<string>();

              // Collect media URLs
              foreach (var media in medias?.AsArray() ?? throw new Exception($"medias is null at {id}"))
              {
                string url = "";
                switch (media?["type"]?.ToString())
                {
                  //   case "animated_gif":
                  //     {
                  //       url = Path.ChangeExtension(
                  //                 media.PreviewImageUrl.Replace("tweet_video_thumb", "tweet_video"), ".mp4"
                  //             );
                  //       break;
                  //     }

                  case "photo":
                    {
                      string mediaUrlHttps = media?["media_url_https"]?.ToString() ?? throw new Exception($"media_url_https not found at {id}");
                      if (Path.GetExtension(mediaUrlHttps).ToLower() == ".jpg") url = $"{mediaUrlHttps}:orig";
                      else url = mediaUrlHttps;
                      break;
                    }
                  case "animated_gif":
                  case "video":
                    {
                      int highestBitrate = -1;
                      string videoUrl = "";
                      foreach (var variant in media?["video_info"]?["variants"]?.AsArray() ?? throw new Exception($"variants not found at {id}"))
                      {
                        var bitrate = Int32.Parse(variant?["bitrate"]?.ToString() ?? "-1");
                        if (bitrate > highestBitrate)
                        {
                          videoUrl = variant?["url"]?.ToString()!;
                          highestBitrate = bitrate;
                        }
                      }
                      url = videoUrl;
                      break;
                    }
                  default:
                    {
                      throw new Exception($"Unknown media type '{media?["type"]}'.");
                    }
                }

                if (url == "") Console.WriteLine($"Warning: An empty url detected. Skipped.\nType: {media?["type"]} Id: {id}");
                else mediaUrls.Add(url);
              }

              tweetMedias.Add(new TweetMedia()
              {
                TweetId = id,
                UserName = userInfo?["screen_name"]?.ToString() ?? throw new Exception("screen_name not found. invalid json structure?"),
                Medias = mediaUrls.ToArray()
              });

              mediaCounts += mediaUrls.Count();
              spinner.Text = $"Fetching {mediaCounts} media URLs... ";
            }
          }
        }


        spinner.Succeed($"Fetched {mediaCounts} media URLs.");
      });

      return tweetMedias.ToArray();
    }

    static async Task downloadMediasAsync(TweetMedia[] tweetMedias, string saveDir)
    {
      // Create media url and path pairs
      var pathPairs = new List<MediaPath>();
      foreach (var tweetMedia in tweetMedias)
      {
        int index = 0;
        foreach (var media in tweetMedia.Medias)
        {
          pathPairs.Add(new MediaPath()
          {
            Url = media,
            FilePath = $"{saveDir}/{tweetMedia.UserName}_{tweetMedia.TweetId}_{index}{Path.GetExtension(new Uri(media).AbsolutePath.Replace(":orig", ""))}" //index.ToString("00")
          });
          index++;
        }
      }

      await Spinner.StartAsync("Preparing download...", async spinner =>
      {
        using (var httpClient = new HttpClient(new HttpClientHandler
        {
          MaxConnectionsPerServer = 25
        }))
        {
          await Task.WhenAll(pathPairs.Select((mediaPath, i) => downloadMediaAsync(mediaPath, httpClient, spinner)));
          spinner.Succeed("Download is complete.");
        }
      });

      Spinner.Start("Update files...", spinner =>
      {
        // Update downloaded file's date stats to order
        DateTime dt = DateTime.Now;
        int index = 0;
        foreach (var pair in pathPairs)
        {
          TimeSpan ts = new TimeSpan(0, 0, index);
          File.SetCreationTime(pair.FilePath, dt - ts);
          File.SetLastWriteTime(pair.FilePath, dt - ts);
          File.SetLastAccessTime(pair.FilePath, dt - ts);
          index++;
        }
        spinner.Succeed("All files have been updated.");
      });
    }

    static async Task downloadMediaAsync(MediaPath mediaPath, HttpClient httpClient, Spinner spinner)
    {
      // if (File.Exists(mediaPath.FilePath))
      // {
      //     spinner.Text = ($"Downloading {Path.GetFileName(mediaPath.FilePath)}");
      //     return;
      // }

      // Download and save media
      using (var res = await httpClient.GetAsync(mediaPath.Url, HttpCompletionOption.ResponseHeadersRead))
      using (var fileStream = File.Create(mediaPath.FilePath))
      using (var httpStream = await res.Content.ReadAsStreamAsync())
      {
        await httpStream.CopyToAsync(fileStream);
      }
      spinner.Text = ($"Downloading {Path.GetFileName(mediaPath.FilePath)}");
    }
  }
}
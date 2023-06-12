using System.Net;
using System.Text.Json;
using CoreTweet;
using Kurukuru;

namespace TCCrawler;

public class FetchData
{
  public static async Task<long[]> fetchAllTweetIdsAsync(HttpClient httpClient, string collectionId)
  {
    List<long> tweetIds = new List<long>();

    async Task<CollectionEntriesPosition> fetchCollectionAsync(CollectionEntriesPosition? pos = null)
    {
      System.Text.Json.Nodes.JsonArray result;
      CollectionEntriesPosition position;

      // Collections API
      if (pos == null)
      {
        System.Text.Json.Nodes.JsonNode? jsonNode;

        var res = await httpClient.GetAsync($"https://twitter.com/i/api/1.1/collections/entries.json?tweet_mode=extended&count=150&id=custom-{collectionId}");
        if (res.StatusCode != HttpStatusCode.OK)
        {
          throw new Exception($"Collection entries api returns {(int)res.StatusCode} {res.ReasonPhrase}");
        }
        var jsonString = await res.Content.ReadAsStringAsync();
        jsonNode = System.Text.Json.Nodes.JsonNode.Parse(jsonString);

        result = jsonNode?["response"]?["timeline"]?.AsArray() ?? throw new Exception($"Undefined collection timeline response.\n{jsonNode}");
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
        var res = await httpClient.GetAsync($"https://twitter.com/i/api/1.1/collections/entries.json?tweet_mode=extended&count=150&max_position={pos.MinPosition}&id=custom-{collectionId}");
        if (res.StatusCode != HttpStatusCode.OK)
        {
          throw new Exception($"Collection entries api returns {(int)res.StatusCode} {res.ReasonPhrase}");
        }
        var jsonString = await res.Content.ReadAsStringAsync();
        jsonNode = System.Text.Json.Nodes.JsonNode.Parse(jsonString);

        result = jsonNode?["response"]?["timeline"]?.AsArray() ?? throw new Exception($"Undefined collection timeline response.\n{jsonNode}");
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

        var id = long.Parse(r?["tweet"]?["id"]?.ToString() ?? throw new Exception($"Undefined tweet result at {r}"));
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
        CollectionEntriesPosition collectionPos;
        collectionPos = await fetchCollectionAsync();

        while (collectionPos.WasTruncated)
        {
          await Task.Delay(500);
          collectionPos = await fetchCollectionAsync(collectionPos);
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

  public static async Task<TweetMedia[]> fetchAllMediaUrlsAsync(HttpClient httpClient, long[] tweetIds)
  {
    List<TweetMedia> tweetMedias = new List<TweetMedia>();
    int mediaCounts = 0;

    await Spinner.StartAsync("Fetching media URLs...", async spinner =>
    {
      async Task<(TweetMedia tweetMedia, int sortIndex)> fetchMediaUrlsAsync(long tweetId, int sortIndex)
      {
        var res = await httpClient.GetAsync($"https://api.twitter.com/1.1/statuses/show.json?id={tweetId}&tweet_mode=extended");
        if (res.StatusCode != HttpStatusCode.OK)
        {
          throw new Exception($"Statuses API returns {(int)res.StatusCode} {res.ReasonPhrase} at {tweetId}.");
        }
        var jsonString = await res.Content.ReadAsStringAsync();
        var status = System.Text.Json.Nodes.JsonNode.Parse(jsonString);

        // Collect media URLs
        List<string> mediaUrls = new List<string>();
        foreach (var media in status?["extended_entities"]?["media"]?.AsArray()!)
        {
          string url = "";
          switch (media?["type"]?.ToString())
          {
            case "photo":
              {
                if (Path.GetExtension(media?["media_url_https"]?.ToString()!).ToLower() == ".jpg")
                  url = $"{media?["media_url_https"]}:orig";
                else
                  url = media?["media_url_https"]?.ToString()!;
                break;
              }
            case "animated_gif":
            case "video":
              {
                int? highestBitrate = -1;
                string videoUrl = "";
                foreach (var variant in media?["video_info"]?["variants"]?.AsArray()!)
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

          if (url == "")
            throw new Exception($"Undefined media url at {tweetId}");
          mediaUrls.Add(url);
        }

        mediaCounts += mediaUrls.Count();
        spinner.Text = $"Fetching {mediaCounts} media URLs... ";

        return (new TweetMedia()
        {
          TweetId = tweetId,
          UserName = status?["user"]?["screen_name"]?.ToString()!,
          Medias = mediaUrls.ToArray()
        },
        sortIndex);
      }

      var res = await Task.WhenAll(tweetIds.Select((tweetId, i) => fetchMediaUrlsAsync(tweetId, i)));
      foreach (var t in res.OrderBy(t => t.sortIndex))
      {
        tweetMedias.Add(t.tweetMedia);
      }

      spinner.Succeed($"Fetched {mediaCounts} media URLs.");
    }
    );

    return tweetMedias.ToArray();
  }
}
using System.Net;
using System.Text;
using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using CoreTweet;
using Kurukuru;


namespace TwitAutomaton;

public class FetchData
{
  public static async Task<long[]> FetchAllTweetIdsAsync(HttpClient httpClient, string collectionId)
  {
    List<long> tweetIds = new();

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
      List<long> savedIds = new();

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

      return position;
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
      catch (Exception e)
      {
        Console.WriteLine(e);
        spinner.Fail("Failed to fetch tweet IDs.");
        Environment.Exit(-1);
      }

    });

    return tweetIds.ToArray();
  }

  public static async Task<TweetMedia[]> FetchAllMediaUrlsAsync(HttpClient httpClient, long[] tweetIds)
  {
    List<TweetMedia> tweetMedias = new();
    int mediaCounts = 0;

    await Spinner.StartAsync("Fetching media URLs...", async spinner =>
    {
      async Task<(TweetMedia tweetMedia, int sortIndex, RateLimitParams rateLimit)> fetchMediaUrlsAsync(long tweetId, int sortIndex)
      {
        var query = new Dictionary<string, string>()
        {
          ["variables"] = $"{{\"focalTweetId\":\"{tweetId}\",\"with_rux_injections\":false,\"includePromotedContent\":true,\"withCommunity\":true,\"withQuickPromoteEligibilityTweetFields\":true,\"withBirdwatchNotes\":true,\"withVoice\":true,\"withV2Timeline\":true}}",
          ["features"] = "{\"rweb_lists_timeline_redesign_enabled\":true,\"responsive_web_graphql_exclude_directive_enabled\":true,\"verified_phone_label_enabled\":false,\"creator_subscriptions_tweet_preview_api_enabled\":true,\"responsive_web_graphql_timeline_navigation_enabled\":true,\"responsive_web_graphql_skip_user_profile_image_extensions_enabled\":false,\"tweetypie_unmention_optimization_enabled\":true,\"responsive_web_edit_tweet_api_enabled\":true,\"graphql_is_translatable_rweb_tweet_is_translatable_enabled\":true,\"view_counts_everywhere_api_enabled\":true,\"longform_notetweets_consumption_enabled\":true,\"responsive_web_twitter_article_tweet_consumption_enabled\":false,\"tweet_awards_web_tipping_enabled\":false,\"freedom_of_speech_not_reach_fetch_enabled\":true,\"standardized_nudges_misinfo\":true,\"tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled\":true,\"longform_notetweets_rich_text_read_enabled\":true,\"longform_notetweets_inline_media_enabled\":true,\"responsive_web_media_download_video_enabled\":false,\"responsive_web_enhance_cards_enabled\":false}"
        };

        var res = await httpClient.GetAsync(QueryHelpers.AddQueryString($"https://twitter.com/i/api/graphql/-Ls3CrSQNo2fRKH6i6Na1A/TweetDetail", query));
        if (res.StatusCode != HttpStatusCode.OK)
        {
          if (res.StatusCode == HttpStatusCode.TooManyRequests)
            throw new Exception($"Statuses API returns {(int)res.StatusCode} {res.ReasonPhrase} at {tweetId}. Rate Limit will be reset at {DateTimeOffset.FromUnixTimeSeconds(Int32.Parse(res.Headers.First(pair => string.Compare(pair.Key, @"x-rate-limit-reset") == 0).Value.First())).LocalDateTime}");
          else
            throw new Exception($"Statuses API returns {(int)res.StatusCode} {res.ReasonPhrase} at {tweetId}.");
        }
        var jsonString = await res.Content.ReadAsStringAsync();
        var data = System.Text.Json.Nodes.JsonNode.Parse(jsonString);
        var entry = data?["data"]?["threaded_conversation_with_injections_v2"]?["instructions"]?[0]?["entries"]?.AsArray().Where(e => e?["entryId"]?.ToString() == $"tweet-{tweetId}").First();

        System.Text.Json.Nodes.JsonNode? result = null;
        if (entry?["content"]?["itemContent"]?["tweet_results"]?["result"]?["__typename"]?.ToString() == "TweetWithVisibilityResults")
          result = entry?["content"]?["itemContent"]?["tweet_results"]?["result"]?["tweet"];
        else
          result = entry?["content"]?["itemContent"]?["tweet_results"]?["result"];
        if (result == null)
          throw new Exception($"Unrecognized response structure at {tweetId}");

        // Collect media URLs
        List<string> mediaUrls = new();

        try
        {
          foreach (var media in result?["legacy"]?["extended_entities"]?["media"]?.AsArray()!)
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
        }
        catch (System.NullReferenceException)
        {
          throw new Exception($"NullReferenceException at {tweetId}\n{data?.ToJsonString()}");
        }
        catch
        {
          throw new Exception($"Unrecognized response structure at {tweetId}");
        }

        mediaCounts += mediaUrls.Count();
        spinner.Text = $"Fetching {mediaCounts} media URLs... ";

        return (new TweetMedia()
        {
          TweetId = tweetId,
          UserName = result?["core"]?["user_results"]?["result"]?["legacy"]?["screen_name"]?.ToString() ?? throw new Exception($"Failed to get screen name at {tweetId}"),
          Medias = mediaUrls.ToArray()
        },
        sortIndex,
        new RateLimitParams()
        {
          RateLimitRemaining = int.Parse(res.Headers.First(pair => string.Compare(pair.Key, @"x-rate-limit-remaining") == 0).Value.First()),
          RateLimitReset = int.Parse(res.Headers.First(pair => string.Compare(pair.Key, @"x-rate-limit-reset") == 0).Value.First())
        });
      }

      var index = 0;
      var results = new List<(TweetMedia tweetMedia, int sortIndex, RateLimitParams rateLimit)>();

      while (results.Count < tweetIds.Length)
      {
        // first request to get response headers
        var firstRes = await fetchMediaUrlsAsync(tweetIds.Skip(results.Count).First(), index);
        index++;
        results.Add(firstRes);

        var chunk = tweetIds.Skip(results.Count).Take(firstRes.rateLimit.RateLimitRemaining);
        results.AddRange(await Task.WhenAll(chunk.Select((tweetId, n) => fetchMediaUrlsAsync(tweetId, (n + index)))));
        index += chunk.Count();

        if (results.Count < tweetIds.Length)
        {
          // wait for reset
          var waitSec = results.Last().rateLimit.RateLimitReset - DateTimeOffset.Now.ToUnixTimeSeconds() + 5; //safe offset

          var timer = 0;
          while (timer <= waitSec)
          {
            spinner.Text = $"Fetched {mediaCounts} media URLs. Waiting {waitSec - timer}s for RateLimit Reset...";
            Thread.Sleep(1000);
            timer++;
          }
        }
      }

      foreach (var t in results.OrderBy(t => t.sortIndex))
      {
        tweetMedias.Add(t.tweetMedia);
      }

      mediaCounts = 0;
      tweetMedias.ForEach(t => mediaCounts += t.Medias.Length);

      spinner.Succeed($"Fetched {mediaCounts} media URLs.");
    }
    );

    return tweetMedias.ToArray();
  }

  public static async Task RemoveTweets(HttpClient httpClient, int? count, DateTimeOffset? untilDate, DateTimeOffset? sinceDate, string[]? keywords)
  {
    await Spinner.StartAsync("Preparing to delete Tweets...", async spinner =>
    {
      var uid = await GetUid(httpClient) ?? throw new Exception("Failed to get UserID.");

      int deletedCount = 0;
      string? cursor = null;
      var breakFlag = true;
      while (breakFlag)
      {
        // Fetch Tweets
        var query = new Dictionary<string, string>()
        {
          ["variables"] = $"{{\"userId\":\"{uid}\",\"count\":100,\"includePromotedContent\":false,\"withQuickPromoteEligibilityTweetFields\":false,\"withVoice\":true,\"withV2Timeline\":true}}",
          ["features"] = "{\"rweb_lists_timeline_redesign_enabled\":true,\"responsive_web_graphql_exclude_directive_enabled\":true,\"verified_phone_label_enabled\":false,\"creator_subscriptions_tweet_preview_api_enabled\":false,\"responsive_web_graphql_timeline_navigation_enabled\":true,\"responsive_web_graphql_skip_user_profile_image_extensions_enabled\":false,\"tweetypie_unmention_optimization_enabled\":true,\"responsive_web_edit_tweet_api_enabled\":true,\"graphql_is_translatable_rweb_tweet_is_translatable_enabled\":true,\"view_counts_everywhere_api_enabled\":true,\"longform_notetweets_consumption_enabled\":true,\"responsive_web_twitter_article_tweet_consumption_enabled\":false,\"tweet_awards_web_tipping_enabled\":false,\"freedom_of_speech_not_reach_fetch_enabled\":true,\"standardized_nudges_misinfo\":true,\"tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled\":true,\"longform_notetweets_rich_text_read_enabled\":true,\"longform_notetweets_inline_media_enabled\":true,\"responsive_web_media_download_video_enabled\":false,\"responsive_web_enhance_cards_enabled\":false}"
        };

        var res = await httpClient.GetAsync(QueryHelpers.AddQueryString("https://twitter.com/i/api/graphql/XicnWRbyQ3WgVY__VataBQ/UserTweets", query));
        if (res.StatusCode != HttpStatusCode.OK)
        {
          throw new Exception($"UserTweets API returns {(int)res.StatusCode} {res.ReasonPhrase}.");
        }
        var jsonString = await res.Content.ReadAsStringAsync();
        var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(jsonString);

        // Process tweets
        var entries = jsonNode?["data"]?["user"]?["result"]?["timeline_v2"]?["timeline"]?["instructions"]?[1]?["entries"]?.AsArray() ?? throw new Exception("Failed to parse json.");
        foreach (var entry in entries)
        {
          // Check cursor
          if (entry?["content"]?["entryType"]?.ToString() == "TimelineTimelineCursor")
          {
            if (entry?["content"]?["cursorType"]?.ToString() == "Bottom")
            {
              if (entry?["content"]?["value"]?.ToString() == cursor)
              {
                breakFlag = false;
                break;
              }
              else
                cursor = entry?["content"]?["value"]?.ToString();
            }
          }

          // Filter by date
          if (untilDate != null || sinceDate != null)
          {
            var createdAt = DateTimeOffset.ParseExact(entry?["content"]?["itemContent"]?["tweet_results"]?["result"]?["legacy"]?["created_at"]?.ToString() ?? "", "ddd MMM d HH:mm:ss zzz yyyy", CultureInfo.InvariantCulture);
            if ((untilDate != null && untilDate < createdAt) || (sinceDate != null && sinceDate > createdAt))
              continue;
          }

          // Filter by keywords


          // Delete
          var content = new StringContent($"{{\"variables\":{{\"tweet_id\":\"{entry?["content"]?["itemContent"]?["tweet_results"]?["result"]?["rest_id"]}\"}},\"queryId\":\"ZYKSe-w7KEslx3JhSIk5LA\"}}", Encoding.UTF8, "application/json");
          res = await httpClient.PostAsync("https://twitter.com/i/api/graphql/ZYKSe-w7KEslx3JhSIk5LA/UnfavoriteTweet", content);
          if (res.StatusCode != HttpStatusCode.OK)
          {
            throw new Exception($"UnfavoriteTweet API returns {(int)res.StatusCode} {res.ReasonPhrase}.");
          }
          deletedCount++;
          spinner.Text = $"Deleted {deletedCount} faves...";

          //　Reach specified count
          if (deletedCount == count)
          {
            breakFlag = false;
            break;
          }

          // Check ratelimit
          var rateLimitRemaining = int.Parse(res.Headers.First(pair => string.Compare(pair.Key, @"x-rate-limit-remaining") == 0).Value.First());
          if (rateLimitRemaining == 0)
          {
            // wait for reset
            var waitSec = int.Parse(res.Headers.First(pair => string.Compare(pair.Key, @"x-rate-limit-reset") == 0).Value.First()) - DateTimeOffset.Now.ToUnixTimeSeconds() + 5; //safe offset

            var timer = 0;
            while (timer <= waitSec)
            {
              spinner.Text = $"Deleted {deletedCount} faves. Waiting {waitSec - timer}s for RateLimit Reset...";
              Thread.Sleep(1000);
              timer++;
            }
          }
        }
      }
      spinner.Succeed($"Deleted {deletedCount} Faves.");
    });
  }

  public static async Task RemoveFaves(HttpClient httpClient, int? count, string[]? from)
  {
    await Spinner.StartAsync("Preparing to delete faves...", async spinner =>
    {
      var uid = await GetUid(httpClient) ?? throw new Exception("Failed to get UserID.");

      int deletedCount = 0;
      string? cursor = null;
      var breakFlag = true;
      while (breakFlag)
      {
        // Fetch Faves
        var query = new Dictionary<string, string>()
        {
          ["variables"] = $"{{\"userId\":\"{uid}\",\"count\":100,\"cursor\":\"{cursor}\",\"includePromotedContent\":false,\"withClientEventToken\":false,\"withBirdwatchNotes\":false,\"withVoice\":true,\"withV2Timeline\":true}}",
          ["features"] = "{\"rweb_lists_timeline_redesign_enabled\":true,\"responsive_web_graphql_exclude_directive_enabled\":true,\"verified_phone_label_enabled\":false,\"creator_subscriptions_tweet_preview_api_enabled\":true,\"responsive_web_graphql_timeline_navigation_enabled\":true,\"responsive_web_graphql_skip_user_profile_image_extensions_enabled\":false,\"tweetypie_unmention_optimization_enabled\":true,\"responsive_web_edit_tweet_api_enabled\":true,\"graphql_is_translatable_rweb_tweet_is_translatable_enabled\":true,\"view_counts_everywhere_api_enabled\":true,\"longform_notetweets_consumption_enabled\":true,\"responsive_web_twitter_article_tweet_consumption_enabled\":false,\"tweet_awards_web_tipping_enabled\":false,\"freedom_of_speech_not_reach_fetch_enabled\":true,\"standardized_nudges_misinfo\":true,\"tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled\":true,\"longform_notetweets_rich_text_read_enabled\":true,\"longform_notetweets_inline_media_enabled\":true,\"responsive_web_media_download_video_enabled\":false,\"responsive_web_enhance_cards_enabled\":false}"
        };

        var res = await httpClient.GetAsync(QueryHelpers.AddQueryString("https://twitter.com/i/api/graphql/P_BKPwbhf2pWGbVaoBo7fg/Likes", query));
        if (res.StatusCode != HttpStatusCode.OK)
        {
          throw new Exception($"Likes API returns {(int)res.StatusCode} {res.ReasonPhrase}.");
        }
        var jsonString = await res.Content.ReadAsStringAsync();
        var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(jsonString);

        // Process faves
        var entries = jsonNode?["data"]?["user"]?["result"]?["timeline_v2"]?["timeline"]?["instructions"]?[0]?["entries"]?.AsArray() ?? throw new Exception("Failed to parse json.");
        foreach (var entry in entries)
        {
          // Check cursor
          if (entry?["content"]?["entryType"]?.ToString() == "TimelineTimelineCursor")
          {
            if (entry?["content"]?["cursorType"]?.ToString() == "Bottom")
            {
              if (entry?["content"]?["value"]?.ToString() == cursor)
              {
                breakFlag = false;
                break;
              }
              else
                cursor = entry?["content"]?["value"]?.ToString();
            }

            continue;
          }

          // check author
          if (from != null && !from.Contains(entry?["content"]?["itemContent"]?["tweet_results"]?["result"]?["core"]?["user_results"]?["result"]?["legacy"]?["screen_name"]?.ToString()))
            continue;

          // Delete
          var content = new StringContent($"{{\"variables\":{{\"tweet_id\":\"{entry?["content"]?["itemContent"]?["tweet_results"]?["result"]?["rest_id"]}\"}},\"queryId\":\"ZYKSe-w7KEslx3JhSIk5LA\"}}", Encoding.UTF8, "application/json");

          res = await httpClient.PostAsync("https://twitter.com/i/api/graphql/ZYKSe-w7KEslx3JhSIk5LA/UnfavoriteTweet", content);
          if (res.StatusCode != HttpStatusCode.OK)
          {
            throw new Exception($"UnfavoriteTweet API returns {(int)res.StatusCode} {res.ReasonPhrase}.");
          }
          deletedCount++;
          spinner.Text = $"Deleted {deletedCount} faves...";

          //　Reach specified count
          if (deletedCount == count)
          {
            breakFlag = false;
            break;
          }

          // Check ratelimit
          var rateLimitRemaining = int.Parse(res.Headers.First(pair => string.Compare(pair.Key, @"x-rate-limit-remaining") == 0).Value.First());
          if (rateLimitRemaining == 0)
          {
            // wait for reset
            var waitSec = int.Parse(res.Headers.First(pair => string.Compare(pair.Key, @"x-rate-limit-reset") == 0).Value.First()) - DateTimeOffset.Now.ToUnixTimeSeconds() + 5; //safe offset

            var timer = 0;
            while (timer <= waitSec)
            {
              spinner.Text = $"Deleted {deletedCount} faves. Waiting {waitSec - timer}s for RateLimit Reset...";
              Thread.Sleep(1000);
              timer++;
            }
          }
        }
      }
      spinner.Succeed($"Deleted {deletedCount} Faves.");
    });
  }

  private static async Task<string?> GetUid(HttpClient httpClient)
  {
    // Get ScreenName
    var query = new Dictionary<string, string>()
    {
      ["include_mention_filter"] = "true",
      ["include_nsfw_user_flag"] = "true",
      ["include_nsfw_admin_flag"] = "true",
      ["include_ranked_timeline"] = "true",
      ["include_alt_text_compose"] = "true",
      ["ext"] = "ssoConnections",
      ["include_country_code"] = "true",
      ["include_ext_dm_nsfw_media_filter"] = "true",
      ["include_ext_sharing_audiospaces_listening_data_with_followers"] = "true"
    };
    var res = await httpClient.GetAsync(QueryHelpers.AddQueryString($"https://api.twitter.com/1.1/account/settings.json", query));
    if (res.StatusCode != HttpStatusCode.OK)
    {
      throw new Exception($"API returns {(int)res.StatusCode} {res.ReasonPhrase} at \"https://api.twitter.com/1.1/account/settings.json\".");
    }
    var jsonString = await res.Content.ReadAsStringAsync();
    var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(jsonString);

    // Get UID from ScreenName
    query = new Dictionary<string, string>()
    {
      ["variables"] = $"{{\"screen_name\":\"{jsonNode?["screen_name"]}\"}}"
    };

    res = await httpClient.GetAsync(QueryHelpers.AddQueryString($"https://twitter.com/i/api/graphql/_pnlqeTOtnpbIL9o-fS_pg/ProfileSpotlightsQuery", query));
    if (res.StatusCode != HttpStatusCode.OK)
    {
      throw new Exception($"ProfileSpotlightsQuery API returns {(int)res.StatusCode} {res.ReasonPhrase}.");
    }
    jsonString = await res.Content.ReadAsStringAsync();
    jsonNode = System.Text.Json.Nodes.JsonNode.Parse(jsonString);

    return jsonNode?["data"]?["user_result_by_screen_name"]?["result"]?["rest_id"]?.ToString();
  }
}

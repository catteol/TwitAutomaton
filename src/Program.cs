using System.Text;
using System.Text.Json;
using System.Net;
using System.CommandLine;
using CommandLine;

namespace TwitAutomaton
{
  class Options
  {
    [Option('s', "settings", Required = false, HelpText = "Settings JSON file path. Default is './settings.json'.")]
    public string SettingsFilePath { get; set; } = "./settings.json";

    [Option('i', "id", Required = false, HelpText = "Twitter Collection ID. Only ID digits, no need 'custom-'.")]
    public string CollectionID { get; set; } = string.Empty;

    [Option('o', "out", Required = false, HelpText = "Directory path to save images. Default is './fetched/'.")]
    public string SavePath { get; set; } = "./fetched/";
  }

  public class Settings
  {
    public ClientHeaders? ClientHeaders { get; set; }
  }

  public class ClientHeaders
  {
    public string Authorization { get; set; } = string.Empty;
    public string Cookie { get; set; } = string.Empty;
    public string XCSRFToken { get; set; } = string.Empty;
  }

  public class TweetMedia
  {
    public long TweetId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string[] Medias { get; set; } = new string[0];
  }

  public class MediaPath
  {
    public string Url { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
  }

  public class RateLimitParams
  {
    public int RateLimitRemaining { get; set; }
    public long RateLimitReset { get; set; }
  }

  class Program
  {
    private static readonly HttpClient httpClient = new HttpClient(new HttpClientHandler { MaxConnectionsPerServer = 25 });

    static async Task<int> Main(string[] args)
    {
      Console.OutputEncoding = Encoding.UTF8;
      ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

      // Parse options
      var rootCommand = new RootCommand("Twitter Automation Tool");

      // Global option
      var settingOption = new Option<string>("--settings", "A path to the specific settings JSON file. The default value is \"./settings.json\".");
      settingOption.SetDefaultValue("./settings.json");
      rootCommand.AddGlobalOption(settingOption);

      // Collection subcommand
      var collectionCommand = new Command("collection", "Commands about collections.");
      rootCommand.Add(collectionCommand);

      var collectionSaveCommand = new Command("save", "Save medias from collections to local filesystem.");
      var collectionSaveIdsOption = new Option<string[]>("--id", "Collection ID without \"custom-\" prefix. Supports multiple IDs.") { IsRequired = true, AllowMultipleArgumentsPerToken = true };
      collectionSaveIdsOption.AddAlias("-i");
      collectionSaveCommand.AddOption(collectionSaveIdsOption);

      var collectionSaveDestOption = new Option<string>("--dest", "A destination path to save medias. The default value is \"./fetched/\".");
      collectionSaveDestOption.AddAlias("-d");
      collectionSaveDestOption.SetDefaultValue("./fetched/");
      collectionSaveCommand.AddOption(collectionSaveDestOption);

      collectionCommand.Add(collectionSaveCommand);

      // Tweet subcommand
      var tweetCommand = new Command("tweet", "Commands about tweets.");
      rootCommand.Add(tweetCommand);

      var tweetDeleteCommand = new Command("delete", "Remove tweets. You can filter tweets for deletion.\nIf you want to use optional values as local time, please specify the timezone like: 2023/01/23 04:56:07 +09:00");

      var tweetDeleteCountOption = new Option<string>("--count", "Count of tweets to delete from latest fave.");
      tweetDeleteCountOption.AddAlias("-c");
      tweetDeleteCommand.AddOption(tweetDeleteCountOption);

      var tweetDeleteUntilOption = new Option<string>("--until", "Add filter that targets before the specified date.");
      tweetDeleteUntilOption.AddAlias("-u");
      tweetDeleteCommand.AddOption(tweetDeleteUntilOption);

      var tweetDeleteSinceOption = new Option<string>("--since", "Add filter that targets after the specified date.");
      tweetDeleteSinceOption.AddAlias("-s");
      tweetDeleteCommand.AddOption(tweetDeleteSinceOption);

      var tweetDeleteKeywordsOption = new Option<string[]>("--Keywords", "Add filter that targets including the specified keyword(s). Supports multiple keywords(OR search).") { AllowMultipleArgumentsPerToken = true };
      tweetDeleteKeywordsOption.AddAlias("-k");
      tweetDeleteCommand.AddOption(tweetDeleteKeywordsOption);

      tweetCommand.Add(tweetDeleteCommand);

      // Fave subcommand
      var faveCommand = new Command("fave", "Commands about faves.");
      rootCommand.Add(faveCommand);

      var faveDeleteCommand = new Command("delete", "Remove tweets. You can filter tweets for deletion.");

      var faveDeleteCountOption = new Option<string>("--count", "Count of faves to delete from latest fave.");
      faveDeleteCountOption.AddAlias("-c");
      faveDeleteCommand.AddOption(faveDeleteCountOption);

      var faveDeleteFromOption = new Option<string[]>("--from", "Specify tweet author's user name which want to remove from faves. Supports multiple user name(s).") { AllowMultipleArgumentsPerToken = true };
      faveDeleteFromOption.AddAlias("-f");
      faveDeleteCommand.AddOption(faveDeleteFromOption);

      faveCommand.Add(faveDeleteCommand);


      // collection save
      collectionSaveCommand.SetHandler(async (collectionIds, destPath, settingsPath) =>
            {
              var settings = Initialize(settingsPath);

              // Create dest directory
              Directory.CreateDirectory(destPath);

              // Init DB
              DBController.ExecuteNoneQuery("CREATE TABLE IF NOT EXISTS tweets(id integer not null primary key, url text);");

              foreach (var (collectionId, index) in collectionIds.Select((collectionId, index) => (collectionId, index)))
              {
                Console.WriteLine($"Processing {collectionId}... [{index + 1}/{collectionIds.Count()}]");

                long[] tweetIds = await FetchData.FetchAllTweetIdsAsync(httpClient, collectionId.ToString());
                TweetMedia[] tweetMedias = await FetchData.FetchAllMediaUrlsAsync(httpClient, tweetIds);
                await Download.downloadMediasAsync(httpClient, tweetMedias, destPath);

                // Update DB
                var sqlQueries = new List<string>();
                foreach (var tweetMedia in tweetMedias)
                {
                  sqlQueries.Add($"INSERT INTO tweets VALUES({tweetMedia.TweetId}, 'https://twitter.com/{tweetMedia.UserName}/status/{tweetMedia.TweetId}')");
                }
                DBController.ExecuteNoneQueryWithTransaction(sqlQueries.ToArray());
              }
            },
            collectionSaveIdsOption, collectionSaveDestOption, settingOption);

      // tweet remove
      tweetDeleteCommand.SetHandler(async (count, until, since, keywords, settingsPath) =>
      {
        var settings = Initialize(settingsPath);

        // Parse count
        int? parsedCount = null;
        if (count != null)
          parsedCount = int.Parse(count);

        // Parse date
        DateTimeOffset? untilDate = null, sinceDate = null;
        try
        {
          if (until != null)
          {
            untilDate = DateTimeOffset.Parse(until);
            if (untilDate?.Offset.Ticks > 0)
            {
              Console.ForegroundColor = ConsoleColor.Yellow;
              Console.WriteLine("Warn: Given \"until\" value will use as UTC. If you want use it as local time, type \"TwitAutomaton fave delete -h\" to show examples.");
              Console.ResetColor();
            }
          }
          if (since != null)
          {
            sinceDate = DateTimeOffset.Parse(since);
            if (sinceDate?.Offset.Ticks > 0)
            {
              Console.ForegroundColor = ConsoleColor.Yellow;
              Console.WriteLine("Warn: Given \"since\" value will use as UTC. If you want use it as local time, type \"TwitAutomaton fave delete -h\" to show examples.");
              Console.ResetColor();
            }
          }
          if (untilDate != null && sinceDate != null && untilDate < sinceDate)
            throw new Exception("Invalid date range. Check your inputs.");
        }
        catch (Exception)
        {
          throw;
        }

        await FetchData.RemoveTweets(httpClient, parsedCount, untilDate, sinceDate, keywords);

      },
      tweetDeleteCountOption, tweetDeleteUntilOption, tweetDeleteSinceOption, tweetDeleteKeywordsOption, settingOption);

      // fave remove
      faveDeleteCommand.SetHandler(async (count, from, settingsPath) =>
      {
        var settings = Initialize(settingsPath);

        // Parse count
        int? parsedCount = null;
        if (count != null)
          parsedCount = int.Parse(count);

        // DateTimeOffset? untilDate = null, sinceDate = null;

        // try
        // {
        //   if (until != null)
        //   {
        //     untilDate = DateTimeOffset.Parse(until);
        //     if (untilDate?.Offset.Ticks > 0)
        //     {
        //       Console.ForegroundColor = ConsoleColor.Yellow;
        //       Console.WriteLine("Warn: Given \"until\" value will use as UTC. If you want use it as local time, type \"TwitAutomaton fave delete -h\" to show examples.");
        //       Console.ResetColor();
        //     }
        //   }
        //   if (since != null)
        //   {
        //     sinceDate = DateTimeOffset.Parse(since);
        //     if (sinceDate?.Offset.Ticks > 0)
        //     {
        //       Console.ForegroundColor = ConsoleColor.Yellow;
        //       Console.WriteLine("Warn: Given \"since\" value will use as UTC. If you want use it as local time, type \"TwitAutomaton fave delete -h\" to show examples.");
        //       Console.ResetColor();
        //     }
        //   }
        //   if (untilDate != null && sinceDate != null && untilDate < sinceDate)
        //     throw new Exception("Invalid date range. Check your inputs.");
        // }
        // catch (Exception)
        // {
        //   throw;
        // }

        await FetchData.RemoveFaves(httpClient, parsedCount, from.Length > 0 ? from : null);

      },
      faveDeleteCountOption, faveDeleteFromOption, settingOption);

      return await rootCommand.InvokeAsync(args);
    }

    static Settings Initialize(string settingPath)
    {
      Settings? settings = new();

      // Load settings json
      try
      {
        using (StreamReader reader = File.OpenText(settingPath))
        {
          while (!reader.EndOfStream)
          {
            settings = JsonSerializer.Deserialize<Settings>(reader.ReadToEnd()) ?? new Settings();
          }
        }

        if (settings.ClientHeaders == null)
          throw new Exception("Failed to load 'ClientHeaders' settings.");
      }
      catch (Exception e)
      {
        Console.WriteLine(e);
        throw new Exception("Failed to load settings.");
      }

      // Configure httpclient
      httpClient.DefaultRequestHeaders.Add("ContentType", "application/json;charset=utf-8");
      httpClient.DefaultRequestHeaders.Add("Authorization", settings.ClientHeaders.Authorization);
      httpClient.DefaultRequestHeaders.Add("Cookie", settings.ClientHeaders.Cookie);
      httpClient.DefaultRequestHeaders.Add("x-csrf-token", settings.ClientHeaders.XCSRFToken);

      return settings;
    }
  }
}
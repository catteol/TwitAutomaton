using System.Text;
using System.Text.Json;
using System.Net;
using CommandLine;

namespace TwitterAutomationTool
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

  public class Settings
  {
    public ClientHeaders? ClientHeaders { get; set; }
    public string? GraphQLInstance { get; set; }
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
            }
          }

          if (settings.ClientHeaders == null)
            throw new Exception("Failed to load 'ClientHeaders' settings.");
        }
        catch (System.Exception e)
        {
          Console.WriteLine(e);
          throw new Exception("Failed to load settings.");
        }

        // Configure httpclient
        httpClient.DefaultRequestHeaders.Add("ContentType", "application/json;charset=utf-8");
        httpClient.DefaultRequestHeaders.Add("Authorization", settings.ClientHeaders.Authorization);
        httpClient.DefaultRequestHeaders.Add("Cookie", settings.ClientHeaders.Cookie);
        httpClient.DefaultRequestHeaders.Add("x-csrf-token", settings.ClientHeaders.XCSRFToken);

        // Create dest directory
        Directory.CreateDirectory(parsed.Value.SavePath);

        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

        DBController.ExecuteNoneQuery("CREATE TABLE IF NOT EXISTS tweets(id integer not null primary key, url text);");

        long[] tweetIds = await FetchData.fetchAllTweetIdsAsync(httpClient, parsed.Value.CollectionID);
        TweetMedia[] tweetMedias = await FetchData.fetchAllMediaUrlsAsync(httpClient, settings.GraphQLInstance ?? "-Ls3CrSQNo2fRKH6i6Na1A", tweetIds);
        await Download.downloadMediasAsync(httpClient, tweetMedias, parsed.Value.SavePath);

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
  }
}
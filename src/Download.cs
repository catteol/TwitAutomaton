using Kurukuru;

namespace TCCrawler;

public class Download
{
  public static async Task downloadMediasAsync(HttpClient httpClient, TweetMedia[] tweetMedias, string saveDir)
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
      await Task.WhenAll(pathPairs.Select((mediaPath, i) => downloadMediaAsync(mediaPath, httpClient, spinner)));
      spinner.Succeed("Download is complete.");
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
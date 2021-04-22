using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace BingDailyImagesDl
{
	public static class Program
	{
		private const string BaseUrl = "https://www.bing.com";
		private const string ImageMetadataUrl = "HPImageArchive.aspx?format=js&idx=0&n=8&mkt=en-CA"; // n indicates how many image metadatas to fetch. 8 is max.
		
		private static readonly string[] ImgMimes = {
			"image/apng",
			"image/bmp",
			"image/gif",
			"image/x-icon",
			"image/jpeg",
			"image/png",
			"image/svg+xml",
			"image/tiff",
			"image/webp",
		};

		private static string CurrentExeDirectory => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

		public static async Task Main(string[] args)
		{
            var option = new Option<string>("--savedir", "The directory to save downloaded images to.")
			{
				Name = "savedir",
				Required = true
			};
			option.AddAlias("-s");

            var cmd = new RootCommand
            {
                option
            };
			cmd.Description = "Download the Bing daily image.";
			
			try
			{
				cmd.Handler = CommandHandler.Create<string>(DownloadBingDailyImage);
				await cmd.InvokeAsync(args);
			}
			catch (Exception ex)
			{
				File.WriteAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "bing-error.txt"), ex.ToString());
			}
		}

		private static async Task DownloadBingDailyImage(string saveDir)
		{
			var jsonHttpClient = BuildJsonHttpClient();
			var metadataTask = jsonHttpClient.GetStringAsync($"{BaseUrl}/{ImageMetadataUrl}");
			var metadataStr = await metadataTask;
			var metadata = JsonSerializer.Deserialize<RootObject>(metadataStr, new JsonSerializerOptions {PropertyNameCaseInsensitive = true});
			var imageHttpClient = BuildImageHttpClient();

			if (metadata.Images.Length == 0)
			{
				return;
			}

			var cache = new StringCache(Path.Combine(CurrentExeDirectory, "cache.txt"));
			cache.Load();

			try
			{
				foreach (var metadataImage in metadata.Images)
				{
					var uri = new Uri(BaseUrl + metadataImage.Url);
					var queryDictionary = HttpUtility.ParseQueryString(uri.Query);
					var id = queryDictionary["id"];

					if (cache.Contains(id))
					{
						continue;
					}

					var imgStream = await imageHttpClient.GetStreamAsync(uri);
					var directoryInfo = Directory.CreateDirectory(saveDir);

					await using var fileStream = File.Create(Path.Combine(directoryInfo.FullName, id));
					await imgStream.CopyToAsync(fileStream);

					cache.Add(id);
				}
			}
			finally
			{
				cache.Flush();
			}
		}

		private static HttpClient BuildImageHttpClient()
		{
			var client = new HttpClient();
			client.DefaultRequestHeaders.Accept.Clear();
			foreach (var mime in ImgMimes)
			{
				client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(mime));
			}
			return client;
		}

		private static HttpClient BuildJsonHttpClient()
		{
			var client = new HttpClient();
			client.DefaultRequestHeaders.Accept.Clear();
			client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			return client;
		}

		public class RootObject
		{
			public Image[] Images { get; set; }
			public ToolTips ToolTips { get; set; }
		}

		public class ToolTips
		{
			public string Loading { get; set; }
			public string Previous { get; set; }
			public string Next { get; set; }
			public string WallE { get; set; }
			public string WallS { get; set; }
		}

		public class Image
		{
			public string StartDate { get; set; }
			public string FullStartDate { get; set; }
			public string EndDate { get; set; }
			public string Url { get; set; }
			public string UrlBase { get; set; }
			public string Copyright { get; set; }
			public string CopyrightLink { get; set; }
			public string Title { get; set; }
			public string Quiz { get; set; }
			public bool Wp { get; set; }
			public string Hsh { get; set; }
			public int Drk { get; set; }
			public int Top { get; set; }
			public int Bot { get; set; }
			public object[] Hs { get; set; }
		}

		public class StringCache
		{
			private readonly IList<string> _added = new List<string>();
			private string[] _cache;

			public StringCache(string filePath)
			{
				FilePath = filePath;
			}

			public string FilePath { get; }

			public void Load()
			{
				try
				{
					_cache = File.ReadAllLines(FilePath);
				}
				catch
				{
					var s = File.CreateText(FilePath);
					s.Close();

					_cache = File.ReadAllLines(FilePath);
				}
			}

			public bool Contains(string str)
			{
				return _cache.Contains(str);
			}

			public void Add(string str)
			{
				_added.Add(str);
			}

			public void Flush()
			{
				if (_added.Count > 0)
				{
					File.AppendAllLines(FilePath, _added);
					_added.Clear();
				}

				Load();
			}
		}
	}
}

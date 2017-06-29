/*
 * Copyright 2015 Google Inc. All Rights Reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 *  Unless required by applicable law or agreed to in writing, software
 *  distributed under the License is distributed on an "AS IS" BASIS,
 *  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *  See the License for the specific language governing permissions and
 *  limitations under the License.
 *
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using NReco.VideoConverter;
using YoutubeExtractor;
using YouTubeHelper.ConsoleApp;

namespace Google.Apis.YouTube.Samples
{
    /// <summary>
    /// YouTube Data API v3 sample: create a playlist.
    /// Relies on the Google APIs Client Library for .NET, v1.7.0 or higher.
    /// See https://code.google.com/p/google-api-dotnet-client/wiki/GettingStarted
    /// </summary>
    internal class PlaylistUpdates
    {

        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("YouTube Data API: Playlist Downloader");
            Console.WriteLine("==================================");

            try
            {
                new PlaylistUpdates().Run(args).Wait();
            }
            catch (AggregateException ex)
            {
                foreach (var e in ex.InnerExceptions)
                {
                    Console.WriteLine("Error: " + e.Message);
                }
            }

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        private async Task Run(string[] args)
        {
            int counter = 0;

            var arguments = await ProcessArguments(args);
            var youtubeService = await BuildYouTubeService();
            var destinationDirectory = await GetDestinationDirectory(arguments);
            var playlistId = await GetPlaylistId(youtubeService, arguments);
            Console.WriteLine("Start downloading:");

            string nextPaginationToken = null;
            do
            {
                var itemsRequest = youtubeService.PlaylistItems.List("snippet");
                itemsRequest.PlaylistId = playlistId;
                itemsRequest.MaxResults = 50;
                if (!String.IsNullOrEmpty(nextPaginationToken))
                {
                    itemsRequest.PageToken = nextPaginationToken;
                }
                var itemsResult = await itemsRequest.ExecuteAsync();
                nextPaginationToken = itemsResult.NextPageToken;

                foreach (var item in itemsResult.Items)
                {
                    var videoRequest = youtubeService.Videos.List("snippet, status");
                    videoRequest.Id = item.Snippet.ResourceId.VideoId;

                    var videoResult = await videoRequest.ExecuteAsync();
                    if (videoResult.Items.Any())
                    {
                        Console.WriteLine("Result:{1} {0}", videoResult.Items.First().Snippet.Title, ++counter);
                        IEnumerable<VideoInfo> videoInfos = DownloadUrlResolver.GetDownloadUrls(String.Format("https://www.youtube.com/watch?v={0}", item.Snippet.ResourceId.VideoId));
                        DownloadMp3(videoInfos, destinationDirectory);
                    }
                }
            } while (!String.IsNullOrEmpty(nextPaginationToken));


            Console.WriteLine("END");
        }

        private async Task<IDictionary<ProgramArguments, string>> ProcessArguments(string[] args)
        {
            return new Dictionary<ProgramArguments, string>()
            {
                {ProgramArguments.DestinationDirectory, @"C:\Users\wilfred.verweij\Downloads\test\test" }
            };
        }

        private async Task<YouTubeService> BuildYouTubeService()
        {
            Console.Write("Connecting to YouTube: ");
            try
            {

                UserCredential credential;
                using (var stream = new FileStream("client_secrets.json", FileMode.Open, FileAccess.Read))
                {
                    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.Load(stream).Secrets,
                        // This OAuth 2.0 access scope allows for full read/write access to the
                        // authenticated user's account.
                        new[] {YouTubeService.Scope.Youtube},
                        "user",
                        CancellationToken.None,
                        new FileDataStore(this.GetType().ToString())
                    );
                }

                var youtubeService = new YouTubeService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = this.GetType().ToString()
                });
                Console.Write("Connected");
                Console.WriteLine();
                return youtubeService;

            }
            catch (Exception ex)
            {
                Console.Write("FAILED: {0}", ex.Message);
                Console.WriteLine();
                throw;
            }
        }

        private async Task<string> GetArgument(string title, ProgramArguments key,
            IDictionary<ProgramArguments, string> arguments)
        {
            Console.Write("{0}: ", title);
            var setting = String.Empty;
            if (arguments.ContainsKey(key))
            {
                setting = arguments[key];
                Console.Write(setting);
                Console.WriteLine();
            }
            else
            {
                setting = await Console.In.ReadLineAsync();
            }
            return setting;
        }

        private async Task<string> GetDestinationDirectory(IDictionary<ProgramArguments, string> arguments)
        {
            var dir = await GetArgument("Destination Directory", ProgramArguments.DestinationDirectory, arguments);

            if (String.IsNullOrWhiteSpace(dir))
            {
                Console.WriteLine("ERROR: Invalid path");
                throw new ArgumentException("Destination directory");
            }
            if (!Directory.Exists(dir))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ERROR: Failed to create directory => {0}", ex.Message);
                    throw;
                }
            }
            return dir;
        }

        private async Task<string> GetPlaylistId(YouTubeService service, IDictionary<ProgramArguments, string> arguments)
        {
            var playlist = await GetArgument("Search playlist", ProgramArguments.Playlist, arguments);


            var searchRequest = service.Search.List("snippet");
            searchRequest.Type = "playlist";
            searchRequest.MaxResults = 10;
            searchRequest.Q = playlist;

            var searchResult = await searchRequest.ExecuteAsync();

            if (!searchResult.Items.Any())
            {
                Console.WriteLine("No items found. Please try again");
                return await GetPlaylistId(service, new Dictionary<ProgramArguments, string>());
            }

            Console.WriteLine("Found playlists. Please select number:");
            for(int i = 0; i < searchResult.Items.Count; i++)
            {
                var item = searchResult.Items[i];
                Console.WriteLine("- {0,2}. {1}", i+1, item.Snippet.Title);
            }
            int number = Int32.Parse(await Console.In.ReadLineAsync());
            return searchResult.Items[number-1].Id.PlaylistId;
        }

        private void DownloadMp3(IEnumerable<VideoInfo> videoInfos, string destinationDirectory)
        {
            int latestReportedProgress = 0;
            /*
             * Select the .mp4 video with highest resolution
             */
            VideoInfo video = videoInfos
                .Where(info => info.VideoType == VideoType.Mp4 && info.AudioBitrate > 0)
                .OrderByDescending(info => info.AudioBitrate)
                .First();

            /*
             * If the video has a decrypted signature, decipher it
             */
            if (video.RequiresDecryption)
            {
                DownloadUrlResolver.DecryptDownloadUrl(video);
            }

            /*
             * Create the video downloader.
             * The first argument is the video to download.
             * The second argument is the path to save the video file.
             */
            var mp4file = RemoveIllegalPathCharacters(video.Title + video.VideoExtension);
            var mp3file = RemoveIllegalPathCharacters(video.Title + ".mp3");
            var videoPath = Path.Combine(destinationDirectory, mp4file);
            var audioPath = Path.Combine(destinationDirectory, mp3file);
            if (!File.Exists(videoPath))
            {
                Console.Write("Download: -");
                var videoDownloader = new VideoDownloader(video, videoPath);

                // Register the ProgressChanged event and print the current progress
                videoDownloader.DownloadProgressChanged += (sender, args) =>
                {
                    if (args.ProgressPercentage > (latestReportedProgress + 10))
                    {
                        Console.Write("-");
                        latestReportedProgress += 10;
                    }
                    if (args.ProgressPercentage == 100.0)
                    {
                        Console.WriteLine();
                    }
                };

                /*
                 * Execute the video downloader.
                 * For GUI applications note, that this method runs synchronously.
                 */
                videoDownloader.Execute();
            }

            if (!File.Exists(audioPath))
            {
                Console.WriteLine("Convert: ----------");

                string arg = String.Format(@"-i ""{0}"" ""{1}""", mp4file, mp3file);
                Process proc = new Process();
                proc.StartInfo.WorkingDirectory = destinationDirectory;
                proc.StartInfo.FileName = @"ffmpeg";
                proc.StartInfo.Arguments = arg;

                proc.StartInfo.CreateNoWindow = true;
                proc.StartInfo.UseShellExecute = false;
                proc.Start();
                proc.WaitForExit();
            }

            if (File.Exists(videoPath) && File.Exists(audioPath))
            {
                Console.WriteLine("Delete video");
                File.Delete(videoPath);
            }
        }

        private static string RemoveIllegalPathCharacters(string path)
        {
            string regexSearch = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            var r = new Regex(string.Format("[{0}]", Regex.Escape(regexSearch)));
            return r.Replace(path, "");
        }
    }
}
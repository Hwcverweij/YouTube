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
            Console.WriteLine("YouTube Data API: Playlist Updates");
            Console.WriteLine("==================================");

            try
            {
                new PlaylistUpdates().Run().Wait();
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

        private async Task Run()
        {
            int counter = 0;
            UserCredential credential;
            using (var stream = new FileStream("client_secrets.json", FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    // This OAuth 2.0 access scope allows for full read/write access to the
                    // authenticated user's account.
                    new[] { YouTubeService.Scope.Youtube },
                    "user",
                    CancellationToken.None,
                    new FileDataStore(this.GetType().ToString())
                );
            }


            Console.WriteLine("YouTubeService");
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = this.GetType().ToString()
            });

            Console.WriteLine("Playlist");
            var playlistRequest = youtubeService.Playlists.List("id, snippet");
            playlistRequest.Id = "PLD-rnqRxia1R1Nr9nqcumjcG0KffKpGA0";

            var playlistResult = await playlistRequest.ExecuteAsync();

            Console.WriteLine("Result: {0}", playlistResult.Items.FirstOrDefault()?.Snippet.Title);

            Console.WriteLine("Items");
            string nextPaginationToken = null;
            do
            {
                var itemsRequest = youtubeService.PlaylistItems.List("snippet");
                itemsRequest.PlaylistId = "PLD-rnqRxia1R1Nr9nqcumjcG0KffKpGA0";
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
                        DownloadMp3(videoInfos);
                    }
                }
            } while (!String.IsNullOrEmpty(nextPaginationToken));


            Console.WriteLine("END");
        }

        private void DownloadMp3(IEnumerable<VideoInfo> videoInfos)
        {
            int latestReportedProgress = 0;
            /*
             * Select the .mp4 video with highest resolution
             */
            VideoInfo video = videoInfos
                .Where(info => info.VideoType == VideoType.Mp4)
                .OrderByDescending(info => info.Resolution)
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
            var videoPath = Path.Combine("C:/Users/wilfred.verweij/Downloads/Otto", RemoveIllegalPathCharacters(video.Title + video.VideoExtension));
            if (File.Exists(videoPath))
            {
                return;
            }
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
            /*
            var mp4file = String.Format(@"""{0}""", video.Title + video.VideoExtension);
            var mp3file = String.Format(@"""{0}""", video.Title + ".mp3");

            string arg = "-i " + mp4file + " " + mp3file;
            Process proc = new Process();
            proc.ErrorDataReceived += new DataReceivedEventHandler(process_ErrorDataReceived);
            proc.OutputDataReceived += new DataReceivedEventHandler(process_OutputDataReceived);
            proc.Exited += new EventHandler(process_Exited);
            proc.StartInfo.WorkingDirectory = "C:/Users/wilfred.verweij/Downloads/Otto";
            proc.StartInfo.FileName = @"C:\Users\wilfred.verweij\Downloads\ffmpeg-20161227-0ff8c6b-win64-static\ffmpeg-20161227-0ff8c6b-win64-static\bin\ffmpeg.exe";
            proc.StartInfo.Arguments = arg;

            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            */
        }

        static int currentLine = 0;
        static void process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            Console.WriteLine("Input line: {0} ({1:m:s:fff})", currentLine++, DateTime.Now);
            Console.WriteLine(e.Data);
            Console.WriteLine();
        }

        static void process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            Console.WriteLine("Output Data Received.");
        }
        static void process_Exited(object sender, EventArgs e)
        {
            Console.WriteLine("Bye bye!");
        }


        private static string RemoveIllegalPathCharacters(string path)
        {
            string regexSearch = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            var r = new Regex(string.Format("[{0}]", Regex.Escape(regexSearch)));
            return r.Replace(path, "");
        }
    }
}
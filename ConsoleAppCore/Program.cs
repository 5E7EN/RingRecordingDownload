﻿using System;
using KoenZomers.Ring.Api;
using System.Configuration;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;

namespace KoenZomers.Ring.RecordingDownload
{
    class Program
    {
        // Initialize semaphore for managing concurrent tasks 
        static SemaphoreSlim semaphoreSlim;
        // Log queue
        static ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();

        static void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var logMessage = $"[{timestamp}] - {message}";
            Console.WriteLine(logMessage);
            logQueue.Enqueue(logMessage);
        }

        /// <summary>
        /// Refresh token to use to authenticate to the Ring API
        /// </summary>
        public static string RefreshToken
        {
            get { return ConfigurationManager.AppSettings["RefreshToken"]; }
            set
            {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                if (configFile.AppSettings.Settings["RefreshToken"] == null)
                {
                    configFile.AppSettings.Settings.Add("RefreshToken", value);
                }
                else
                {
                    configFile.AppSettings.Settings["RefreshToken"].Value = value;
                }
                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            }
        }

        /// <summary>
        /// The Id of the last downloaded historical event
        /// </summary>
        public static string LastdownloadedRecordingId
        {
            get { return ConfigurationManager.AppSettings["LastdownloadedRecordingId"]; }
            set
            {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                if (configFile.AppSettings.Settings["LastdownloadedRecordingId"] == null)
                {
                    configFile.AppSettings.Settings.Add("LastdownloadedRecordingId", value);
                }
                else
                {
                    configFile.AppSettings.Settings["LastdownloadedRecordingId"].Value = value;
                }
                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            }
        }

        static async Task Main(string[] args)
        {
            Console.WriteLine();

            var appVersion = Assembly.GetExecutingAssembly().GetName().Version;

            Console.WriteLine($"Ring Recordings Download Tool v{appVersion.Major}.{appVersion.Minor}.{appVersion.Build}.{appVersion.Revision} by Koen Zomers");
            Console.WriteLine();

            // Ensure arguments have been provided
            if (args.Length == 0)
            {
                DisplayHelp();
                Environment.Exit(1);
            }

            // Parse the provided arguments
            var configuration = ParseArguments(args);

            // Ensure we have the required configuration
            if (string.IsNullOrWhiteSpace(configuration.Username) && (string.IsNullOrWhiteSpace(RefreshToken) || configuration.IgnoreCachedToken))
            {
                Console.WriteLine("-username is required");
                Environment.Exit(1);
            }

            if (string.IsNullOrWhiteSpace(configuration.Password) && (string.IsNullOrWhiteSpace(RefreshToken) || configuration.IgnoreCachedToken))
            {
                Console.WriteLine("-password is required");
                Environment.Exit(1);
            }

            if (!configuration.RingDeviceId.HasValue && !configuration.ListBots)
            {
                Console.WriteLine("Error: -deviceid or -list is required");
                Environment.Exit(1);
            }

            if (!configuration.StartDate.HasValue && !configuration.ListBots)
            {
                Console.WriteLine("-startdate or -lastdays is required");
                Environment.Exit(1);
            }

            // Initialize semaphore and with max threads limit
            semaphoreSlim = new SemaphoreSlim(configuration.MaxConcurrency);

            // Connect to Ring
            Console.WriteLine("Connecting to Ring services");
            Session session;
            if (!string.IsNullOrWhiteSpace(RefreshToken) && !configuration.IgnoreCachedToken)
            {
                // Use refresh token from previous session
                Console.WriteLine("Authenticating using refresh token from previous session");

                session = Session.GetSessionByRefreshToken(RefreshToken).Result;
            }
            else
            {
                // Use the username and password provided
                Console.WriteLine("Authenticating using provided username and password");

                session = new Session(configuration.Username, configuration.Password);

                try
                {
                    await session.Authenticate();
                }
                catch (Api.Exceptions.TwoFactorAuthenticationRequiredException)
                {
                    // Two factor authentication is enabled on the account. The above Authenticate() will trigger a text or e-mail message to be sent. Ask for the token sent in that message here.
                    Console.WriteLine($"Two factor authentication enabled on this account, please enter the Ring token from the e-mail, text message or authenticator app:");
                    var token = Console.ReadLine();

                    // Authenticate again using the two factor token
                    await session.Authenticate(twoFactorAuthCode: token);
                }
                catch (Api.Exceptions.ThrottledException)
                {
                    Console.WriteLine("Two factor authentication is required, but too many tokens have been requested recently. Wait for a few minutes and try connecting again.");
                    Environment.Exit(1);
                }
                catch (System.Net.WebException)
                {
                    Console.WriteLine("Connection failed. Validate your credentials.");
                    Environment.Exit(1);
                }
            }

            // If we have a refresh token, update the config file with it so we don't need to authenticate again next time
            if (session.OAuthToken != null)
            {
                RefreshToken = session.OAuthToken.RefreshToken;
            }

            if (configuration.ListBots)
            {
                // Retrieve all available Ring devices and list them
                Console.Write("Retrieving all devices... ");

                var devices = await session.GetRingDevices();

                Console.WriteLine($"{devices.Doorbots.Count + devices.AuthorizedDoorbots.Count + devices.StickupCams.Count} found");
                Console.WriteLine();

                if (devices.AuthorizedDoorbots.Count > 0)
                {
                    Console.WriteLine("Authorized Doorbells");
                    foreach (var device in devices.AuthorizedDoorbots)
                    {
                        Console.WriteLine($"{device.Id} - {device.Description}");
                    }
                    Console.WriteLine();
                }
                if (devices.Doorbots.Count > 0)
                {
                    Console.WriteLine("Doorbells");
                    foreach (var device in devices.Doorbots)
                    {
                        Console.WriteLine($"{device.Id} - {device.Description}");
                    }
                    Console.WriteLine();
                }
                if (devices.StickupCams.Count > 0)
                {
                    Console.WriteLine("Stickup cams");
                    foreach (var device in devices.StickupCams)
                    {
                        Console.WriteLine($"{device.Id} - {device.Description}");
                    }
                    Console.WriteLine();
                }
            }
            else
            {
                // Retrieve all sessions
                Console.WriteLine($"Downloading {(string.IsNullOrWhiteSpace(configuration.Type) ? "all" : configuration.Type)} historical events between {configuration.StartDate.Value:dddd d MMMM yyyy HH:mm:ss} and {(configuration.EndDate.HasValue ? configuration.EndDate.Value.ToString("dddd d MMMM yyyy HH:mm:ss") : "now")}{(configuration.RingDeviceId.HasValue ? $" for Ring device {configuration.RingDeviceId.Value}" : "")} using {configuration.MaxConcurrency} thread{(configuration.MaxConcurrency == 1 ? "" : "s")}");

                List<Api.Entities.DoorbotHistoryEvent> doorbotHistory = null;
                try
                {
                    doorbotHistory = await session.GetDoorbotsHistory(configuration.StartDate.Value, configuration.EndDate, configuration.RingDeviceId);
                }
                catch (Exception e) when ((e is AggregateException && e.InnerException != null && e.InnerException.Message.Contains("404")) || e is Api.Exceptions.DeviceUnknownException)
                {
                    Console.WriteLine($"No Ring device with Id {configuration.RingDeviceId} found under this account");
                    Environment.Exit(1);
                }

                // If a specific Type has been provided, filter out all the ones that don't match this type
                if (!string.IsNullOrWhiteSpace(configuration.Type))
                {
                    doorbotHistory = doorbotHistory.Where(h => h.Kind.Equals(configuration.Type, StringComparison.CurrentCultureIgnoreCase)).ToList();
                }

                // If we should only continue downloading from the most recently successfully downloaded recording, remove all entries that are older than this one
                if (configuration.ResumeFromLastDownload && !string.IsNullOrWhiteSpace(LastdownloadedRecordingId))
                {
                    // Try to find the last successfully downloaded item
                    var lastDownloadedItem = doorbotHistory.FirstOrDefault(h => h.Id.HasValue && h.Id.Value.ToString() == LastdownloadedRecordingId);

                    if (lastDownloadedItem != null && lastDownloadedItem.CreatedAtDateTime.HasValue)
                    {
                        // Only take all historical recordings which are newer than the last successfully downloaded historical item
                        Console.WriteLine($"Filtering for recordings newer than {lastDownloadedItem.CreatedAtDateTime.Value:dddd dd MMMM yyyy} at {lastDownloadedItem.CreatedAtDateTime.Value:HH:mm:ss}");
                        doorbotHistory = doorbotHistory.Where(h => h.CreatedAtDateTime.HasValue && h.CreatedAtDateTime.Value > lastDownloadedItem.CreatedAtDateTime.Value).ToList();
                    }
                }

                // Ensure we have items to download
                if (doorbotHistory.Count == 0)
                {
                    Console.WriteLine("No recordings found. Quitting.");
                    Environment.Exit(0);
                }

                Console.WriteLine($"{doorbotHistory.Count} item{(doorbotHistory.Count == 1 ? "" : "s")} found, downloading to {configuration.OutputPath}");
                Console.WriteLine("---");
                Log($"Beginning downloads...");
                await Task.Delay(1000);
                var startTime = DateTime.Now;

                // Ensure the downloads directory exists by creating it
                Directory.CreateDirectory(configuration.OutputPath);

                // Sort videos by oldest to newest (to start downloading the oldest items first)
                var sortedDoorbotHistory = doorbotHistory.OrderBy(h => h.CreatedAtDateTime).ToList();

                List<Task> downloadTasks = new List<Task>();

                for (var itemCount = 0; itemCount < sortedDoorbotHistory.Count; itemCount++)
                {
                    var doorbotHistoryItem = sortedDoorbotHistory[itemCount];
                    var localItemCount = itemCount + 1;

                    // If no valid date on the item, skip it and continue with the next
                    if (!doorbotHistoryItem.CreatedAtDateTime.HasValue) continue;

                    // Construct the filename and path where to save the file
                    var downloadFileName = $"{doorbotHistoryItem.Id}_{doorbotHistoryItem.CreatedAtDateTime.Value:yyyy_MM_dd-HH_mm_ss}.mp4";
                    var downloadFullPath = Path.Combine(configuration.OutputPath, downloadFileName);

                    // Create a new task for each download
                    Task downloadTask = Task.Run(async () =>
                    {
                        // Wait for our turn to proceed with download (concurrency control)
                        await semaphoreSlim.WaitAsync();

                        try
                        {
                            // Check if the file already exists
                            if (File.Exists(downloadFullPath))
                            {
                                Log($"Video {localItemCount}/{doorbotHistory.Count}: {downloadFileName} already exists. Skipping download.");
                            }
                            else
                            {
                                short attempt = 0;
                                bool success = false;
                                while (attempt <= configuration.MaxRetries && !success)
                                {
                                    attempt++;

                                    //Log($"Task {localItemCount}: Starting download for {downloadFileName}... ");

                                    try
                                    {
                                        // Download the recording
                                        await session.GetDoorbotHistoryRecording(doorbotHistoryItem, downloadFullPath);
                                        Log($"Video {localItemCount}/{doorbotHistory.Count}: Downloaded -> {downloadFileName} ({new FileInfo(downloadFullPath).Length / 1048576} MB)");
                                        success = true;

                                        // Store the Id of this historical item as the most recent downloaded item so it can download up to this one on a next attempt
                                        if ((localItemCount - 1) == 0 && doorbotHistoryItem.Id.HasValue)
                                        {
                                            LastdownloadedRecordingId = doorbotHistoryItem.Id.Value.ToString();
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        Log($"Video {localItemCount}/{doorbotHistory.Count}: Download failed -> {downloadFileName} :: ({e.Message}). Retrying attempt {attempt}/{configuration.MaxRetries}");
                                        await Task.Delay(1000); // Wait for 1 second before retrying
                                    }
                                }
                            }
                        }
                        finally
                        {
                            // Release the semaphore once the download is complete or has failed
                            semaphoreSlim.Release();
                        }
                    });

                    downloadTasks.Add(downloadTask);

                    // Wait for 1 second before starting the next download task
                    await Task.Delay(1000);
                }

                // Wait for all tasks to complete
                await Task.WhenAll(downloadTasks);

                // Calculate and display the time taken for all video downloads
                var elapsedTime = DateTime.Now - startTime;
                Log($"Finished downloading {doorbotHistory.Count} item{(doorbotHistory.Count == 1 ? "" : "s")} in {(int)elapsedTime.TotalSeconds} seconds.");
                Environment.Exit(0);
            }
        }

        /// <summary>
        /// Parses all provided arguments
        /// </summary>
        /// <param name="args">String array with arguments passed to this console application</param>
        private static Configuration ParseArguments(IList<string> args)
        {
            var configuration = new Configuration
            {
                Username = ConfigurationManager.AppSettings["RingUsername"],
                Password = ConfigurationManager.AppSettings["RingPassword"],
                OutputPath = Environment.CurrentDirectory
            };

            if (args.Contains("-out"))
            {
                configuration.OutputPath = args[args.IndexOf("-out") + 1];
            }

            if (args.Contains("-username"))
            {
                configuration.Username = args[args.IndexOf("-username") + 1];
            }

            if (args.Contains("-password"))
            {
                configuration.Password = args[args.IndexOf("-password") + 1];
            }

            if (args.Contains("-list"))
            {
                configuration.ListBots = true;
            }

            if (args.Contains("-type"))
            {
                configuration.Type = args[args.IndexOf("-type") + 1];
            }

            if (args.Contains("-lastdays"))
            {
                if (double.TryParse(args[args.IndexOf("-lastdays") + 1], out double lastDays))
                {
                    configuration.StartDate = DateTime.Now.AddDays(lastDays * -1);
                    configuration.EndDate = DateTime.Now;
                }
            }

            if (args.Contains("-startdate"))
            {
                if (DateTime.TryParse(args[args.IndexOf("-startdate") + 1], out DateTime startDate))
                {
                    configuration.StartDate = startDate;
                }
            }

            if (args.Contains("-enddate"))
            {
                if (DateTime.TryParse(args[args.IndexOf("-enddate") + 1], out DateTime endDate))
                {
                    configuration.EndDate = endDate;
                }
            }

            if (args.Contains("-retries"))
            {
                if (short.TryParse(args[args.IndexOf("-retries") + 1], out short maxRetries))
                {
                    configuration.MaxRetries = maxRetries;
                }
            }

            if (args.Contains("-deviceid"))
            {
                if (int.TryParse(args[args.IndexOf("-deviceid") + 1], out int deviceId))
                {
                    configuration.RingDeviceId = deviceId;
                }
            }

            if (args.Contains("-resumefromlastdownload"))
            {
                configuration.ResumeFromLastDownload = true;
            }

            if (args.Contains("-ignorecachedtoken"))
            {
                configuration.IgnoreCachedToken = true;
            }

            if (args.Contains("-threads"))
            {
                int index = args.IndexOf("-threads") + 1;
                if (index < args.Count && int.TryParse(args[index], out int threads))
                {
                    configuration.MaxConcurrency = threads;
                }
            }

            return configuration;
        }

        /// <summary>
        /// Shows the syntax
        /// </summary>
        private static void DisplayHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("   RingRecordingDownload -username <username> -password <password> [-out <folder location> -type <motion/ring/...> -lastdays X -startdate <date> -enddate <date>]");
            Console.WriteLine();
            Console.WriteLine("username: Username of the account to use to log on to Ring");
            Console.WriteLine("password: Password of the account to use to log on to Ring");
            Console.WriteLine("list: Request an overview of the Ring devices available in your account so you can get the deviceid value");
            Console.WriteLine("out: The folder where to store the recordings (optional, will use current directory if not specified)");
            Console.WriteLine("type: The type of events to store the recordings of, i.e. motion or ring (optional, will download them all if not specified)");
            Console.WriteLine("lastdays: The amount of days in the past to download recordings of (optional)");
            Console.WriteLine("startdate: Date and time from which to start downloading events (optional)");
            Console.WriteLine("enddate: Date and time until which to download events (optional, will use now if not specified)");
            Console.WriteLine("retries: Amount of retries on download failures (optional, will use 3 retries by default)");
            Console.WriteLine("deviceid: Id of the Ring device to download the recordings for (optional, will download for all registered Ring devices by default)");
            Console.WriteLine("resumefromlastdownload: If provided, it will try to start downloading recordings since the last successful download");
            Console.WriteLine("ignorecachedtoken: If provided, it will not use the cached token that may exist from a previous session");
            Console.WriteLine("threads: If provided, it will set the maximum number of concurrent download tasks allowed at a time. Defaults to 10.");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine("   RingRecordingDownload -username my@email.com -password mypassword -list");
            Console.WriteLine("   RingRecordingDownload -username my@email.com -password mypassword -lastdays 7");
            Console.WriteLine("   RingRecordingDownload -username my@email.com -password mypassword -lastdays 1 -resumefromlastdownload");
            Console.WriteLine("   RingRecordingDownload -username my@email.com -password mypassword -lastdays 7 -retries 5");
            Console.WriteLine("   RingRecordingDownload -username my@email.com -password mypassword -lastdays 7 -type ring");
            Console.WriteLine("   RingRecordingDownload -username my@email.com -password mypassword -lastdays 7 -type ring -out \"c:\\recordings path\"");
            Console.WriteLine("   RingRecordingDownload -username my@email.com -password mypassword -startdate \"12-02-2019 08:12:45\"");
            Console.WriteLine("   RingRecordingDownload -username my@email.com -password mypassword -startdate \"12-02-2019 08:12:45\" -enddate \"12-03-2019 10:53:12\"");
            Console.WriteLine("   RingRecordingDownload -username my@email.com -password mypassword -startdate \"12-02-2019 08:12:45\" -enddate \"12-03-2019 10:53:12\" -deviceId 1234567");
            Console.WriteLine();
        }
    }
}

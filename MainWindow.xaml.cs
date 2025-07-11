using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using FastDownloader;


namespace FastDownloader
{
    public static class Config
    {
        public static int MaxDegreeOfParallelism { get; set; } = 30;

        public static HttpClient HttpClient { get; } = new HttpClient();
    }
    public class AdvancedToolsJson
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public long size { get; set; }
        public string hash { get; set; } = "";
        public string algorithm { get; set; } = "";
        public bool encrypted { get; set; }
        public string password { get; set; } = "";
        public int numparts { get; set; }
        public List<string> listparts { get; set; } = new();
    }

    public class File
    {
        public string DownloadUrl { get; set; } = "";
        public string ParentFolder { get; set; } = "";
    }

    public partial class MainWindow : Window
    {
        public ObservableCollection<FileDownloadItem> Files { get; set; } = new();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            LoadFilesFromJson();
        }
        private async void LoadFilesFromJson()
        {
            try
            {
                string jsonUrl = "https://arsscriptum.github.io/files/fileshares-index/advanced-tools.json";
                var files = await LoadJsonAsync(jsonUrl);
                foreach (var f in files)
                {
                    Files.Add(f);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load JSON file: {ex.Message}");
            }
        }
        public async Task<List<FileDownloadItem>> LoadJsonAsync(string jsonUrl)
        {
            var response = await Config.HttpClient.GetAsync(jsonUrl);
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync();

            var model = JsonSerializer.Deserialize<AdvancedToolsJson>(jsonString);

            if (model == null || model.listparts == null)
                throw new Exception("JSON is invalid or missing listparts.");

            // Convert JSON URLs into FileDownloadItem objects
            var files = model.listparts.Select(url => new FileDownloadItem
            {
                Url = url,
                FileName = Path.GetFileName(url),
                Status = "Pending",
                Progress = 0
            }).ToList();

            return files;
        }

        public async Task<FileInfo> DownloadFile(
    File file,
    string rootDirectory,
    CancellationToken ct = default,
    Action<string>? updateStatus = null,
    Action<int>? reportProgress = null)
    {
        var sw = Stopwatch.StartNew();

        updateStatus?.Invoke("Downloading");

        string fileName = System.IO.Path.GetFileName(new Uri(file.DownloadUrl).LocalPath);
        string downloadPath = System.IO.Path.Combine(rootDirectory, fileName);

        using var response = await Config.HttpClient.GetAsync(file.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(downloadPath)!);

        using var httpStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        int read;
        long totalRead = 0;
        var totalBytes = response.Content.Headers.ContentLength ?? -1;

        while ((read = await httpStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            fileStream.Write(buffer, 0, read);
            totalRead += read;

            if (totalBytes > 0)
            {
                int percent = (int)(totalRead * 100 / totalBytes);
                reportProgress?.Invoke(percent);
            }
        }

        sw.Stop();

        // Format download time
        var duration = sw.Elapsed;
        string durationStr = $"{duration.TotalSeconds:F2}s";

        // Update status to show transfer time
        updateStatus?.Invoke($"Completed in {durationStr}");

        return new FileInfo(downloadPath);
    }

        private async void TestCombine_Click(object sender, RoutedEventArgs e)
        {
            string downloadRoot = @"C:\Users\guillaumep\AppData\Local\Temp\cf2ec66a-5a97-4606-aab6-11a0ec404bf5";
            string destination = @"C:\Users\guillaumep\AppData\Local\Temp\bmw_installer_package.rar.aes";

            try
            {
                await Task.Run(() =>
                {
                    FileUtils.CombineSplitFiles(downloadRoot, destination, type: "raw");
                });

                MessageBox.Show($"Combined file created:\n{destination}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error combining files:\n{ex.Message}");
            }
        }


        public async Task<List<FileInfo>> DownloadFiles(IEnumerable<File> fileList, string rootDirectory, CancellationToken ct = default)
        {
            var fileInfoBag = new ConcurrentBag<FileInfo>();

            var parallelOptions = new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Config.MaxDegreeOfParallelism
            };

            await Parallel.ForEachAsync(fileList, parallelOptions, async (file, ctx) =>
            {
                var fileName = System.IO.Path.GetFileName(new Uri(file.DownloadUrl).LocalPath);

                // Find matching FileDownloadItem
                var matchingItem = Files.FirstOrDefault(f => f.FileName == fileName);

                // Define how to update the status safely on the UI thread
                Action<string> updateStatus = (status) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (matchingItem != null)
                        {
                            matchingItem.Status = status;
                        }
                    });
                };
                Action<int> reportProgress = (percent) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (matchingItem != null)
                        {
                            matchingItem.Progress = percent;
                        }
                    });
                };
                var fileInfo = await DownloadFile(file, rootDirectory, ctx, updateStatus, reportProgress);
                fileInfoBag.Add(fileInfo);
            });


            return fileInfoBag.ToList();
        }


        private async void StartDownload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var swGlobal = Stopwatch.StartNew();

                string downloadRoot = @"C:\Users\guillaumep\AppData\Local\Temp\cf2ec66a-5a97-4606-aab6-11a0ec404bf5";
                Directory.CreateDirectory(downloadRoot);

                var filesToDownload = Files.Select(f => new File
                {
                    DownloadUrl = f.Url,
                    ParentFolder = ""
                }).ToList();

                var downloadedFiles = await DownloadFiles(filesToDownload, downloadRoot);

                foreach (var fileInfo in downloadedFiles)
                {
                    var matchingFile = Files.FirstOrDefault(f => f.FileName == fileInfo.Name);
                    if (matchingFile != null)
                    {
                        matchingFile.Progress = 100;
                        // Status already updated in DownloadFile
                    }
                }

                swGlobal.Stop();
                string globalDuration = $"{swGlobal.Elapsed.TotalSeconds:F2}s";

                MessageBox.Show($"All downloads completed in {globalDuration}.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error downloading files: {ex.Message}");
            }
        }


    }

    public class FileDownloadItem : DependencyObject
    {
        public string Url { get; set; } = "";
        public string FileName { get; set; } = "";

        public string Status
        {
            get => (string)GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }

        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register("Status", typeof(string), typeof(FileDownloadItem), new PropertyMetadata(""));

        public int Progress
        {
            get => (int)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }

        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register("Progress", typeof(int), typeof(FileDownloadItem), new PropertyMetadata(0));
    }

}

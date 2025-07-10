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

namespace FastDownloader
{
    public static class Config
    {
        public static int MaxDegreeOfParallelism { get; set; } = 30;

        public static HttpClient HttpClient { get; } = new HttpClient();
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

            for (int i = 1; i <= 200; i++)
            {
                string url = $"https://arsscriptum.github.io/files/advanced-tools-v2/data/bmw_installer_package.rar{i:D4}.cpp";
                Files.Add(new FileDownloadItem
                {
                    Url = url,
                    FileName = System.IO.Path.GetFileName(url),
                    Status = "Pending",
                    Progress = 0
                });
            }
        }
        public async Task<FileInfo> DownloadFile(
            File file,
            string rootDirectory,
            CancellationToken ct = default,
            Action<string>? updateStatus = null)
        {
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
                // optional: report progress
            }

            return new FileInfo(downloadPath);
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

                var fileInfo = await DownloadFile(file, rootDirectory, ctx, updateStatus);
                fileInfoBag.Add(fileInfo);
            });


            return fileInfoBag.ToList();
        }

        private async void StartDownload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string downloadRoot = @"C:\Temp\DownloadedFiles";
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
                        matchingFile.Status = "Completed";
                        matchingFile.Progress = 100;
                    }
                }

                MessageBox.Show("All downloads completed!");
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

using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;


namespace FastDownloader
{
    public static class FileUtils
    {
        public static List<string> SortLexically(IEnumerable<string> items)
        {
            return items
                .OrderBy(s =>
                {
                    var name = Path.GetFileName(s);
                    var match = System.Text.RegularExpressions.Regex.Match(name, @"(\d+)(?=\D*$)");

                    if (match.Success)
                        return int.Parse(match.Groups[1].Value);
                    else
                        return int.MaxValue;
                })
                .ThenBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> SortByFileHeaderId(IEnumerable<string> paths)
        {
            var fileInfos = new List<(string Path, int PartId)>();

            foreach (var path in paths)
            {
                try
                {
                    var header = FileHeaderUtils.ReadFileHeader(path);
                    fileInfos.Add((path, header.PartID));
                }
                catch
                {
                    Console.WriteLine($"Skipping invalid header in {path}");
                }
            }

            return fileInfos
                .OrderBy(x => x.PartId)
                .Select(x => x.Path)
                .ToList();
        }

        public static void SplitDataFile(
            string path,
            long newSize,
            string outPath = "",
            string extension = "cpp",
            bool asBase64 = false)
        {
            if (newSize <= 0)
                throw new ArgumentException("Only positive sizes allowed");

            if (string.IsNullOrWhiteSpace(outPath))
                outPath = Path.GetDirectoryName(path)!;

            Directory.CreateDirectory(outPath);

            var filename = Path.GetFileNameWithoutExtension(path);

            long fileSize = new FileInfo(path).Length;
            int numFile = 1;

            const int maxBufferSize = 1 * 1024 * 1024 * 1024; // 1 GB

            using var reader = new FileStream(path, FileMode.Open, FileAccess.Read);

            byte[] buffer = new byte[Math.Min(newSize, maxBufferSize)];
            int bytesRead;

            while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                string newName = Path.Combine(outPath,
                    $"{filename}{numFile:D4}.{extension.TrimStart('.')}");

                if (asBase64)
                {
                    string base64 = Convert.ToBase64String(buffer, 0, bytesRead);
                    System.IO.File.WriteAllText(newName, base64);
                }
                else
                {
                    System.IO.File.WriteAllBytes(newName, buffer.Take(bytesRead).ToArray());
                }

                FileHeaderUtils.WriteFileHeader(newName, numFile);

                numFile++;
            }
        }

        public static void CombineSplitFiles(
            string path,
            string destination,
            string type = "base64")
        {
            bool encodedAsString = type.Equals("base64", StringComparison.OrdinalIgnoreCase);

            var files = Directory
                .GetFiles(path, "*.cpp")
                .ToList();

            var sortedFiles = SortByFileHeaderId(files);

            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write);

            foreach (var file in sortedFiles)
            {
                if (!System.IO.File.Exists(file))
                    throw new FileNotFoundException($"Missing file: {file}");

                var headerData = FileHeaderUtils.RemoveFileHeader(file);

                byte[] data;

                if (encodedAsString)
                {
                    var base64 = System.IO.File.ReadAllText(file);
                    data = Convert.FromBase64String(base64);
                }
                else
                {
                    data = System.IO.File.ReadAllBytes(file);
                }

                output.Write(data, 0, data.Length);
            }

            Console.WriteLine($"Recombined Successfully! Wrote combined file to {destination}");
        }
    }
}

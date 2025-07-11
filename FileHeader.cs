using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.IO;

namespace FastDownloader
{
    public class FileHeaderMetadata
    {
        public string Path { get; set; } = "";
        public int PartID { get; set; }
        public long DataSize { get; set; }
        public string Hash { get; set; } = "";
    }

    public static class FileHeaderUtils
    {
        private static readonly byte[] Magic = new byte[]
        {
            0x42, 0x4D, 0x57, 0x21, 0x2A, 0x4D, 0x53, 0x47
        };

        public static FileHeaderMetadata ReadFileHeader(string path)
        {
            using var reader = new BinaryReader(System.IO.File.OpenRead(path));

            var magicStart = reader.ReadBytes(8);
            if (!magicStart.SequenceEqual(Magic))
                throw new InvalidDataException($"Invalid or missing header magic number at start of file: {path}");

            int partId = reader.ReadInt32();
            long dataSize = reader.ReadInt64();
            byte[] hashBytes = reader.ReadBytes(32);

            var magicEnd = reader.ReadBytes(8);
            if (!magicEnd.SequenceEqual(Magic))
                throw new InvalidDataException($"Invalid or missing header magic number at end of header: {path}");

            return new FileHeaderMetadata
            {
                Path = path,
                PartID = partId,
                DataSize = dataSize,
                Hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower()
            };
        }

        public static FileHeaderMetadata RemoveFileHeader(string path)
        {
            string tempPath = path + ".raw";

            using var reader = new BinaryReader(System.IO.File.OpenRead(path));

            // Read and validate start magic
            var magicStart = reader.ReadBytes(8);
            if (!magicStart.SequenceEqual(Magic))
                throw new InvalidDataException($"Invalid or missing header magic number at start of file: {path}");

            int partId = reader.ReadInt32();
            long dataSize = reader.ReadInt64();
            byte[] hashBytes = reader.ReadBytes(32);

            var magicEnd = reader.ReadBytes(8);
            if (!magicEnd.SequenceEqual(Magic))
                throw new InvalidDataException($"Invalid or missing header magic number at end of header: {path}");

            // Read payload
            byte[] payload = reader.ReadBytes((int)dataSize);

            // Write payload to temp file
            using (var writer = new BinaryWriter(System.IO.File.Create(tempPath)))
            {
                writer.Write(payload);
            }

            // Replace original file
            System.IO.File.Delete(path);
            System.IO.File.Move(tempPath, path);

            return new FileHeaderMetadata
            {
                Path = path,
                PartID = partId,
                DataSize = dataSize,
                Hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower()
            };
        }

        public static void WriteFileHeader(string path, int partId)
        {
            byte[] data = System.IO.File.ReadAllBytes(path);

            long dataSize = data.Length;

            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(data);

            string tempPath = path + ".tmp";

            using (var writer = new BinaryWriter(System.IO.File.Create(tempPath)))
            {
                writer.Write(Magic);
                writer.Write(partId);
                writer.Write(dataSize);
                writer.Write(hashBytes);
                writer.Write(Magic);
                writer.Write(data, 0, data.Length);
            }

            System.IO.File.Delete(path);
            System.IO.File.Move(tempPath, path);
        }
    }
}

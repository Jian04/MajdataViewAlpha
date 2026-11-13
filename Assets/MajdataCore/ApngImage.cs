using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MajdataCore
{
    // Rebuild each APNG sub-frame as an ordinary PNG. Pixel decoding stays with
    // the platform image decoder; offsets, blend and disposal belong to the caller.
    public sealed class ApngImage
    {
        public sealed class Frame
        {
            public int Width, Height, X, Y;
            public double Duration;
            public byte Dispose, Blend;
            public byte[] Png;
        }
        public int Width, Height;
        public uint Loops;
        public readonly List<Frame> Frames = new();
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static ApngImage Read(byte[] bytes)
        {
            if (bytes.Length < 33) throw new InvalidDataException("APNG 文件不完整。");
            for (var i = 0; i < Signature.Length; i++)
                if (bytes[i] != Signature[i]) throw new InvalidDataException("不是 PNG/APNG 文件。");
            var image = new ApngImage();
            byte[] header = null;
            var paletteChunks = new List<byte[]>();
            var data = new MemoryStream();
            Frame frame = null;
            void FinishFrame()
            {
                if (frame == null) return;
                if (data.Length == 0) throw new InvalidDataException("APNG 帧缺少图像数据。");
                using var png = new MemoryStream();
                png.Write(Signature, 0, Signature.Length);
                var dimensions = (byte[])header.Clone();
                Put32(dimensions, 0, frame.Width); Put32(dimensions, 4, frame.Height);
                WriteChunk(png, "IHDR", dimensions);
                foreach (var chunk in paletteChunks) png.Write(chunk, 0, chunk.Length);
                WriteChunk(png, "IDAT", data.ToArray());
                WriteChunk(png, "IEND", Array.Empty<byte>());
                frame.Png = png.ToArray();
                image.Frames.Add(frame);
                data.SetLength(0); data.Position = 0;
            }
            for (var at = 8; at + 12 <= bytes.Length;)
            {
                var length = Get32(bytes, at);
                if (length < 0 || length > bytes.Length - at - 12) throw new InvalidDataException("APNG 数据块越界。");
                var kind = Encoding.ASCII.GetString(bytes, at + 4, 4);
                var start = at + 8;
                switch (kind)
                {
                    case "IHDR":
                        if (length != 13) throw new InvalidDataException("PNG 文件头无效。");
                        header = new byte[13]; Array.Copy(bytes, start, header, 0, 13);
                        image.Width = Get32(header, 0); image.Height = Get32(header, 4);
                        break;
                    case "acTL":
                        if (length != 8) throw new InvalidDataException("APNG 动画头无效。");
                        image.Loops = unchecked((uint)Get32(bytes, start + 4));
                        break;
                    case "fcTL":
                        FinishFrame();
                        if (length != 26 || header == null) throw new InvalidDataException("APNG 帧控制块无效。");
                        frame = new Frame { Width = Get32(bytes, start + 4), Height = Get32(bytes, start + 8),
                            X = Get32(bytes, start + 12), Y = Get32(bytes, start + 16),
                            Duration = Get16(bytes, start + 20) / (double)(Get16(bytes, start + 22) == 0 ? 100 : Get16(bytes, start + 22)),
                            Dispose = bytes[start + 24], Blend = bytes[start + 25] };
                        if (frame.Width <= 0 || frame.Height <= 0 || frame.X < 0 || frame.Y < 0 ||
                            frame.X > image.Width - frame.Width || frame.Y > image.Height - frame.Height || frame.Dispose > 2 || frame.Blend > 1)
                            throw new InvalidDataException("APNG 帧尺寸或合成方式无效。");
                        break;
                    case "IDAT":
                        if (frame != null) data.Write(bytes, start, length);
                        break;
                    case "fdAT":
                        if (frame == null || length < 4) throw new InvalidDataException("APNG 帧数据无效。");
                        data.Write(bytes, start + 4, length - 4);
                        break;
                    case "PLTE": case "tRNS":
                        var copy = new byte[length + 12]; Array.Copy(bytes, at, copy, 0, copy.Length); paletteChunks.Add(copy);
                        break;
                    case "IEND":
                        FinishFrame(); data.Dispose(); return image;
                }
                at += length + 12;
            }
            data.Dispose();
            throw new InvalidDataException("APNG 文件缺少结束块。");
        }

        private static int Get16(byte[] b, int i) => b[i] << 8 | b[i + 1];
        private static int Get32(byte[] b, int i) => b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3];
        private static void Put32(byte[] b, int i, int value)
        { b[i] = (byte)(value >> 24); b[i + 1] = (byte)(value >> 16); b[i + 2] = (byte)(value >> 8); b[i + 3] = (byte)value; }
        private static readonly uint[] CrcTable = BuildCrcTable();
        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                var value = i;
                for (var bit = 0; bit < 8; bit++) value = (value & 1) == 0 ? value >> 1 : 0xedb88320u ^ (value >> 1);
                table[i] = value;
            }
            return table;
        }
        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            var chunk = new byte[data.Length + 12]; Put32(chunk, 0, data.Length);
            Encoding.ASCII.GetBytes(type, 0, 4, chunk, 4); Array.Copy(data, 0, chunk, 8, data.Length);
            var crc = uint.MaxValue;
            for (var i = 4; i < chunk.Length - 4; i++) crc = CrcTable[(crc ^ chunk[i]) & 255] ^ (crc >> 8);
            Put32(chunk, chunk.Length - 4, unchecked((int)~crc)); output.Write(chunk, 0, chunk.Length);
        }
    }
}

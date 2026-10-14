using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OsuBeatmapEditor.Game.Library
{
    /// <summary>The song title / artist stored inside an audio file's tags (either may be null).</summary>
    public sealed record AudioTagInfo(string? Title, string? Artist)
    {
        public bool HasAny => Title != null || Artist != null;
    }

    /// <summary>
    /// Minimal tag reader for the formats osu! maps use: ID3v2 / ID3v1 (mp3) and Vorbis / Opus comments (ogg). Read-only,
    /// never throws, and never reads more than a few MB, so a damaged or hostile file just yields "no tags".
    /// </summary>
    public static class AudioTags
    {
        private const int max_tag_bytes = 8 * 1024 * 1024;

        public static AudioTagInfo Read(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] head = new byte[4];
                int n = fs.Read(head, 0, 4);
                fs.Position = 0;

                if (n == 4 && head[0] == 'O' && head[1] == 'g' && head[2] == 'g' && head[3] == 'S')
                    return readOgg(fs);

                var v2 = readId3v2(fs);
                if (v2.HasAny)
                    return v2;

                return readId3v1(fs);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
            {
                return new AudioTagInfo(null, null);
            }
        }

        // ---------------------------------------------------------------- ID3v2

        private static AudioTagInfo readId3v2(Stream s)
        {
            var none = new AudioTagInfo(null, null);

            byte[] h = new byte[10];
            s.Position = 0;
            if (s.Read(h, 0, 10) != 10 || h[0] != 'I' || h[1] != 'D' || h[2] != '3')
                return none;

            int major = h[3];
            if (major < 2 || major > 4)
                return none;

            byte flags = h[5];
            int size = syncsafe(h, 6);
            if (size <= 0 || size > max_tag_bytes)
                return none;

            byte[] tag = new byte[size];
            int got = readFully(s, tag);
            if (got < size)
                Array.Resize(ref tag, got);

            if ((flags & 0x80) != 0 && major < 4)
                tag = unsync(tag); // v2.3 and v2.2 unsynchronise the whole tag

            int pos = 0;
            if ((flags & 0x40) != 0 && major >= 3 && tag.Length >= 4) // extended header
            {
                int ext = major == 4 ? syncsafe(tag, 0) : 4 + beInt(tag, 0, 4);
                if (ext < 4 || ext > tag.Length)
                    return none;
                pos = ext;
            }

            int idLen = major == 2 ? 3 : 4;
            int headerLen = major == 2 ? 6 : 10;
            string? title = null, artist = null;

            while (pos + headerLen <= tag.Length)
            {
                if (tag[pos] == 0)
                    break; // padding

                string id = Encoding.ASCII.GetString(tag, pos, idLen);
                int frameSize = major switch
                {
                    2 => beInt(tag, pos + 3, 3),
                    3 => beInt(tag, pos + 4, 4),
                    _ => syncsafe(tag, pos + 4),
                };
                byte f2 = major >= 3 ? tag[pos + 9] : (byte)0;
                int dataStart = pos + headerLen;

                if (frameSize < 0 || (long)dataStart + frameSize > tag.Length)
                    break;

                bool wanted = id is "TIT2" or "TT2" or "TPE1" or "TP1";
                // Compressed / encrypted frames (v2.3 bits 0x80/0x40 of the second flag byte, v2.4 0x08/0x04) are skipped.
                bool unreadable = major == 3 ? (f2 & 0xC0) != 0 : major == 4 && (f2 & 0x0C) != 0;

                if (wanted && !unreadable && frameSize > 1)
                {
                    int start = dataStart, len = frameSize;
                    if (major == 4 && (f2 & 0x01) != 0 && len > 4) // data length indicator
                    {
                        start += 4;
                        len -= 4;
                    }

                    byte[] data = new byte[len];
                    Array.Copy(tag, start, data, 0, len);
                    if (major == 4 && (f2 & 0x02) != 0)
                        data = unsync(data);

                    string? text = decodeText(data);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (id is "TIT2" or "TT2")
                            title ??= text;
                        else
                            artist ??= text;
                    }
                }

                pos = dataStart + frameSize;
            }

            return new AudioTagInfo(title, artist);
        }

        private static string? decodeText(byte[] data)
        {
            if (data.Length < 2)
                return null;

            byte enc = data[0];
            Encoding encoding;
            int start = 1;

            switch (enc)
            {
                case 0:
                    encoding = Encoding.Latin1;
                    break;

                case 1:
                    if (data.Length >= 3 && data[1] == 0xFF && data[2] == 0xFE)
                    {
                        encoding = new UnicodeEncoding(false, false);
                        start = 3;
                    }
                    else if (data.Length >= 3 && data[1] == 0xFE && data[2] == 0xFF)
                    {
                        encoding = new UnicodeEncoding(true, false);
                        start = 3;
                    }
                    else
                    {
                        encoding = new UnicodeEncoding(false, false);
                    }

                    break;

                case 2:
                    encoding = new UnicodeEncoding(true, false);
                    break;

                case 3:
                    encoding = new UTF8Encoding(false);
                    break;

                default:
                    return null;
            }

            string text = encoding.GetString(data, start, data.Length - start);
            int nul = text.IndexOf('\0');
            if (nul >= 0)
                text = text.Substring(0, nul); // several values are separated by NUL: the first is the main one

            text = text.Trim().Trim('﻿').Trim();
            return text.Length == 0 ? null : text;
        }

        // ---------------------------------------------------------------- ID3v1

        private static AudioTagInfo readId3v1(Stream s)
        {
            if (s.Length < 128)
                return new AudioTagInfo(null, null);

            s.Position = s.Length - 128;
            byte[] b = new byte[128];
            if (readFully(s, b) < 128 || b[0] != 'T' || b[1] != 'A' || b[2] != 'G')
                return new AudioTagInfo(null, null);

            string? field(int off) => Encoding.Latin1.GetString(b, off, 30).Split('\0')[0].Trim() is { Length: > 0 } t ? t : null;
            return new AudioTagInfo(field(3), field(33));
        }

        // ---------------------------------------------------------------- Ogg (Vorbis / Opus comments)

        private static AudioTagInfo readOgg(Stream s)
        {
            var none = new AudioTagInfo(null, null);
            var packets = new List<byte[]>();
            var current = new MemoryStream();
            long budget = max_tag_bytes;

            s.Position = 0;
            byte[] ph = new byte[27];

            while (packets.Count < 2 && budget > 0)
            {
                if (readFully(s, ph) < 27 || ph[0] != 'O' || ph[1] != 'g' || ph[2] != 'g' || ph[3] != 'S')
                    return none;

                int segCount = ph[26];
                byte[] segs = new byte[segCount];
                if (readFully(s, segs) < segCount)
                    return none;

                foreach (byte seg in segs)
                {
                    budget -= seg;
                    if (budget < 0)
                        return none;

                    byte[] data = new byte[seg];
                    if (readFully(s, data) < seg)
                        return none;

                    current.Write(data, 0, seg);

                    if (seg < 255)
                    {
                        packets.Add(current.ToArray());
                        current = new MemoryStream();
                        if (packets.Count >= 2)
                            break;
                    }
                }
            }

            if (packets.Count < 2)
                return none;

            byte[] p = packets[1];
            int pos;
            if (p.Length > 7 && p[0] == 3 && Encoding.ASCII.GetString(p, 1, 6) == "vorbis")
                pos = 7;
            else if (p.Length > 8 && Encoding.ASCII.GetString(p, 0, 8) == "OpusTags")
                pos = 8;
            else
                return none;

            string? title = null, artist = null;

            if (pos + 4 > p.Length)
                return none;
            long vendor = leInt(p, pos);
            pos += 4;
            if (vendor < 0 || pos + vendor > p.Length)
                return none;
            pos += (int)vendor;

            if (pos + 4 > p.Length)
                return none;
            long count = leInt(p, pos);
            pos += 4;

            for (long i = 0; i < count && pos + 4 <= p.Length; i++)
            {
                long len = leInt(p, pos);
                pos += 4;
                if (len < 0 || pos + len > p.Length)
                    break;

                string comment = Encoding.UTF8.GetString(p, pos, (int)len);
                pos += (int)len;

                int eq = comment.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = comment.Substring(0, eq).ToUpperInvariant();
                string value = comment.Substring(eq + 1).Trim();
                if (value.Length == 0)
                    continue;

                if (key == "TITLE")
                    title ??= value;
                else if (key == "ARTIST")
                    artist ??= value;
            }

            return new AudioTagInfo(title, artist);
        }

        // ---------------------------------------------------------------- helpers

        private static int readFully(Stream s, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int n = s.Read(buffer, total, buffer.Length - total);
                if (n <= 0)
                    break;
                total += n;
            }

            return total;
        }

        private static int syncsafe(byte[] b, int o) => o + 4 > b.Length ? -1 : (b[o] & 0x7F) << 21 | (b[o + 1] & 0x7F) << 14 | (b[o + 2] & 0x7F) << 7 | (b[o + 3] & 0x7F);

        private static int beInt(byte[] b, int o, int n)
        {
            if (o + n > b.Length)
                return -1;
            long v = 0;
            for (int i = 0; i < n; i++)
                v = v << 8 | b[o + i];
            return v > int.MaxValue ? -1 : (int)v;
        }

        private static long leInt(byte[] b, int o) => (long)b[o] | (long)b[o + 1] << 8 | (long)b[o + 2] << 16 | (long)b[o + 3] << 24;

        private static byte[] unsync(byte[] b)
        {
            var o = new List<byte>(b.Length);
            for (int i = 0; i < b.Length; i++)
            {
                o.Add(b[i]);
                if (b[i] == 0xFF && i + 1 < b.Length && b[i + 1] == 0)
                    i++;
            }

            return o.ToArray();
        }
    }
}

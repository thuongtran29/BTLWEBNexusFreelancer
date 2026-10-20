using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace NexusFreelance.Web.Services
{
    // Structural validation is defense in depth, not a substitute for malware scanning.
    public static class WorkspaceContentPolicy
    {
        public const int MaxBytes = 8 * 1024 * 1024;
        private const int ExpandedLimit = 64 * 1024 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static string Validate(string name, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxBytes) throw Invalid();
            var extension = Path.GetExtension(name ?? "").ToLowerInvariant();
            try
            {
                if (extension == ".txt") { Text(bytes); return "text/plain"; }
                if (extension == ".pdf")
                {
                    var start = Encoding.ASCII.GetString(bytes, 0, Math.Min(12, bytes.Length));
                    var end = Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 1024), Math.Min(1024, bytes.Length)).TrimEnd();
                    if (!(start.StartsWith("%PDF-1.") || start.StartsWith("%PDF-2.")) || !end.EndsWith("%%EOF", StringComparison.Ordinal)) throw Invalid();
                    return "application/pdf";
                }
                if (new[] { ".png", ".jpg", ".jpeg" }.Contains(extension))
                {
                    var png = extension == ".png";
                    var pngHeader = new byte[] {137,80,78,71,13,10,26,10};
                    var pngEnd = new byte[] {0,0,0,0,73,69,78,68,174,66,96,130};
                    if (png ? bytes.Length < 20 || !bytes.Take(8).SequenceEqual(pngHeader) || !bytes.Skip(bytes.Length - 12).SequenceEqual(pngEnd) :
                        bytes.Length < 4 || bytes[0] != 255 || bytes[1] != 216 || bytes[bytes.Length - 2] != 255 || bytes[bytes.Length - 1] != 217) throw Invalid();
                    using (var stream = new MemoryStream(bytes))
                    using (var image = Image.FromStream(stream, false, true))
                    {
                        if (image.Width > 8192 || image.Height > 8192 || (long)image.Width * image.Height > 24000000 ||
                            image.RawFormat.Guid != (png ? System.Drawing.Imaging.ImageFormat.Png.Guid : System.Drawing.Imaging.ImageFormat.Jpeg.Guid)) throw Invalid();
                    }
                    return png ? "image/png" : "image/jpeg";
                }
                if (new[] { ".zip", ".docx", ".xlsx" }.Contains(extension))
                { Archive(bytes, extension); return extension == ".zip" ? "application/zip" :
                    "application/vnd.openxmlformats-officedocument." + (extension == ".docx" ? "wordprocessingml.document" : "spreadsheetml.sheet"); }
            }
            catch (InvalidDataException) { throw Invalid(); }
            catch (ArgumentException) { throw Invalid(); }
            catch (XmlException) { throw Invalid(); }
            catch (OutOfMemoryException) { throw Invalid(); } // GDI+ uses this for invalid image formats.
            throw Invalid();
        }
        private static void Text(byte[] bytes)
        {
            var text = Utf8.GetString(bytes);
            if (text.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')) throw Invalid();
        }
        private static void Archive(byte[] bytes, string extension)
        {
            if (bytes.Length < 4 || bytes[0] != 80 || bytes[1] != 75 || bytes[2] != 3 || bytes[3] != 4) throw Invalid();
            using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            {
                if (archive.Entries.Count == 0 || archive.Entries.Count > 400) throw Invalid();
                long expanded = 0;
                var paths = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    var path = entry.FullName.Replace('\\', '/');
                    if (path.Length > 240 || path.StartsWith("/") || path.Contains(":") || path.Any(char.IsControl) ||
                        path.Split('/').Any(p => p == "." || p == "..") || !paths.Add(path) || entry.Length > ExpandedLimit ||
                        new[] { ".exe", ".dll", ".com", ".scr", ".msi", ".lnk", ".bat", ".cmd", ".ps1", ".vbs", ".hta", ".docm", ".xlsm" }.Contains(Path.GetExtension(path).ToLowerInvariant()) ||
                        path.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)) throw Invalid();
                    expanded += entry.Length;
                    if (expanded > ExpandedLimit || (entry.Length > 1024 * 1024 && entry.Length > Math.Max(1, entry.CompressedLength) * 200L)) throw Invalid();
                    // Read every entry: corrupted or encrypted payloads must not pass by metadata alone.
                    long actual = 0;
                    using (var input = entry.Open()) { var block = new byte[8192]; int count;
                        while ((count = input.Read(block, 0, block.Length)) > 0) { actual += count; if (actual > entry.Length || actual > ExpandedLimit) throw Invalid(); } }
                    if (actual != entry.Length) throw Invalid();
                }
                if (extension == ".zip") return;
                var document = extension == ".docx" ? "word/document.xml" : "xl/workbook.xml";
                if (!paths.Contains("[Content_Types].xml") || !paths.Contains("_rels/.rels") || !paths.Contains(document)) throw Invalid();
                // Parse package XML with DTD/external entities disabled and a bounded character count.
                foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
                using (var input = entry.Open())
                using (var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = ExpandedLimit }))
                    while (reader.Read()) { if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Override" &&
                        (reader.GetAttribute("ContentType") ?? "").IndexOf("macroEnabled", StringComparison.OrdinalIgnoreCase) >= 0) throw Invalid(); }
            }
        }
        private static InvalidDataException Invalid() => new InvalidDataException("Tệp không hợp lệ hoặc nội dung không khớp định dạng. Dùng PDF, ảnh PNG/JPEG, TXT UTF-8 hoặc ZIP/DOCX/XLSX không có mã thực thi; tối đa 8 MB, 400 mục và 64 MB sau giải nén.");
    }
}

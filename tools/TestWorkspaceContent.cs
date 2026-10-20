using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using NexusFreelance.Web.Services;
class TestWorkspaceContent
{
    static int count;
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
    static void Reject(string file, byte[] data, string label) { try { WorkspaceContentPolicy.Validate(file, data); throw new Exception("Accepted: " + label); } catch (InvalidDataException) { Check(true, label); } }
    static byte[] Zip(params string[] values)
    {
        using (var memory = new MemoryStream()) { using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            for (var i = 0; i < values.Length; i += 2) using (var writer = new StreamWriter(zip.CreateEntry(values[i]).Open())) writer.Write(values[i+1]);
            return memory.ToArray(); }
    }
    static byte[] Text(string text) { return Encoding.UTF8.GetBytes(text); }
    public static void Main()
    {
        Check(WorkspaceContentPolicy.Validate("ghi-chu.txt", Text("Tài liệu tiếng Việt\r\nNội dung\tOK")) == "text/plain", "UTF8 Vietnamese text");
        Reject("bad.txt", new byte[] {0,255,22}, "Binary disguised as text");
        Reject("bad.txt", new byte[] {0xc3,0x28}, "Malformed UTF8");
        Reject("fake.png", Text("not an image"), "Spoof PNG blocked");
        Reject("fake.jpg", Text("not an image"), "Spoof JPEG blocked");
        using (var bitmap = new Bitmap(2,2)) using (var stream = new MemoryStream()) {
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png); var png = stream.ToArray();
            Check(WorkspaceContentPolicy.Validate("valid.png", png) == "image/png", "Real decoded PNG accepted");
            Reject("wrong.jpg", png, "Mismatched image extension");
            Reject("trailing.png", Combine(png, Text("payload")), "Appended PNG data blocked");
            Reject("truncated.png", new ArraySegment<byte>(png,0,png.Length-4).ToArray(), "Truncated PNG blocked");
        }
        Check(WorkspaceContentPolicy.Validate("minimal.pdf", Text("%PDF-1.7\n%%EOF\n")) == "application/pdf", "PDF signature and EOF");
        Reject("spoof.pdf", Text("hello"), "Spoof PDF blocked");
        Reject("bad.pdf", Text("%PDF-1.7\ntruncated"), "Truncated PDF blocked");
        Check(WorkspaceContentPolicy.Validate("source.zip", Zip("src/app.js","console.log('demo');")) == "application/zip", "Source archive accepted");
        Reject("path.zip", Zip("../escape.txt","a"), "ZIP traversal blocked");
        Reject("path.zip", Zip("C:/escape.txt","a"), "ZIP absolute path blocked");
        Reject("dupe.zip", Zip("a.txt","a","A.txt","b"), "Ambiguous ZIP paths blocked");
        Reject("exe.zip", Zip("run.exe","MZ"), "Executable ZIP blocked");
        Reject("fake.docx", Zip("readme.txt","not office"), "Renamed ZIP is not DOCX");
        var office = Zip("[Content_Types].xml","<Types />","_rels/.rels","<Relationships />","word/document.xml","<document />");
        Check(WorkspaceContentPolicy.Validate("doc.docx", office).Contains("wordprocessingml"), "Structured XML DOCX accepted");
        Reject("wrong.xlsx", office, "DOCX cannot masquerade as XLSX");
        Reject("dtd.docx", Zip("[Content_Types].xml","<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///etc/passwd'>]><Types>&a;</Types>","_rels/.rels","<Relationships />","word/document.xml","<document />"), "DTD and XXE blocked");
        Reject("macro.docx", Zip("[Content_Types].xml","<Types />","_rels/.rels","<Relationships />","word/document.xml","<document />","word/vbaProject.bin","macro"), "Office macro package blocked");
        Reject("bomb.zip", Zip("large.txt",new string('a',2*1024*1024)), "Extreme compression ratio blocked");
        Reject("empty.zip", Zip(), "Empty archive blocked");
        Reject("large.txt", new byte[WorkspaceContentPolicy.MaxBytes+1], "8MB size limit");
        Reject("bad.svg", Text("<svg/>"), "Unsupported active image type");
        Console.WriteLine("CONTENT_CHECKS="+count);
    }
    static byte[] Combine(byte[] a, byte[] b) { var all=new byte[a.Length+b.Length];Buffer.BlockCopy(a,0,all,0,a.Length);Buffer.BlockCopy(b,0,all,a.Length,b.Length);return all; }
}
static class ArrayExtensions { public static byte[] ToArray(this ArraySegment<byte> a) { var bytes=new byte[a.Count];Buffer.BlockCopy(a.Array,a.Offset,bytes,0,a.Count);return bytes; } }

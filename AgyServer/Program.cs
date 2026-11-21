using System.Diagnostics;
using System.Text;

// agy-talk server — รับไฟล์เสียงจากแอป → ส่งให้ agy ฟัง → ตอบกลับเป็นข้อความ
// ฟัง localhost:8791 (แอปมือถือเข้าผ่าน tailnet: 100.73.5.82:8791)

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(8791));
var app = builder.Build();

app.UseDefaultFiles();
var mime = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
mime.Mappings[".apk"] = "application/vnd.android.package-archive";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = mime });

var gate = new SemaphoreSlim(1, 1); // agy ทีละ 1 คำขอ
var workDir = Path.Combine(Path.GetTempPath(), "agy-talk");
Directory.CreateDirectory(workDir);

app.MapGet("/api/ping", () => Results.Ok(new { ok = true, now = DateTime.Now }));

app.MapPost("/ask", async (HttpRequest req) =>
{
    if (!gate.Wait(0))
        return Results.Conflict(new { error = "มีคำขอก่อนหน้ากำลังประมวลผลอยู่ รอสักครู่แล้วลองใหม่" });

    string? savedPath = null;
    try
    {
        string prompt;

        if (req.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            // ใหม่ (v1.1): มือถือถอดเสียงเป็นข้อความเองแล้ว (SpeechRecognizer บนเครื่อง)
            // — ตัดต้นทุนอัปโหลดเสียง+โมเดลฟังเสียงที่เคยกิน ~9 วิ ออกทั้งหมด
            var body = await req.ReadFromJsonAsync<AskBody>();
            var text = body?.Text?.Trim();
            if (string.IsNullOrEmpty(text))
                return Results.BadRequest(new { error = "ฟิลด์ text ว่าง" });

            prompt =
                "ผู้ใช้กำลังคุยกับผู้ช่วย AI ผ่านแอปมือถือด้วยเสียง " +
                "(ข้อความต่อไปนี้คือคำพูดของผู้ใช้ ถอดจากเสียงบนมือถือแล้ว): " +
                $"\"{text}\" — " +
                "ตอบกลับตรง ๆ ตอบสั้น ๆ ไม่เกิน 3 ประโยค " +
                "ด้วยภาษาเดียวกับที่ผู้ใช้พูด ไม่ต้องทำงานอื่นใดนอกจากตอบ";
        }
        else if (req.HasFormContentType)
        {
            // เดิม (v1.0): รับไฟล์เสียงตรง ๆ ให้ agy ฟังเอง — เก็บไว้เป็น fallback
            var form = await req.ReadFormAsync();
            var audio = form.Files["audio"];
            if (audio is null || audio.Length == 0)
                return Results.BadRequest(new { error = "ต้องแนบไฟล์เสียงในฟิลด์ 'audio'" });
            if (audio.Length > 20_000_000)
                return Results.BadRequest(new { error = "ไฟล์ใหญ่เกิน 20MB" });

            var ext = Path.GetExtension(audio.FileName);
            if (string.IsNullOrEmpty(ext)) ext = ".webm";
            savedPath = Path.Combine(workDir, Guid.NewGuid().ToString("N") + ext);
            await using (var fs = File.Create(savedPath))
                await audio.CopyToAsync(fs);

            prompt =
                $"ฟังไฟล์เสียง {Path.GetFileName(savedPath)} ในโฟลเดอร์ปัจจุบัน " +
                "นี่คือเสียงพูดของผู้ใช้ที่กำลังคุยกับผู้ช่วย AI ผ่านแอปมือถือ " +
                "จงทำความเข้าใจสิ่งที่ผู้ใช้พูด แล้วตอบกลับตรง ๆ ตอบสั้น ๆ ไม่เกิน 3 ประโยค " +
                "ด้วยภาษาเดียวกับที่ผู้ใช้พูด ไม่ต้องทำงานอื่นใดนอกจากตอบ";
        }
        else
        {
            return Results.BadRequest(new { error = "ส่งเป็น JSON {\"text\":...} หรือ multipart เสียงเท่านั้น" });
        }

        var agy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "agy", "bin", "agy.exe");
        if (!File.Exists(agy))
            return Results.Json(new { error = $"หา agy.exe ไม่เจอที่ {agy}" }, statusCode: 500);

        var psi = new ProcessStartInfo
        {
            FileName = agy,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add("gemini-4-neon-low");
        psi.ArgumentList.Add("--dangerously-skip-permissions");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(prompt);

        using var p = Process.Start(psi);
        if (p is null)
            return Results.Json(new { error = "เริ่มกระบวนการ agy ไม่ได้" }, statusCode: 500);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(110));
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            p.Kill(entireProcessTree: true);
            return Results.Json(new { error = "agy คิดนานเกินไป ลองถามใหม่อีกครั้ง" }, statusCode: 504);
        }

        var stdout = await p.StandardOutput.ReadToEndAsync();
        var lines = stdout.Split('\n')
            .Where(l => !l.StartsWith("ERROR: logging", StringComparison.Ordinal))
            .Select(l => l.TrimEnd('\r'));
        var reply = string.Join('\n', lines).Trim();

        if (p.ExitCode != 0 && string.IsNullOrWhiteSpace(reply))
            return Results.Json(new { error = $"agy จบด้วยรหัส {p.ExitCode}" }, statusCode: 500);

        if (string.IsNullOrWhiteSpace(reply)) reply = "(agy เงียบไป ลองถามใหม่)";
        return Results.Ok(new { reply });
    }
    finally
    {
        gate.Release();
        if (savedPath is not null) { try { File.Delete(savedPath); } catch { } }
    }
});

app.Run();

record AskBody(string? Text);

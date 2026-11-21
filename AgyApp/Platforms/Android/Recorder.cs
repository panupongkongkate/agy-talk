using Android.Media;
using Microsoft.Maui.Storage;

namespace AgyApp;

public class Recorder : IRecorder
{
    MediaRecorder? mr;
    string? current;

    public Task StartAsync()
    {
        current = Path.Combine(FileSystem.CacheDirectory, $"speech_{DateTime.Now:HHmmss}.m4a");

        mr = new MediaRecorder(Android.App.Application.Context); // ctor แบบ context = API 31+ (มือถือเครื่องเป้าหมาย Android 12)
        mr.SetAudioSource(AudioSource.Mic);
        mr.SetOutputFormat(OutputFormat.Mpeg4);
        mr.SetAudioEncoder(AudioEncoder.Aac);
        mr.SetAudioEncodingBitRate(96000);
        mr.SetAudioSamplingRate(44100);
        mr.SetOutputFile(current);
        mr.Prepare();
        mr.Start();
        return Task.CompletedTask;
    }

    public Task<string> StopAsync()
    {
        var path = current!;
        try { mr?.Stop(); } catch { /* อัดสั้นเกินจะโยน — ส่งไฟล์ไปเช็คต่อเอง */ }
        mr?.Release();
        mr?.Dispose();
        mr = null;
        return Task.FromResult(path);
    }
}

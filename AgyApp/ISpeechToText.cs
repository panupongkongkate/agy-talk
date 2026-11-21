using Android.Speech;

namespace AgyApp;

public interface ISpeechToText
{
    bool IsAvailable { get; }

    /// <summary>error ล่าสุดของรอบที่แล้ว (null = ไม่มี error)</summary>
    SpeechRecognizerError? LastError { get; }

    Task<string> ListenAsync(Action<string>? onPartial);
    void Stop();
}

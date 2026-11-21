using Android.Content;
using Android.OS;
using Android.Speech;
using Microsoft.Maui.ApplicationModel;

namespace AgyApp.Platforms.Android;

/// <summary>
/// ถอดเสียงพูดบนเครื่องด้วย SpeechRecognizer ของ Android (Google recognition service)
/// แสดงผลบางส่วนสด ๆ ระหว่างพูด และหยุดอัตโนมัติเมื่อเงียบ ~1.5 วิ
/// </summary>
public class SpeechToText : ISpeechToText
{
    SpeechRecognizer? recognizer;
    TaskCompletionSource<string>? tcs;
    Action<string>? onPartial;
    string? lastPartial;

    /// <summary>error ล่าสุด (ถ้ามี) — ให้ UI แสดงสาเหตุเวลาถอดคำพัง</summary>
    public SpeechRecognizerError? LastError { get; private set; }

    public bool IsAvailable => SpeechRecognizer.IsRecognitionAvailable(global::Android.App.Application.Context);

    public Task<string> ListenAsync(Action<string>? partial)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("เครื่องนี้ไม่มีบริการรู้จำเสียง (SpeechRecognizer)");
        if (tcs is not null && !tcs.Task.IsCompleted)
            throw new InvalidOperationException("กำลังฟังอยู่แล้ว");

        var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(RecognizerIntent.ExtraLanguage, "th");
        intent.PutExtra(RecognizerIntent.ExtraLanguagePreference, "th");
        intent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
        intent.PutExtra(RecognizerIntent.ExtraSpeechInputCompleteSilenceLengthMillis, 1500);

        onPartial = partial;
        lastPartial = null;
        LastError = null;
        var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs = source;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                recognizer?.Destroy();
                recognizer = SpeechRecognizer.CreateSpeechRecognizer(global::Android.App.Application.Context);
                recognizer.SetRecognitionListener(new Listener(this));
                recognizer.StartListening(intent);
            }
            catch (Exception ex)
            {
                tcs?.TrySetException(ex);
            }
        });

        return source.Task;
    }

    /// <summary>หยุดฟัง = ส่งผลที่ได้ยินจนถึงตอนนี้ทันที (จะไปเรียก OnResults)</summary>
    public void Stop() => MainThread.BeginInvokeOnMainThread(() =>
    {
        try { recognizer?.StopListening(); }
        catch { tcs?.TrySetResult(""); }
    });

    void Finish(string? text)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { recognizer?.Destroy(); } catch { }
            recognizer = null;
        });
        tcs?.TrySetResult(text ?? "");
    }

    class Listener : Java.Lang.Object, IRecognitionListener
    {
        readonly SpeechToText p;
        public Listener(SpeechToText p) => this.p = p;

        public void OnResults(Bundle? results)
        {
            var list = results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
            var best = list?.FirstOrDefault();
            // กับดัก Infinix/บางเครื่อง: ผลสุดท้ายมาเป็น "ชุดว่าง" ทั้งที่ถอดคำระหว่างพูดได้ครบ
            // → ใช้คำจาก partial ที่เก็บไว้แทน ไม่ทิ้ง
            if (string.IsNullOrWhiteSpace(best))
                best = p.lastPartial;
            p.Finish(best);
        }

        public void OnPartialResults(Bundle? partialResults)
        {
            var list = partialResults?.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
            var first = list?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
            {
                p.lastPartial = first; // เก็บไว้เผื่อจบด้วย error จะได้ไม่เสียคำที่ถอดได้แล้ว
                p.onPartial?.Invoke(first!);
            }
        }

        public void OnError(SpeechRecognizerError error)
        {
            // มีคำที่ถอดได้แล้ว = ใช้คำนั้นส่งต่อเลย ไม่ทิ้ง
            if (!string.IsNullOrWhiteSpace(p.lastPartial))
            {
                p.Finish(p.lastPartial);
                return;
            }
            p.LastError = error;
            p.Finish("");
        }

        public void OnEndOfSpeech() { }
        public void OnBeginningOfSpeech() { }
        public void OnBufferReceived(byte[]? buffer) { }
        public void OnEvent(int eventType, Bundle? data) { }
        public void OnReadyForSpeech(Bundle? parameters) { }
        public void OnRmsChanged(float rmsdB) { }
    }
}

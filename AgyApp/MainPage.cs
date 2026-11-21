using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Media;

namespace AgyApp;

public class MainPage : ContentPage
{
    // v2.0: ยิง Gemini ผ่าน FreeAPI bridge บน mycom ตรง ๆ (บัญชี TH-AI ฟรี)
    const string Bridge = "http://100.81.96.103:8792";
    const string ChatUrl = Bridge + "/v1/chat/completions";
    const string HealthUrl = Bridge + "/health";
    const string Model = "gemini-3.8-flash";
    const string SystemPrompt =
        "คุณคือผู้ช่วยเสียงส่วนตัวชื่อ agy-talk " +
        "ผู้ใช้คุยกับคุณด้วยเสียงและข้อความถูกถอดจากเสียงอัตโนมัติ อาจมีคำสะกดเพี้ยนเล็กน้อย จงเข้าใจความหมายแทน " +
        "ตอบกลับสั้น กระชับ ไม่เกิน 3 ประโยค ด้วยภาษาเดียวกับที่ผู้ใช้พูด ไม่ต้องใช้ markdown";

    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(90) };
    readonly HttpClient pingHttp = new() { Timeout = TimeSpan.FromSeconds(5) };
    readonly Button mic;
    readonly Button reset;
    readonly Label status;
    readonly Label reply;
    readonly ActivityIndicator busy;
    readonly Label serverStatus;

    // บทสนทนาต่อเนื่อง (เก็บในแอป ส่งประวัติไปด้วยทุกครั้ง)
    readonly List<(string Role, string Content)> history = new();
    readonly List<string> transcript = new(); // สำหรับแสดงบนจอ
    DateTime lastTurn = DateTime.MinValue;
    bool listening;
    bool busyPosting;

    ISpeechToText Stt => Handler!.MauiContext!.Services.GetRequiredService<ISpeechToText>();

    public MainPage()
    {
        Title = "agy-talk";
        BackgroundColor = Color.FromArgb("#101828");

        var version = AppInfo.Current.VersionString;

        reset = new Button
        {
            Text = "🔄", FontSize = 16, WidthRequest = 44, HeightRequest = 36, CornerRadius = 10,
            BackgroundColor = Color.FromArgb("#1D2939"), TextColor = Color.FromArgb("#98A2B3"),
            BorderWidth = 1, BorderColor = Color.FromArgb("#344054"),
            VerticalOptions = LayoutOptions.Center,
        };
        reset.Clicked += (_, _) =>
        {
            history.Clear();
            transcript.Clear();
            reply.Text = "…";
            status.Text = "เริ่มคุยใหม่แล้ว — กดปุ่มพูดได้เลย";
        };

        var header = new HorizontalStackLayout
        {
            Spacing = 10, HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "agy-talk", FontSize = 22, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#E4E7EC"), VerticalOptions = LayoutOptions.Center,
                },
                new Border
                {
                    Stroke = Color.FromArgb("#1F4D33"),
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                    BackgroundColor = Color.FromArgb("#16261B"),
                    Padding = new Thickness(10, 2),
                    VerticalOptions = LayoutOptions.Center,
                    Content = new Label
                    {
                        Text = "v" + version, FontSize = 12, FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#12B76A"),
                    },
                },
                reset,
            },
        };

        mic = new Button
        {
            Text = "🎙️", FontSize = 44, WidthRequest = 116, HeightRequest = 116, CornerRadius = 58,
            BackgroundColor = Color.FromArgb("#175CD3"), TextColor = Colors.White,
            BorderWidth = 3, BorderColor = Color.FromArgb("#1849A9"),
            HorizontalOptions = LayoutOptions.Center,
        };
        mic.Clicked += OnMic;

        status = new Label
        {
            Text = "กดปุ่ม พูดให้จบ แล้วเงียบ 1-2 วิ = ส่งอัตโนมัติ", FontSize = 15,
            TextColor = Color.FromArgb("#98A2B3"), HorizontalOptions = LayoutOptions.Center,
        };

        busy = new ActivityIndicator { IsRunning = false, Color = Color.FromArgb("#12B76A") };

        reply = new Label
        {
            Text = "…", FontSize = 18, TextColor = Color.FromArgb("#E4E7EC"),
            LineBreakMode = LineBreakMode.WordWrap,
        };

        serverStatus = new Label
        {
            Text = "● กำลังเช็ค bridge (mycom)…", FontSize = 12,
            TextColor = Color.FromArgb("#98A2B3"), HorizontalOptions = LayoutOptions.Center,
        };
        var footer = new VerticalStackLayout { Padding = new Thickness(0, 0, 0, 14), Children = { serverStatus } };

        var top = new VerticalStackLayout
        {
            Padding = new Thickness(0, 28, 0, 12), Spacing = 18,
            Children = { header, mic, status, busy },
        };
        var scroll = new ScrollView { Padding = 20, Content = reply };
        Grid.SetRow(scroll, 1);
        Grid.SetRow(footer, 2);

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            Children = { top, scroll, footer },
        };

        Appearing += async (_, _) => await CheckServer();
    }

    async Task CheckServer()
    {
        try
        {
            using var r = await pingHttp.GetAsync(HealthUrl);
            if (r.IsSuccessStatusCode)
            {
                serverStatus.Text = "● bridge mycom ✓";
                serverStatus.TextColor = Color.FromArgb("#12B76A");
            }
            else
            {
                serverStatus.Text = $"● bridge ตอบรหัส {(int)r.StatusCode}";
                serverStatus.TextColor = Color.FromArgb("#F97066");
            }
        }
        catch
        {
            serverStatus.Text = "● หา bridge ไม่เจอ (mycom ปิดอยู่?)";
            serverStatus.TextColor = Color.FromArgb("#F97066");
        }
    }

    async void OnMic(object? sender, EventArgs e)
    {
        if (busyPosting) return;
        try
        {
            if (!listening)
            {
                var perm = await Permissions.RequestAsync<Permissions.Microphone>();
                if (perm != PermissionStatus.Granted)
                {
                    status.Text = "❌ ไม่ได้รับสิทธิ์ไมค์ (ลองตั้งค่าแอป → สิทธิ์ → ไมโครโฟน)";
                    return;
                }
                if (!Stt.IsAvailable)
                {
                    status.Text = "❌ เครื่องนี้ไม่มีบริการรู้จำเสียง";
                    return;
                }

                listening = true;
                mic.Text = "⏹️";
                mic.BackgroundColor = Color.FromArgb("#D92D20");
                mic.BorderColor = Color.FromArgb("#B42318");
                status.Text = "🎧 ฟังอยู่… พูดได้เลย";

                _ = ListenCycle();
            }
            else
            {
                status.Text = "หยุดฟัง ส่งประโยคที่ได้ยิน…";
                Stt.Stop();
            }
        }
        catch (Exception ex)
        {
            Reset($"❌ {ex.Message}");
        }
    }

    async Task ListenCycle()
    {
        string text;
        try
        {
            text = await Stt.ListenAsync(p =>
            {
                if (!string.IsNullOrWhiteSpace(p)) status.Text = "🎧 " + p;
            });
        }
        catch (Exception ex)
        {
            Reset($"❌ {ex.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            var err = Stt.LastError;
            Reset(err is null
                ? "🤫 ไม่ได้ยินเสียง — กดแล้วพูดใหม่อีกครั้ง"
                : $"❌ ตัวรู้จำเสียงติดขัด ({err}) — กดลองใหม่ ถ้าซ้ำบ่อยบอกหน่อย");
            return;
        }

        // เงียบนานเกิน 10 นาที = เริ่มบทสนทนาใหม่
        if ((DateTime.Now - lastTurn).TotalMinutes > 10)
        {
            history.Clear();
            transcript.Clear();
        }
        lastTurn = DateTime.Now;
        history.Add(("user", text));
        transcript.Add("🗣️ " + text);

        listening = false;
        busyPosting = true;
        mic.IsEnabled = false;
        mic.Text = "⏳";
        busy.IsRunning = true;
        mic.BackgroundColor = Color.FromArgb("#667085");
        mic.BorderColor = Color.FromArgb("#475467");
        RenderTranscript("…");
        status.Text = "คิดอยู่…";

        try
        {
            var sw = Stopwatch.StartNew();

            var messages = new List<object> { new { role = "system", content = SystemPrompt } };
            foreach (var (role, content) in history.TakeLast(12))
                messages.Add(new { role, content });
            var payload = JsonSerializer.Serialize(new { model = Model, messages });

            using var content_ = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await http.PostAsync(ChatUrl, content_);
            var json = await resp.Content.ReadAsStringAsync();
            sw.Stop();

            string answer;
            try
            {
                using var doc = JsonDocument.Parse(json);
                answer = doc.RootElement.TryGetProperty("choices", out var ch)
                    && ch.GetArrayLength() > 0
                    && ch[0].TryGetProperty("message", out var m)
                    && m.TryGetProperty("content", out var c)
                        ? (c.GetString() ?? "")
                        : doc.RootElement.TryGetProperty("error", out var er)
                              && er.TryGetProperty("message", out var em)
                              ? ("⚠️ " + em.GetString())
                              : json;
            }
            catch { answer = json; }

            if (answer.StartsWith("⚠️"))
            {
                status.Text = answer;
                history.RemoveAt(history.Count - 1); // คำถามที่พังไม่เก็บเข้าประวัติ
                return;
            }

            history.Add(("assistant", answer));
            transcript.Add("🤖 " + answer);
            RenderTranscript();
            status.Text = $"ตอบแล้ว 🎉 ใช้เวลา {sw.Elapsed.TotalSeconds:0.0} วิ — ถามต่อได้เลย (จำบทสนทนาได้)";

            var locales = await TextToSpeech.GetLocalesAsync();
            var th = locales.FirstOrDefault(l => (l.Language ?? "").StartsWith("th", StringComparison.OrdinalIgnoreCase));
            await TextToSpeech.SpeakAsync(answer, new SpeechOptions { Locale = th });
        }
        catch (Exception ex)
        {
            status.Text = "❌ " + ex.Message;
        }
        finally
        {
            Reset("กดปุ่ม พูดให้จบ แล้วเงียบ 1-2 วิ = ส่งอัตโนมัติ");
        }
    }

    void RenderTranscript(string pending = "")
    {
        var shown = transcript.Count > 20 ? transcript.Skip(transcript.Count - 20).ToList() : transcript.ToList();
        reply.Text = string.Join("\n\n", shown) + (pending == "" ? "" : "\n\n🤖 " + pending);
    }

    void Reset(string msg)
    {
        listening = false;
        busyPosting = false;
        mic.Text = "🎙️";
        mic.IsEnabled = true;
        busy.IsRunning = false;
        mic.BackgroundColor = Color.FromArgb("#175CD3");
        mic.BorderColor = Color.FromArgb("#1849A9");
        status.Text = msg;
    }
}

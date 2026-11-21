using Microsoft.Maui.Hosting;

namespace AgyApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddSingleton<ISpeechToText, Platforms.Android.SpeechToText>();
        return builder.Build();
    }
}

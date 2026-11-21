namespace AgyApp;

public interface IRecorder
{
    Task StartAsync();
    Task<string> StopAsync();
}

namespace Phoenix.Core;

public interface ILogger
{
    void Info(string message);
    void Warn(string message);
}

public sealed class ConsoleLogger : ILogger
{
    public void Info(string m) => Console.WriteLine($"  [i] {m}");
    public void Warn(string m) => Console.WriteLine($"  [!] {m}");
}

using System.Diagnostics;

namespace Yrki.IoT.WurthMetisII.Features.Serial;

internal sealed class SerialPortService : ISerialPortService
{
    public void ConfigurePort(string portName, int baudRate)
    {
        var arguments = $"-f {portName} {baudRate} cs8 -cstopb -parenb raw -ixon -ixoff min 0 time 1";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "stty",
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        process.Start();
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            return;
        }

        var error = process.StandardError.ReadToEnd().Trim();
        throw new IOException($"Failed to configure serial port {portName}: {error}");
    }

    public FileStream OpenStream(string portName)
    {
        return new FileStream(
            portName,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite,
            bufferSize: 4096);
    }
}

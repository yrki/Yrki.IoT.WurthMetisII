namespace Yrki.IoT.WurthMetisII.Features.Serial;

internal interface ISerialPortService
{
    void ConfigurePort(string portName, int baudRate);
    FileStream OpenStream(string portName);
}

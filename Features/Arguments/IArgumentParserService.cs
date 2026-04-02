namespace Yrki.IoT.WurthMetisII.Features.Arguments;

internal interface IArgumentParserService
{
    RuntimeOptions Parse(string[] args);
    void PrintUsage();
}

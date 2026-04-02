using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Yrki.IoT.WurthMetisII.Features.Activation;
using Yrki.IoT.WurthMetisII.Features.Application;
using Yrki.IoT.WurthMetisII.Features.Gateway;
using Yrki.IoT.WurthMetisII.Features.Arguments;
using Yrki.IoT.WurthMetisII.Features.Logging;
using Yrki.IoT.WurthMetisII.Features.MetisProtocol;
using Yrki.IoT.WurthMetisII.Features.Parameters;
using Yrki.IoT.WurthMetisII.Features.Serial;
using Yrki.IoT.WurthMetisII.Features.ServerTransport;
using Yrki.IoT.WurthMetisII.Features.Telegrams;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ";
});

builder.Services.AddSingleton<IArgumentParserService, ArgumentParserService>();
builder.Services.AddSingleton<IApplicationService, ApplicationService>();
builder.Services.AddSingleton<IPayloadLogService, PayloadLogService>();
builder.Services.AddSingleton<ISerialPortService, SerialPortService>();
builder.Services.AddSingleton<IMetisProtocolService, MetisProtocolService>();
builder.Services.AddSingleton<IActivationService, ActivationService>();
builder.Services.AddSingleton<IParameterDumpService, ParameterDumpService>();
builder.Services.AddSingleton<IGatewayIdService, GatewayIdService>();
builder.Services.AddSingleton<IWMBusTelegramParserService, WMBusTelegramParserService>();
builder.Services.AddSingleton<ITelegramListenerService, TelegramListenerService>();
builder.Services.AddSingleton<ISendToServer, SendToServerWithMqttService>();

using var host = builder.Build();
using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var app = host.Services.GetRequiredService<IApplicationService>();
var exitCode = await app.RunAsync(args, cancellation.Token);
Environment.ExitCode = exitCode;

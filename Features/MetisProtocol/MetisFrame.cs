namespace Yrki.IoT.WurthMetisII.Features.MetisProtocol;

internal readonly record struct MetisFrame(byte Command, byte[] Payload, byte[] RawFrame);

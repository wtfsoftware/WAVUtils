using NAudio.Wave;

var path = @"C:\TEMP\ZOOM0008\ZOOM0008_Tr1.WAV";
using var reader = new WaveFileReader(path);

Console.WriteLine($"Channels: {reader.WaveFormat.Channels}");
Console.WriteLine($"SampleRate: {reader.WaveFormat.SampleRate}");
Console.WriteLine($"BitsPerSample: {reader.WaveFormat.BitsPerSample}");
Console.WriteLine($"AvgBytesPerSec: {reader.WaveFormat.AverageBytesPerSecond}");
Console.WriteLine($"BlockAlign: {reader.WaveFormat.BlockAlign}");
Console.WriteLine($"IsReadFully: {reader.IsReadFully}");

if (reader.ChunkCollection != null)
{
    Console.WriteLine($"\nChunks ({reader.ChunkCollection.Count}):");
    foreach (var chunk in reader.ChunkCollection)
    {
        Console.WriteLine($"  {chunk.ChunkId} ({chunk.Length} bytes, type: {chunk.GetType().Name})");
        if (chunk is NAudio.Wave.WaveFormatExtensibleChunk ext)
        {
            Console.WriteLine($"    Extensions: {ext.BitsPerSample} bits, mask={ext.ChannelMask:X4}");
        }
    }
}

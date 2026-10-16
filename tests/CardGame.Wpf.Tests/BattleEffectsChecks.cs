using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class BattleEffectsChecks
{

    internal static void SetGifTiming(string path, ushort delay = 8)
    {
        var data = File.ReadAllBytes(path);
        var headerEnd = 13 + ((data[10] & 128) == 0 ? 0 : 3 * (1 << ((data[10] & 7) + 1)));
        var cursor = headerEnd;
        while (cursor < data.Length && data[cursor] != 0x3b)
        {
            var kind = data[cursor++];
            if (kind == 0x21)
            {
                var label = data[cursor++];
                if (label == 0xf9)
                {
                    Assert(data[cursor] == 4, "Invalid GIF graphic control block.");
                    data[cursor + 1] = (byte)((data[cursor + 1] & ~28) | 8); // Restore background between full frames.
                    data[cursor + 2] = (byte)(delay & 255); data[cursor + 3] = (byte)(delay >> 8); // Centiseconds.
                }
            }
            else if (kind == 0x2c)
            {
                var packed = data[cursor + 8];
                cursor += 9;
                if ((packed & 128) != 0) cursor += 3 * (1 << ((packed & 7) + 1));
                cursor++; // LZW minimum code size.
            }
            else throw new InvalidOperationException($"Unexpected GIF block {kind:X2}.");
            while (data[cursor] != 0) cursor += 1 + data[cursor];
            cursor++;
        }
        using var file = File.Create(path);
        file.Write(data, 0, headerEnd);
        // Infinite loop application extension at the first block boundary.
        file.Write(new byte[] { 0x21, 0xff, 11 });
        file.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        file.Write(new byte[] { 3, 1, 0, 0, 0 });
        file.Write(data, headerEnd, data.Length - headerEnd);
    }
}

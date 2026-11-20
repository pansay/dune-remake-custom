namespace Cryogenic.Player;

using SkiaSharp;
using Spice86.Core.Emulator.VM;

/// <summary>Opt-in captures of a paused game for save/load colour regression checks.</summary>
internal static class PlayerDiagnostics {
    public static void Capture(Machine machine, uint dataBase, int slot) {
        if (Environment.GetEnvironmentVariable("CRYOGENIC_CAPTURE_FRAMES") != "1") return;
        string folder = Path.Combine(PlayerPaths.Home, "diagnostics");
        Directory.CreateDirectory(folder);
        string stem = Path.Combine(folder, $"slot-{slot:D3}");
        byte[] pixels = machine.Memory.ReadRam(64000, 0xA0000);
        byte[] palette = new byte[768];
        for (int index = 0; index < 256; index++) {
            for (int channel = 0; channel < 3; channel++) palette[index * 3 + channel] = machine.VgaRegisters.DacRegisters.Palette[index, channel];
        }
        using SKBitmap bitmap = new(320, 200);
        for (int y = 0; y < 200; y++) {
            for (int x = 0; x < 320; x++) {
                int index = pixels[y * 320 + x] * 3;
                bitmap.SetPixel(x, y, new SKColor((byte)(palette[index] * 255 / 63), (byte)(palette[index + 1] * 255 / 63), (byte)(palette[index + 2] * 255 / 63)));
            }
        }
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        using (FileStream output = File.Create(stem + ".png")) png.SaveTo(output);
        AtomicJson.Write(stem + ".json", new {
            GameTime = machine.Memory.UInt16[dataBase + 2],
            RoomX = machine.Memory.UInt16[dataBase + 4],
            RoomY = machine.Memory.UInt16[dataBase + 6],
            Palette = palette,
            PaletteTransition = machine.Memory[dataBase + 0xCE80],
            WorldView = machine.Memory[dataBase + 0xFB],
            FullVisions = (machine.Memory[dataBase + FullVisionMapReveal.PowersOffset] & FullVisionMapReveal.WaterOfLifePower) != 0,
            KnownLocations = Enumerable.Range(0, FullVisionMapReveal.LocationCount).Count(i => (machine.Memory[dataBase + (uint)(FullVisionMapReveal.LocationsOffset + i * FullVisionMapReveal.LocationSize + FullVisionMapReveal.LocationFlagsOffset)] & FullVisionMapReveal.Hidden) == 0)
        });
    }
}

// FILTER CMR — colour palette (original; see README).

using UnityEngine;

namespace FilterCMR.Core
{
    public static class Palette
    {
        public static readonly Color ElectricBlue = Hex(0x16, 0x4B, 0xFF);        // #164BFF
        public static readonly Color ElectricBlueBright = Hex(0x4D, 0x78, 0xFF);  // lifted for legibility on video
        public static readonly Color Cyan = Hex(0x00, 0xD9, 0xFF);                // #00D9FF
        public static readonly Color LightBlue = Hex(0x91, 0xDF, 0xFF);           // #91DFFF
        public static readonly Color Navy = Hex(0x07, 0x15, 0x2F);                // #07152F
        public static readonly Color Black = Hex(0x05, 0x07, 0x0D);               // #05070D
        public static readonly Color Warning = Hex(0xFF, 0xC2, 0x3D);
        public static readonly Color Error = Hex(0xFF, 0x4F, 0x7A);

        // Rich-text colour tags for UI.Text
        public const string OkTag = "#00D9FF";
        public const string DimTag = "#5E7BA8";
        public const string WarnTag = "#FFC23D";
        public const string ErrorTag = "#FF4F7A";
        public const string TextTag = "#91DFFF";

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        static Color Hex(byte r, byte g, byte b) => new Color32(r, g, b, 255);
    }
}

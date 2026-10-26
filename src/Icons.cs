using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace IslandPanel;
public static class Icons
{
    static readonly Dictionary<string, Geometry> Shapes = new();
    static readonly Dictionary<string, string> Data = new()
    {
        ["home"]="M3,10 L12,3 21,10 M5,9 L5,21 10,21 10,14 14,14 14,21 19,21 19,9",
        ["clipboard"]="M9,5 L5,5 5,22 19,22 19,5 15,5 M9,3 L15,3 15,7 9,7 Z M8,11 L16,11 M8,15 L16,15 M8,19 L13,19",
        ["files"]="M3,8 L3,20 21,20 21,8 12,8 10,5 3,5 Z",
        ["text"]="M4,5 L20,5 M12,5 L12,21 M8,21 L16,21",
        ["note"]="M12,4 L4,4 4,21 20,21 20,13 M11,14 L12,10 20,2 23,5 15,13 Z",
        ["music"]="M9,18 L9,5 21,3 21,16 M9,9 L21,7 M9,18 C9,22 2,23 2,19 2,16 9,15 9,18 M21,16 C21,20 14,21 14,17 14,14 21,13 21,16",
        ["weather"]="M12,2 L12,4 M12,20 L12,22 M2,12 L4,12 M20,12 L22,12 M5,5 L6.5,6.5 M17.5,17.5 L19,19 M5,19 L6.5,17.5 M17.5,6.5 L19,5 M17,12 A5,5 0 1 1 7,12 A5,5 0 1 1 17,12",
        ["settings"]="M4,6 L20,6 M4,12 L20,12 M4,18 L20,18 M8,3 L8,9 M16,9 L16,15 M10,15 L10,21",
        ["mixer"]="M5,3 L5,21 M12,3 L12,21 M19,3 L19,21 M2,8 L8,8 M9,15 L15,15 M16,10 L22,10",
        ["area"]="M3,9 L3,3 9,3 M15,3 L21,3 21,9 M21,15 L21,21 15,21 M9,21 L3,21 3,15 M8,8 L17,12 13,14 12,18 Z",
        ["screen"]="M2,4 L22,4 22,18 2,18 Z M12,18 L12,22 M7,22 L17,22",
        ["ocr"]="M3,8 L3,3 8,3 M16,3 L21,3 21,8 M21,16 L21,21 16,21 M8,21 L3,21 3,16 M8,8 L16,8 M12,8 L12,16 M9,16 L15,16",
        ["qr"]="M3,3 L9,3 9,9 3,9 Z M15,3 L21,3 21,9 15,9 Z M3,15 L9,15 9,21 3,21 Z M15,15 L18,15 18,18 21,18 21,21 15,21 Z M12,3 L12,6 M12,12 L15,12 M21,12 L21,15 M12,18 L12,21",
        ["picker"]="M14,3 L21,10 M16,4 L19,1 23,5 20,8 M17,6 L5,18 3,22 7,20 19,8 M7,16 L11,20",
        ["clean"]="M3,14 L14,3 22,11 12,21 9,21 Z M8,9 L17,18 M12,21 L22,21",
        ["record"]="M3,6 L16,6 16,19 3,19 Z M16,10 L22,6 22,19 16,15",
        ["search"]="M16,10 A6,6 0 1 1 4,10 A6,6 0 1 1 16,10 M15,15 L22,22",
        ["pin"]="M8,3 L17,3 M10,3 L10,10 6,14 19,14 15,10 15,3 M12.5,14 L12.5,22",
        ["volume"]="M3,9 L7,9 12,5 12,19 7,15 3,15 Z M16,8 C20,10 20,14 16,16 M19,4 C27,9 27,15 19,20",
        ["clock"]="M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M12,6 L12,12 16,14",
        ["minus"]="M5,12 L19,12"
        ,["play"]="M7,4 L21,12 7,20 Z"
        ,["pause"]="M6,4 L10,4 10,20 6,20 Z M15,4 L19,4 19,20 15,20 Z"
        ,["previous"]="M4,4 L4,20 M20,4 L7,12 20,20 Z"
        ,["next"]="M20,4 L20,20 M4,4 L17,12 4,20 Z"
        ,["spectrum"]="M3,10 L3,15 M7,6 L7,19 M12,2 L12,22 M17,6 L17,19 M21,10 L21,15"
    };
    public static Path Create(string name, double size = 17, Brush? stroke = null)
    {
        if (!Shapes.TryGetValue(name, out var g)) { g = Geometry.Parse(Data.TryGetValue(name, out var d) ? d : Data["home"]); g.Freeze(); Shapes[name] = g; }
        return new Path { Data = g, Width = size, Height = size, Stretch = Stretch.Uniform, Stroke = stroke ?? Brushes.LightGray, Fill = name == "play" || name == "pause" ? stroke ?? Brushes.LightGray : null, StrokeThickness = 1.45, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    }
}

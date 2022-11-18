﻿using System.Xml.Serialization;

namespace Cedserver.Config; 

public class Rect {
    [XmlAttribute("x1")]
    public int X1 { get; set; }
    [XmlAttribute("x2")]
    public int X2 { get; set; }
    [XmlAttribute("y1")]
    public int Y1 { get; set; }
    [XmlAttribute("y2")]
    public int Y2 { get; set; }

    public override string ToString() {
        return $"{nameof(X1)}: {X1}, {nameof(X2)}: {X2}, {nameof(Y1)}: {Y1}, {nameof(Y2)}: {Y2}";
    }
}
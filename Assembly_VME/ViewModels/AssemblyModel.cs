using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Assembly_VME.ViewModels
{
    public class AssemblyItem
    {
        public string Name { get; set; }
        public AssemblyInstance Instance { get; set; }
        public List<ElementId> WallIds { get; set; } = new List<ElementId>();
        public List<ElementId> RebarIds { get; set; } = new List<ElementId>();
        public List<ElementId> GenericModelIds { get; set; } = new List<ElementId>();

        public int WallCount => WallIds.Count;
        public int RebarCount => RebarIds.Count;
        public int GenericModelCount => GenericModelIds.Count;

        public string Description => $"Walls: {WallCount} | Rebars: {RebarCount} | Embeds: {GenericModelCount}";

        // High-Fidelity Schedule Data for Selection Binding
        public List<BbsItem> RebarSchedules { get; set; } = new List<BbsItem>();
        public List<MaterialSummaryItem> MaterialSummaries { get; set; } = new List<MaterialSummaryItem>();
        public List<WallItem> WallSchedules { get; set; } = new List<WallItem>();
        public List<HookItem> HookSchedules { get; set; } = new List<HookItem>();
    }

    public class BbsItem
    {
        public string Mark { get; set; } = "-";
        public string Type { get; set; } = "-";
        public string Size { get; set; } = "-";       // e.g. "8 mm"
        public string Shape { get; set; } = "-";      // e.g. "M_17"
        public string Quantity { get; set; } = "-";   // e.g. "6"
        public string BarLength { get; set; } = "-";  // e.g. "782 mm" (Single bar)
        public string TotalLength { get; set; } = "-"; // e.g. "4693 mm" (Total length)
        public string Weight { get; set; } = "-";     // e.g. "1.85 kg"
        
        // Shape Dimensions (A, B, C, D, E)
        public string DimA { get; set; } = "-";
        public string DimB { get; set; } = "-";
        public string DimC { get; set; } = "-";
        public string DimD { get; set; } = "-";
        public string DimE { get; set; } = "-";
    }

    public class MaterialSummaryItem
    {
        public string BarDiameter { get; set; }  // e.g. "8 mm"
        public string TotalLength { get; set; }  // e.g. "150453 mm"
        public string Weight { get; set; }       // e.g. "59.44 kg"
    }

    public class WallItem
    {
        public string Mark { get; set; } = "-";
        public string Length { get; set; } = "-";
        public string Height { get; set; } = "-";
        public string Area { get; set; } = "-";
        public string Volume { get; set; } = "-";
        public string Weight { get; set; } = "-";
    }

    public class HookItem
    {
        public string Mark { get; set; } = "-";
        public string Type { get; set; } = "-";
        public string Count { get; set; } = "-";
    }
}

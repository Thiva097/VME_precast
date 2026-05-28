using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Structure;

namespace VME_Apollo
{
    public class ModelGenerationHandler : IExternalEventHandler
    {
        public double Length { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double WallThickness { get; set; }
        public double BaseSlabThickness { get; set; }
        public double CoverSlabThickness { get; set; }
        public string ElementType { get; set; }

        public ElementId CreatedElementId { get; private set; } = ElementId.InvalidElementId;
        public bool IsUpdate { get; set; }
        public Action<string, bool> OnProcessCompleted { get; set; }

        // Reinforcement Properties
        public double WallHBarDia { get; set; }
        public double WallHSpacing { get; set; }
        public double WallVBarDia { get; set; }
        public double WallVSpacing { get; set; }
        public double SlabMainBarDia { get; set; }
        public double SlabMainSpacing { get; set; }
        public double SlabDistBarDia { get; set; }
        public double SlabDistSpacing { get; set; }
        public double ClearCover { get; set; }

        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;

            using (Transaction trans = new Transaction(doc, "Full Tank Sync"))
            {
                trans.Start();

                try
                {
                    double lengthFt = UnitUtils.ConvertToInternalUnits(Length, UnitTypeId.Millimeters);
                    double widthFt = UnitUtils.ConvertToInternalUnits(Width, UnitTypeId.Millimeters);
                    double heightFt = UnitUtils.ConvertToInternalUnits(Height, UnitTypeId.Millimeters);
                    double wallFt = UnitUtils.ConvertToInternalUnits(WallThickness, UnitTypeId.Millimeters);
                    double ccFt = UnitUtils.ConvertToInternalUnits(ClearCover, UnitTypeId.Millimeters);

                    // 1. Update Global Parameters
                    TryUpdateGlobalParameter(doc, new[] { "Length", "TANK_LENGTH" }, lengthFt);
                    TryUpdateGlobalParameter(doc, new[] { "Width", "TANK_WIDTH" }, widthFt);
                    TryUpdateGlobalParameter(doc, new[] { "Height", "TANK_HEIGHT" }, heightFt);
                    TryUpdateGlobalParameter(doc, new[] { "WallThickness", "Wall Thickness" }, wallFt);
                    
                    // CRITICAL: Force Revit to update all geometry and location curves
                    doc.Regenerate(); 

                    // 2. Adjust Rebar using the updated wall geometry
                    UpdateRebarToMatchModel(doc, ccFt);

                    trans.Commit();
                    OnProcessCompleted?.Invoke("Tank and Rebar fully synchronized", true);
                }
                catch (Exception ex)
                {
                    trans.RollBack();
                    OnProcessCompleted?.Invoke("Sync Error: " + ex.Message, false);
                }
            }
        }

        private void UpdateRebarToMatchModel(Document doc, double ccFt)
        {
            var allRebars = new FilteredElementCollector(doc)
                .OfClass(typeof(Rebar))
                .Cast<Rebar>()
                .Where(r => r.GroupId == ElementId.InvalidElementId)
                .ToList();

            foreach (Rebar rebar in allRebars)
            {
                string comment = rebar.LookupParameter("Comments")?.AsString() ?? "";
                if (string.IsNullOrEmpty(comment)) continue;

                bool isVertical = comment.ToUpper().Contains("V");
                bool isHorizontal = comment.ToUpper().Contains("H");
                bool isExtra = comment.ToUpper().Contains("X");

                if (!isVertical && !isHorizontal && !isExtra) continue;

                // Sync Type and Spacing first
                if (!isExtra) UpdateRebarTypeAndSpacing(rebar, isVertical);

                // Recalculate Geometry to fit the CURRENT wall
                FitRebarInCurrentWall(rebar, ccFt, isVertical);
            }
        }

        private void UpdateRebarTypeAndSpacing(Rebar rebar, bool isVertical)
        {
            double dia = isVertical ? WallVBarDia : WallHBarDia;
            double sp = isVertical ? WallVSpacing : WallHSpacing;

            RebarBarType type = new FilteredElementCollector(rebar.Document)
                .OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>()
                .FirstOrDefault(t => t.Name.Contains(dia.ToString()));
            if (type != null) rebar.ChangeTypeId(type.Id);

            Parameter spParam = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_BAR_SPACING);
            if (spParam != null && !spParam.IsReadOnly)
                spParam.Set(UnitUtils.ConvertToInternalUnits(sp, UnitTypeId.Millimeters));
        }

        private void FitRebarInCurrentWall(Rebar rebar, double ccFt, bool isVertical)
        {
            try 
            {
                Wall wall = rebar.Document.GetElement(rebar.GetHostId()) as Wall;
                if (wall == null) return;

                LocationCurve loc = wall.Location as LocationCurve;
                if (loc == null) return;

                Curve curve = loc.Curve;
                XYZ start = curve.GetEndPoint(0);
                XYZ end = curve.GetEndPoint(1);
                XYZ wallDir = (end - start).Normalize();
                
                // Get ACTUAL wall height from its parameters after regeneration
                double wallHeight = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).AsDouble();
                double wallBaseOffset = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET).AsDouble();
                
                double wallHeightInside = wallHeight - (2 * ccFt);
                double wallLengthInside = curve.Length - (2 * ccFt);

                XYZ origin, xVec, yVec;

                if (isVertical)
                {
                    // Vertical bars: Shape 00 runs along Z
                    origin = start + wallDir * ccFt + XYZ.BasisZ * (wallBaseOffset + ccFt);
                    xVec = XYZ.BasisZ * wallHeightInside; // Bar length is vertical
                    yVec = wallDir * wallLengthInside;    // Distribution is horizontal
                }
                else
                {
                    // Horizontal bars: Shape 00 runs along wall direction
                    origin = start + wallDir * ccFt + XYZ.BasisZ * (wallBaseOffset + ccFt);
                    xVec = wallDir * wallLengthInside;    // Bar length is horizontal
                    yVec = XYZ.BasisZ * wallHeightInside; // Distribution is vertical
                }

                rebar.GetShapeDrivenAccessor().ScaleToBox(origin, xVec, yVec);
            }
            catch { }
        }

        private bool TryUpdateGlobalParameter(Document doc, string[] names, double val)
        {
            foreach (string n in names)
            {
                ElementId id = GlobalParametersManager.FindByName(doc, n);
                if (id != ElementId.InvalidElementId)
                {
                    (doc.GetElement(id) as GlobalParameter)?.SetValue(new DoubleParameterValue(val));
                    return true;
                }
            }
            return false;
        }

        public string GetName() => "Model Generation Handler";
    }
}

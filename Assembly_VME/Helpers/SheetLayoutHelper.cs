using System;
using Autodesk.Revit.DB;

namespace Assembly_VME.Helpers
{
    /// <summary>
    /// Computes drawable sheet area (left of title block) and schedule placement points.
    /// </summary>
    public static class SheetLayoutHelper
    {
        public const double MarginFeet = 0.03;

        public struct SheetLayout
        {
            public double MinX;
            public double MinY;
            public double MaxX;
            public double MaxY;
            public double DrawableMinX;
            public double DrawableMaxX;
            public double DrawableMinY;
            public double DrawableMaxY;

            public double SheetWidth => MaxX - MinX;
            public double SheetHeight => MaxY - MinY;
            public double DrawableWidth => DrawableMaxX - DrawableMinX;
            public double DrawableHeight => DrawableMaxY - DrawableMinY;
        }

        public static SheetLayout GetLayout(Document doc, ViewSheet sheet)
        {
            double minX = 0;
            double minY = 0;
            double maxX = 1.378;
            double maxY = 0.974;
            double titleBlockLeftX = maxX;

            Element titleBlockInstance = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .FirstElement();

            if (titleBlockInstance != null)
            {
                BoundingBoxXYZ bbox = titleBlockInstance.get_BoundingBox(sheet);
                if (bbox != null)
                {
                    titleBlockLeftX = bbox.Min.X;
                    maxX = bbox.Max.X;
                    maxY = bbox.Max.Y;

                    double sheetWidth = GetLengthParameterFeet(titleBlockInstance, BuiltInParameter.SHEET_WIDTH);
                    double sheetHeight = GetLengthParameterFeet(titleBlockInstance, BuiltInParameter.SHEET_HEIGHT);

                    if (sheetWidth > 0 && sheetHeight > 0)
                    {
                        minX = maxX - sheetWidth;
                        minY = maxY - sheetHeight;
                    }
                    else
                    {
                        minX = bbox.Min.X;
                        minY = bbox.Min.Y;
                    }
                }
            }

            var layout = new SheetLayout
            {
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY,
                DrawableMinX = minX + MarginFeet,
                DrawableMaxX = titleBlockLeftX - MarginFeet,
                DrawableMinY = minY + MarginFeet,
                DrawableMaxY = maxY - MarginFeet
            };

            if (layout.DrawableMaxX <= layout.DrawableMinX + 0.1)
            {
                layout.DrawableMaxX = minX + 0.72 * (maxX - minX);
                layout.DrawableMinX = minX + MarginFeet;
            }

            return layout;
        }

        public static XYZ GetSummaryPlacementPoint(SheetLayout layout)
        {
            return new XYZ(
                layout.DrawableMinX,
                layout.DrawableMaxY - 0.02,
                0);
        }

        public static XYZ GetBbsPlacementPoint(SheetLayout layout)
        {
            return new XYZ(
                layout.DrawableMinX,
                layout.MinY + 0.52 * layout.SheetHeight,
                0);
        }

        public static XYZ GetFooterPlacementPoint(SheetLayout layout, XYZ summaryPoint, double summaryHeightFeet)
        {
            return new XYZ(summaryPoint.X, summaryPoint.Y - summaryHeightFeet, 0);
        }

        public static XYZ Get3DViewCenter(SheetLayout layout)
        {
            double centerX = layout.DrawableMinX + 0.71 * layout.DrawableWidth;
            double centerY = layout.DrawableMinY + 0.77 * layout.DrawableHeight;
            return new XYZ(centerX, centerY, 0);
        }

        private static double GetLengthParameterFeet(Element element, BuiltInParameter builtInParam)
        {
            try
            {
                Parameter param = element.get_Parameter(builtInParam);
                if (param == null || !param.HasValue)
                {
                    return 0;
                }

                return param.AsDouble();
            }
            catch
            {
                return 0;
            }
        }
    }
}

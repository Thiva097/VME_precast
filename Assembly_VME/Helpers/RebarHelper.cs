using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace Assembly_VME.Helpers
{
    public struct RebarWeightInfo
    {
        public int Quantity;
        public double DiameterMm;
        public double SingleLengthMm;
        public double TotalLengthMm;
        /// <summary>Weight of one bar in kg.</summary>
        public double UnitWeightKg;
        /// <summary>Total weight for the entire rebar set (unit * quantity) in kg.</summary>
        public double TotalWeightKg;
    }

    public static class RebarHelper
    {
        private const double SteelDensityFactor = 0.00617; // kg/mm for d(mm), L(mm): L/1000 * d^2 * 0.00617

        /// <summary>
        /// Gets all structural rebar elements hosted by the specified host element.
        /// </summary>
        public static List<Rebar> GetRebarsInHost(Document doc, ElementId hostId)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);

            IList<Element> allRebars = collector.OfClass(typeof(Rebar)).ToElements();

            List<Rebar> hostedRebars = new List<Rebar>();

            foreach (Element elem in allRebars)
            {
                Rebar rebar = elem as Rebar;
                if (rebar != null && rebar.GetHostId() == hostId)
                {
                    hostedRebars.Add(rebar);
                }
            }

            return hostedRebars;
        }

        /// <summary>
        /// Calculates rebar weight. TotalLength in Revit is length of a single bar; Quantity is bar count.
        /// </summary>
        public static RebarWeightInfo CalculateWeight(Element elem)
        {
            RebarWeightInfo info = new RebarWeightInfo
            {
                Quantity = 1,
                DiameterMm = 10.0,
                SingleLengthMm = 0,
                TotalLengthMm = 0,
                UnitWeightKg = 0,
                TotalWeightKg = 0
            };

            if (elem == null) return info;

            Parameter diaParam = elem.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
            if (diaParam != null)
            {
                info.DiameterMm = diaParam.AsDouble() * 304.8;
            }

            double totalLengthSetFeet = 0;
            if (elem is Rebar rebar)
            {
                info.Quantity = rebar.Quantity;
                totalLengthSetFeet = rebar.TotalLength; // Total length of ALL bars in the set (Revit 2025)
            }
            else if (elem is RebarInSystem ris)
            {
                info.Quantity = ris.Quantity;
                totalLengthSetFeet = ris.TotalLength;
            }

            if (info.Quantity <= 0) info.Quantity = 1;

            // info.TotalLengthMm should be the sum of all bars (from totalLengthSetFeet)
            info.TotalLengthMm = totalLengthSetFeet * 304.8;

            // info.SingleLengthMm is the length of one individual bar
            info.SingleLengthMm = info.TotalLengthMm / info.Quantity;

            // UnitWeightKg calculation (Single bar for metadata/UI)
            info.UnitWeightKg = (info.DiameterMm * info.DiameterMm * info.SingleLengthMm) / 162000.0;
            
            // TotalWeightKg: Exact literal formula provided by user: (D * D * TotalLength * Quantity) / 162000
            // Here info.TotalLengthMm already represents the Total Bar Length of the set.
            info.TotalWeightKg = (info.DiameterMm * info.DiameterMm * info.TotalLengthMm * info.Quantity) / 162000.0;

            // Round to avoid floating-point differences that cause "<varies>" in grouped schedules.
            info.UnitWeightKg = Math.Round(info.UnitWeightKg, 4);
            info.TotalWeightKg = Math.Round(info.TotalWeightKg, 3);

            return info;
        }

        /// <summary>
        /// Writes total set weight to Weight_Kg so schedules can total and show grand totals correctly.
        /// </summary>
        public static bool SetWeightParameters(Element elem, RebarWeightInfo info)
        {
            if (elem == null) return false;

            bool success = ParameterHelper.SetDoubleParameterByName(elem, "Weight_Kg", info.TotalWeightKg);

            // Keep Comments in sync as a readable fallback (not used in schedule when Weight_Kg exists).
            try
            {
                Parameter comments = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (comments != null && !comments.IsReadOnly && comments.StorageType == StorageType.String)
                {
                    comments.Set($"{info.TotalWeightKg:F2}");
                }
            }
            catch { }

            return success;
        }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Assembly_VME.Helpers
{
    public static class ScheduleHelper
    {
        private const double DefaultMinColumnWidthFeet = 0.06;
        /// <summary>
        /// Configures a numeric schedule column to sum grouped rows and appear in the grand total row.
        /// </summary>
        public static void ConfigureNumericTotalField(ScheduleField field, bool totalByAssemblyType = false)
        {
            if (field == null) return;

            try
            {
                // Force totals display type. Try-catch handles parameters that strictly cannot total.
                field.DisplayType = ScheduleFieldDisplayType.Totals;
            }
            catch { }

            try
            {
                field.TotalByAssemblyType = totalByAssemblyType;
            }
            catch { }
        }

        /// <summary>
        /// Enables grand total row and ensures the weight column can be totaled.
        /// </summary>
        public static void EnableGrandTotals(ScheduleDefinition def, params string[] weightHeadings)
        {
            if (def == null) return;

            def.ShowGrandTotal = true;
            def.ShowGrandTotalTitle = true;
            def.ShowGrandTotalCount = false;

            // Enable totals for ALL matching fields
            foreach (ScheduleFieldId fieldId in def.GetFieldOrder())
            {
                ScheduleField field = def.GetField(fieldId);
                string colHeader = field.ColumnHeading ?? "";
                
                bool matches = false;
                if (weightHeadings != null)
                {
                    foreach (string h in weightHeadings)
                    {
                        if (!string.IsNullOrEmpty(h) && colHeader.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            matches = true;
                            break;
                        }
                    }
                }

                if (matches || colHeader.IndexOf("Weight", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ConfigureNumericTotalField(field);
                }
            }
        }

        public static ScheduleField FindFieldByHeading(ScheduleDefinition def, string heading)
        {
            if (def == null || string.IsNullOrEmpty(heading)) return null;

            foreach (ScheduleFieldId fieldId in def.GetFieldOrder())
            {
                ScheduleField field = def.GetField(fieldId);
                if (field?.ColumnHeading != null &&
                    field.ColumnHeading.Equals(heading, StringComparison.OrdinalIgnoreCase))
                {
                    return field;
                }
            }
            return null;
        }

        public static void AddSortGroupField(ScheduleDefinition def, ScheduleField field)
        {
            if (def == null || field == null) return;
            try
            {
                def.AddSortGroupField(new ScheduleSortGroupField(field.FieldId));
            }
            catch { }
        }

        /// <summary>
        /// Adds the Weight column using Weight_Kg and fallbacks; ensures it is visible on the sheet.
        /// </summary>
        public static ScheduleField AddWeightColumn(Document doc, ScheduleDefinition def, string heading = "Weight")
        {
            if (doc == null || def == null) return null;

            ScheduleField field = AddFieldByName(doc, def, "Weight_Kg", heading);
            if (field == null) field = AddFieldByName(doc, def, "Weight_Dia_Total", heading);
            if (field == null) field = AddFieldContaining(doc, def, "Weight_Kg", heading);
            if (field == null) field = AddFieldContaining(doc, def, "Weight", heading);
            if (field == null) field = AddFieldByBuiltInComments(doc, def, heading);

            if (field != null)
            {
                EnsureFieldVisibleOnSheet(field);
            }

            return field;
        }

        /// <summary>
        /// Ensures a schedule column is not hidden and has a readable width on the sheet.
        /// </summary>
        public static void EnsureFieldVisibleOnSheet(ScheduleField field, double minWidthFeet = DefaultMinColumnWidthFeet)
        {
            if (field == null) return;

            try { field.IsHidden = false; } catch { }

            try
            {
                if (field.SheetColumnWidth < minWidthFeet)
                {
                    field.SheetColumnWidth = minWidthFeet;
                }
            }
            catch { }

            try
            {
                if (field.GridColumnWidth < minWidthFeet)
                {
                    field.GridColumnWidth = minWidthFeet;
                }
            }
            catch { }
        }

        /// <summary>
        /// Centers cell text vertically and horizontally for all schedule columns.
        /// </summary>
        public static void ApplyFieldAlignment(ScheduleDefinition def)
        {
            if (def == null) return;

            foreach (ScheduleFieldId fieldId in def.GetFieldOrder())
            {
                ScheduleField field = def.GetField(fieldId);
                if (field == null) continue;

                try
                {
                    field.VerticalAlignment = ScheduleVerticalAlignment.Middle;
                }
                catch { }

                try
                {
                    field.HorizontalAlignment = ScheduleHorizontalAlignment.Center;
                }
                catch { }
            }
        }

        /// <summary>
        /// Sets uniform row height on sheet so text sits centered between grid lines.
        /// </summary>
        public static void ApplyRowHeightOnSheet(ViewSchedule schedule, double rowHeightFeet = 0.011)
        {
            if (schedule == null) return;

            try
            {
                schedule.RowHeightOverride = RowHeightOverrideOptions.All;
                schedule.RowHeight = rowHeightFeet;
            }
            catch { }
        }

        /// <summary>
        /// Scales column widths so the schedule fits within the drawable sheet width.
        /// </summary>
        public static void FitScheduleColumnsToWidth(
            ScheduleDefinition def,
            double maxTotalWidthFeet,
            IReadOnlyDictionary<string, double> relativeWeights = null)
        {
            if (def == null || maxTotalWidthFeet <= 0) return;

            IList<ScheduleFieldId> fieldOrder = def.GetFieldOrder();
            if (fieldOrder.Count == 0) return;

            var fields = new List<ScheduleField>();
            var weights = new List<double>();

            foreach (ScheduleFieldId fieldId in fieldOrder)
            {
                ScheduleField field = def.GetField(fieldId);
                if (field == null) continue;

                try { field.IsHidden = false; } catch { }

                fields.Add(field);
                weights.Add(GetColumnWeight(field, relativeWeights));
            }

            if (fields.Count == 0) return;

            double weightSum = weights.Sum();
            if (weightSum <= 0)
            {
                weightSum = fields.Count;
                weights = Enumerable.Repeat(1.0, fields.Count).ToList();
            }

            var widths = new double[fields.Count];
            for (int i = 0; i < fields.Count; i++)
            {
                widths[i] = maxTotalWidthFeet * (weights[i] / weightSum);
            }

            double assignedTotal = widths.Sum();
            if (assignedTotal > maxTotalWidthFeet && assignedTotal > 0)
            {
                double scale = maxTotalWidthFeet / assignedTotal;
                for (int i = 0; i < widths.Length; i++)
                {
                    widths[i] *= scale;
                }
            }

            for (int i = 0; i < fields.Count; i++)
            {
                double width = Math.Max(widths[i], DefaultMinColumnWidthFeet);
                try { fields[i].SheetColumnWidth = width; } catch { }
                try { fields[i].GridColumnWidth = width; } catch { }
            }
        }

        /// <summary>
        /// Applies alignment, row height, and column fitting for schedules placed on assembly sheets.
        /// </summary>
        public static void ConfigureScheduleSheetAppearance(
            ViewSchedule schedule,
            double maxTableWidthFeet,
            IReadOnlyDictionary<string, double> columnWeights = null)
        {
            if (schedule?.Definition == null) return;

            ApplyFieldAlignment(schedule.Definition);
            FitScheduleColumnsToWidth(schedule.Definition, maxTableWidthFeet, columnWeights);
            ApplyRowHeightOnSheet(schedule);
        }

        private static double GetColumnWeight(
            ScheduleField field,
            IReadOnlyDictionary<string, double> relativeWeights)
        {
            string heading = field.ColumnHeading ?? string.Empty;

            if (relativeWeights != null)
            {
                foreach (var pair in relativeWeights)
                {
                    if (heading.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return pair.Value;
                    }
                }
            }

            if (heading.IndexOf("Shape", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 1.1;
            }

            if (heading.IndexOf("Diameter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                heading.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 1.2;
            }

            if (heading.Length == 1 && char.IsLetter(heading[0]))
            {
                return 0.65;
            }

            if (heading.IndexOf("Length", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 1.15;
            }

            if (heading.IndexOf("Quantity", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0.75;
            }

            if (heading.IndexOf("Weight", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0.9;
            }

            return 1.0;
        }

        public static ScheduleField AddFieldByName(Document doc, ScheduleDefinition def, string name, string heading = null)
        {
            if (doc == null || def == null || string.IsNullOrEmpty(name)) return null;

            foreach (SchedulableField sf in def.GetSchedulableFields())
            {
                string sfName = sf.GetName(doc);
                if (!sfName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ScheduleField existing = FindFieldByParameterId(def, sf.ParameterId);
                if (existing != null)
                {
                    if (!string.IsNullOrEmpty(heading))
                    {
                        existing.ColumnHeading = heading;
                    }
                    return existing;
                }

                ScheduleField field = def.AddField(sf);
                if (field != null)
                {
                    field.ColumnHeading = string.IsNullOrEmpty(heading) ? name : heading;
                    EnsureFieldVisibleOnSheet(field);
                }
                return field;
            }

            return null;
        }

        public static ScheduleField AddFieldContaining(Document doc, ScheduleDefinition def, string keyword, string heading)
        {
            if (doc == null || def == null || string.IsNullOrEmpty(keyword)) return null;

            foreach (SchedulableField sf in def.GetSchedulableFields())
            {
                string sfName = sf.GetName(doc);
                if (sfName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                ScheduleField existing = FindFieldByParameterId(def, sf.ParameterId);
                if (existing != null)
                {
                    if (!string.IsNullOrEmpty(heading))
                    {
                        existing.ColumnHeading = heading;
                    }
                    return existing;
                }

                ScheduleField field = def.AddField(sf);
                if (field != null)
                {
                    field.ColumnHeading = heading ?? sfName;
                    EnsureFieldVisibleOnSheet(field);
                }
                return field;
            }

            return null;
        }

        private static ScheduleField AddFieldByBuiltInComments(Document doc, ScheduleDefinition def, string heading)
        {
            foreach (SchedulableField sf in def.GetSchedulableFields())
            {
                if (sf.ParameterId == null || sf.ParameterId == ElementId.InvalidElementId)
                {
                    continue;
                }

                string sfName = sf.GetName(doc);
                if (sfName.IndexOf("Comment", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                ScheduleField field = def.AddField(sf);
                if (field != null)
                {
                    field.ColumnHeading = heading;
                    EnsureFieldVisibleOnSheet(field);
                }
                return field;
            }

            return null;
        }

        private static ScheduleField FindFieldByParameterId(ScheduleDefinition def, ElementId parameterId)
        {
            if (def == null || parameterId == null || parameterId == ElementId.InvalidElementId)
            {
                return null;
            }

            foreach (ScheduleFieldId fid in def.GetFieldOrder())
            {
                ScheduleField f = def.GetField(fid);
                if (f != null && f.ParameterId.Value == parameterId.Value)
                {
                    return f;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the vertical height of a schedule on a sheet (feet) for placing content below it.
        /// </summary>
        public static double GetScheduleHeightOnSheet(ViewSchedule schedule)
        {
            if (schedule == null) return 0.25;

            try
            {
                ScheduleHeightsOnSheet heights = schedule.GetScheduleHeightsOnSheet();
                if (heights != null && heights.IsValidObject)
                {
                    double total = heights.TitleHeight + heights.ColumnHeaderHeight;
                    IList<double> bodyHeights = heights.GetBodyRowHeights();
                    if (bodyHeights != null)
                    {
                        foreach (double rowHeight in bodyHeights)
                        {
                            total += rowHeight;
                        }
                    }

                    if (total > 0.05)
                    {
                        return total;
                    }
                }
            }
            catch { }

            return EstimateScheduleHeightOnSheet(schedule);
        }

        /// <summary>
        /// Estimates schedule height when sheet heights are not yet available.
        /// </summary>
        public static double EstimateScheduleHeightOnSheet(ViewSchedule schedule)
        {
            double heightFeet = 0.12;
            try
            {
                TableData tableData = schedule.GetTableData();
                if (tableData == null) return heightFeet;

                heightFeet += EstimateSectionHeight(tableData.GetSectionData(SectionType.Header));
                heightFeet += EstimateSectionHeight(tableData.GetSectionData(SectionType.Body));
                heightFeet += EstimateSectionHeight(tableData.GetSectionData(SectionType.Footer));
            }
            catch { }

            return heightFeet;
        }

        private static double EstimateSectionHeight(TableSectionData section)
        {
            if (section == null || section.NumberOfRows <= 0) return 0;
            return section.NumberOfRows * 0.065;
        }

        /// <summary>
        /// Creates a one-row generic-module footer schedule aligned with the material summary columns.
        /// </summary>
        public static ViewSchedule CreateGenericModuleFooterSchedule(
            Document doc,
            ElementId assemblyId,
            int genericModelCount,
            string assemblyName)
        {
            if (genericModelCount <= 0) return null;

            ViewSchedule footerSchedule = AssemblyViewUtils.CreateSingleCategorySchedule(
                doc, assemblyId, new ElementId(BuiltInCategory.OST_GenericModel));

            string baseName = $"Generic Module Row - {assemblyName}";
            try { footerSchedule.Name = baseName; }
            catch
            {
                AssignUniqueScheduleName(footerSchedule, baseName);
            }

            ScheduleDefinition def = footerSchedule.Definition;

            IList<ScheduleFieldId> fieldIds = def.GetFieldOrder();
            for (int i = fieldIds.Count - 1; i >= 0; i--)
            {
                try { def.RemoveField(fieldIds[i]); } catch { }
            }

            AddFieldByNameOrFallback(doc, def, "Summary_Footer_Label", "Bar Diameter");
            AddFieldByNameOrFallback(doc, def, "Summary_Footer_Length", "Total Bar Length");
            AddFieldByNameOrFallback(doc, def, "Summary_Footer_Weight", "Weight");

            def.IsItemized = false;
            def.ShowTitle = false;
            def.ShowHeaders = false;
            def.ShowGrandTotal = false;

            try { def.ClearSortGroupFields(); } catch { }

            ScheduleField labelField = FindFieldByHeading(def, "Bar Diameter");
            AddSortGroupField(def, labelField);

            ApplyFieldAlignment(def);
            ApplyRowHeightOnSheet(footerSchedule);

            return footerSchedule;
        }

        private static ScheduleField AddFieldByNameOrFallback(
            Document doc,
            ScheduleDefinition def,
            string paramName,
            string heading)
        {
            ScheduleField field = AddFieldByName(doc, def, paramName, heading);
            if (field != null)
            {
                EnsureFieldVisibleOnSheet(field);
            }
            return field;
        }

        public static void AssignUniqueScheduleName(ViewSchedule schedule, string baseName)
        {
            if (schedule == null) return;
            try
            {
                schedule.Name = baseName;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                int counter = 2;
                while (true)
                {
                    try
                    {
                        schedule.Name = $"{baseName} ({counter})";
                        break;
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        counter++;
                    }
                }
            }
        }
    }
}

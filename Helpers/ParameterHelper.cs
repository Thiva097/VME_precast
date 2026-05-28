using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.UI;

namespace Assembly_VME.Helpers
{
    public static class ParameterHelper
    {
        private static readonly string[] SharedParamNames =
        {
            "Panel_Name",
            "Weight_Kg",
            "Weight_Dia_Total",
            "Summary_Footer_Label",
            "Summary_Footer_Length",
            "Summary_Footer_Weight"
        };

        /// <summary>
        /// Safely sets the string value of a parameter by name.
        /// </summary>
        public static bool SetParameterByName(Element element, string paramName, string value)
        {
            if (element == null || string.IsNullOrEmpty(paramName)) return false;

            Parameter param = element.LookupParameter(paramName);

            if (param != null && !param.IsReadOnly)
            {
                if (param.StorageType == StorageType.String)
                {
                    param.Set(value);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Safely sets a numeric shared/instance parameter by name.
        /// </summary>
        public static bool SetDoubleParameterByName(Element element, string paramName, double value)
        {
            if (element == null || string.IsNullOrEmpty(paramName)) return false;

            Parameter param = element.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return false;

            if (param.StorageType == StorageType.Double)
            {
                param.Set(value);
                return true;
            }

            if (param.StorageType == StorageType.Integer)
            {
                param.Set((int)Math.Round(value));
                return true;
            }

            if (param.StorageType == StorageType.String)
            {
                param.Set($"{value:F2}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Safely sets the string value of a built-in parameter by its enumeration.
        /// </summary>
        public static bool SetBuiltInParameter(Element element, BuiltInParameter bip, string value)
        {
            if (element == null) return false;

            Parameter param = element.get_Parameter(bip);

            if (param != null && !param.IsReadOnly)
            {
                if (param.StorageType == StorageType.String)
                {
                    param.Set(value);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Retrieves the value of a parameter by name as a string.
        /// </summary>
        public static string GetParameterValue(Element element, string paramName)
        {
            if (element == null || string.IsNullOrEmpty(paramName)) return "-";
            Parameter param = element.LookupParameter(paramName);
            if (param == null || !param.HasValue) return "-";

            switch (param.StorageType)
            {
                case StorageType.Double:
                    return param.AsDouble().ToString("F2");
                case StorageType.String:
                    return param.AsString() ?? "-";
                case StorageType.Integer:
                    return param.AsInteger().ToString();
                case StorageType.ElementId:
                    return param.AsElementId().ToString();
                default:
                    return "-";
            }
        }

        /// <summary>
        /// Retrieves the value of a built-in parameter as a string, with optional unit conversion.
        /// </summary>
        public static string GetBuiltInParameterValue(Element elem, BuiltInParameter bip, bool convertToMm = false)
        {
            if (elem == null) return "-";
            Parameter p = elem.get_Parameter(bip);
            if (p == null || !p.HasValue) return "-";

            if (p.StorageType == StorageType.Double)
            {
                double val = p.AsDouble();
                if (convertToMm)
                {
                    if (bip == BuiltInParameter.CURVE_ELEM_LENGTH || bip == BuiltInParameter.WALL_USER_HEIGHT_PARAM || bip == BuiltInParameter.WALL_ATTR_WIDTH_PARAM)
                        return $"{Math.Round(val * 304.8)} mm";
                    if (bip == BuiltInParameter.HOST_AREA_COMPUTED)
                        return $"{Math.Round(val * 304.8 * 304.8 / 100) / 100.0} m²";
                    if (bip == BuiltInParameter.HOST_VOLUME_COMPUTED)
                        return $"{Math.Round(val * 304.8 * 304.8 * 304.8 / (1000 * 1000 * 1000) * 100) / 100.0} m³";
                }
                return val.ToString("F2");
            }
            if (p.StorageType == StorageType.String) return p.AsString() ?? "-";
            if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
            return "-";
        }

        /// <summary>
        /// Ensures all VME shared parameters exist and are bound to the correct categories.
        /// </summary>
        public static void EnsureSharedParametersExist(Document doc)
        {
            // Build a map of existing parameter names AND their category bindings
            BindingMap bindingMap = doc.ParameterBindings;

            // Check which of our params are already correctly bound to Rebar
            HashSet<string> boundToRebar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> boundAny = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            DefinitionBindingMapIterator it = bindingMap.ForwardIterator();
            while (it.MoveNext())
            {
                string pName = it.Key.Name;
                boundAny.Add(pName);

                InstanceBinding ib = it.Current as InstanceBinding;
                if (ib == null) continue;
                foreach (Category cat in ib.Categories)
                {
                    if (cat != null && cat.Id.Value == (long)BuiltInCategory.OST_Rebar)
                    {
                        boundToRebar.Add(pName);
                        break;
                    }
                }
            }

            // Determine which params still need to be created
            bool needWeightKg = !boundToRebar.Contains("Weight_Kg");
            bool needWeightDia = !boundToRebar.Contains("Weight_Dia_Total");
            bool needPanelName = !boundAny.Contains("Panel_Name");
            bool needFooterLabel = !boundAny.Contains("Summary_Footer_Label");
            bool needFooterLength = !boundAny.Contains("Summary_Footer_Length");
            bool needFooterWeight = !boundAny.Contains("Summary_Footer_Weight");

            bool nothingToDo = !needWeightKg && !needWeightDia &&
                               !needPanelName && !needFooterLabel &&
                               !needFooterLength && !needFooterWeight;
            if (nothingToDo) return;

            Application app = doc.Application;
            string originalSharedParamFile = app.SharedParametersFilename;
            string tempFile = Path.Combine(
                Path.GetTempPath(),   // use system temp folder — more reliable than MyDocuments
                $"VME_SP_{Guid.NewGuid():N}.txt");

            try
            {
                // Write the temp shared parameter file
                using (StreamWriter sw = new StreamWriter(tempFile, false, System.Text.Encoding.UTF8))
                {
                    sw.WriteLine("# This is a Revit shared parameter file.");
                    sw.WriteLine("# Do not edit manually.");
                    sw.WriteLine("*META\tVERSION\tMINVERSION");
                    sw.WriteLine("META\t2\t1");
                    sw.WriteLine("*GROUP\tID\tNAME");
                    sw.WriteLine("GROUP\t1\tVME_Parameters");
                    sw.WriteLine("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE");

                    if (needPanelName)
                        sw.WriteLine("PARAM\td8a55c2f-e8b2-4d2c-8cb4-3ef89a05b3cb\tPanel_Name\tTEXT\t\t1\t1\t\t1\t0");
                    if (needWeightKg)
                        sw.WriteLine("PARAM\tb1a2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d\tWeight_Kg\tNUMBER\t\t1\t1\t\t1\t0");
                    if (needWeightDia)
                        sw.WriteLine("PARAM\tc2b3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e\tWeight_Dia_Total\tNUMBER\t\t1\t1\t\t1\t0");
                    if (needFooterLabel)
                        sw.WriteLine("PARAM\td3c4e5f6-a7b8-4c9d-0e1f-2a3b4c5d6e7f\tSummary_Footer_Label\tTEXT\t\t1\t1\t\t1\t0");
                    if (needFooterLength)
                        sw.WriteLine("PARAM\te4d5f6a7-b8c9-4d0e-1f2a-3b4c5d6e7f8a\tSummary_Footer_Length\tTEXT\t\t1\t1\t\t1\t0");
                    if (needFooterWeight)
                        sw.WriteLine("PARAM\tf5e6a7b8-c9d0-4e1f-2a3b-4c5d6e7f8a9b\tSummary_Footer_Weight\tTEXT\t\t1\t1\t\t1\t0");
                }

                // Point Revit at the temp file
                app.SharedParametersFilename = tempFile;

                // IMPORTANT: verify Revit can actually open it before proceeding
                DefinitionFile defFile = null;
                try
                {
                    defFile = app.OpenSharedParameterFile();
                }
                catch (Exception openEx)
                {
                    TaskDialog.Show("Shared Parameter Error",
                        $"Could not open temp shared parameter file:\n{openEx.Message}\n\nPath: {tempFile}");
                    return;
                }

                if (defFile == null)
                {
                    TaskDialog.Show("Shared Parameter Error",
                        "OpenSharedParameterFile returned null. The file may be malformed.");
                    return;
                }

                DefinitionGroup defGroup = defFile.Groups.get_Item("VME_Parameters");
                if (defGroup == null)
                {
                    TaskDialog.Show("Shared Parameter Error",
                        "VME_Parameters group not found in temp file.");
                    return;
                }

                using (Transaction trans = new Transaction(doc, "Create VME Shared Parameters"))
                {
                    trans.Start();

                    Category rebarCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Rebar);
                    Category wallCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                    Category gmCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);

                    if (needPanelName)
                        BindTextParam(doc, app, defGroup, "Panel_Name", boundAny, wallCat, rebarCat, gmCat);

                    if (needWeightKg)
                        BindNumberParam(doc, app, defGroup, "Weight_Kg", boundToRebar, rebarCat);

                    if (needWeightDia)
                        BindNumberParam(doc, app, defGroup, "Weight_Dia_Total", boundToRebar, rebarCat);

                    if (needFooterLabel)
                        BindTextParam(doc, app, defGroup, "Summary_Footer_Label", boundAny, gmCat);

                    if (needFooterLength)
                        BindTextParam(doc, app, defGroup, "Summary_Footer_Length", boundAny, gmCat);

                    if (needFooterWeight)
                        BindTextParam(doc, app, defGroup, "Summary_Footer_Weight", boundAny, gmCat);

                    trans.Commit();
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ERROR", $"EnsureSharedParametersExist failed:\n{ex}");
            }
            finally
            {
                // Always restore the original shared param file first
                app.SharedParametersFilename = originalSharedParamFile ?? string.Empty;

                // Then delete the temp file — after restoring, Revit no longer holds it
                try
                {
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                }
                catch { }
            }
        }

        private static void BindTextParam(
            Document doc,
            Application app,
            DefinitionGroup defGroup,
            string paramName,
            HashSet<string> existing,
            params Category[] categories)
        {
            if (existing.Contains(paramName)) return;

            Definition def = defGroup.Definitions.get_Item(paramName);
            if (def == null) return;

            CategorySet set = app.Create.NewCategorySet();
            foreach (Category cat in categories)
            {
                if (cat != null) set.Insert(cat);
            }
            InsertBindingSafely(doc, def, app.Create.NewInstanceBinding(set));
        }

        private static void BindNumberParam(
            Document doc,
            Application app,
            DefinitionGroup defGroup,
            string paramName,
            HashSet<string> existing,
            Category category)
        {
            if (existing.Contains(paramName)) return;

            Definition def = defGroup.Definitions.get_Item(paramName);
            if (def == null) return;

            CategorySet set = app.Create.NewCategorySet();
            set.Insert(category);
            InsertBindingSafely(doc, def, app.Create.NewInstanceBinding(set));
        }

        private static void InsertBindingSafely(Document doc, Definition definition, Binding binding)
        {
            BindingMap bindingMap = doc.ParameterBindings;

            try
            {
                Type groupTypeIdType = typeof(GroupTypeId);
                System.Reflection.PropertyInfo identityDataProp = groupTypeIdType.GetProperty(
                    "IdentityData",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (identityDataProp != null)
                {
                    object forgeTypeId = identityDataProp.GetValue(null);
                    System.Reflection.MethodInfo insertMethod = bindingMap.GetType().GetMethod(
                        "Insert",
                        new Type[] { typeof(Definition), typeof(Binding), forgeTypeId.GetType() });
                    if (insertMethod != null)
                    {
                        insertMethod.Invoke(bindingMap, new object[] { definition, binding, forgeTypeId });
                        return;
                    }
                }
            }
            catch { }

            try
            {
                Type bipgType = doc.Application.GetType().Assembly.GetType("Autodesk.Revit.DB.BuiltInParameterGroup");
                if (bipgType != null)
                {
                    object pgIdentityData = Enum.Parse(bipgType, "PG_IDENTITY_DATA");
                    System.Reflection.MethodInfo insertMethod = bindingMap.GetType().GetMethod(
                        "Insert",
                        new Type[] { typeof(Definition), typeof(Binding), bipgType });
                    if (insertMethod != null)
                    {
                        insertMethod.Invoke(bindingMap, new object[] { definition, binding, pgIdentityData });
                    }
                }
            }
            catch { }
        }
    }
}

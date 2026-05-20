using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.ApplicationServices;

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
            HashSet<string> existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            BindingMap bindingMap = doc.ParameterBindings;
            DefinitionBindingMapIterator it = bindingMap.ForwardIterator();
            while (it.MoveNext())
            {
                existing.Add(it.Key.Name);
            }

            bool allExist = true;
            foreach (string name in SharedParamNames)
            {
                if (!existing.Contains(name))
                {
                    allExist = false;
                    break;
                }
            }
            if (allExist) return;

            Application app = doc.Application;
            string originalSharedParamFile = app.SharedParametersFilename;
            string tempFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "VME_SharedParameters_Temp.txt");

            try
            {
                using (StreamWriter sw = File.CreateText(tempFile))
                {
                    sw.WriteLine("# This is a Revit shared parameter file.");
                    sw.WriteLine("# Do not edit manually.");
                    sw.WriteLine("*META\tVERSION\tMINVERSION");
                    sw.WriteLine("META\t2.0\t2.0");
                    sw.WriteLine("*GROUP\tID\tNAME");
                    sw.WriteLine("GROUP\t1\tVME_Parameters");
                    sw.WriteLine("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE");

                    if (!existing.Contains("Panel_Name"))
                    {
                        sw.WriteLine("PARAM\td8a55c2f-e8b2-4d2c-8cb4-3ef89a05b3cb\tPanel_Name\tTEXT\t\t1\t1\t\t1");
                    }
                    if (!existing.Contains("Weight_Kg"))
                    {
                        sw.WriteLine("PARAM\tb1a2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d\tWeight_Kg\tNUMBER\t\t1\t1\t\t1");
                    }
                    if (!existing.Contains("Weight_Dia_Total"))
                    {
                        sw.WriteLine("PARAM\tc2b3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e\tWeight_Dia_Total\tNUMBER\t\t1\t1\t\t1");
                    }
                    if (!existing.Contains("Summary_Footer_Label"))
                    {
                        sw.WriteLine("PARAM\td3c4e5f6-a7b8-4c9d-0e1f-2a3b4c5d6e7f\tSummary_Footer_Label\tTEXT\t\t1\t1\t\t1");
                    }
                    if (!existing.Contains("Summary_Footer_Length"))
                    {
                        sw.WriteLine("PARAM\te4d5f6a7-b8c9-4d0e-1f2a-3b4c5d6e7f8a\tSummary_Footer_Length\tTEXT\t\t1\t1\t\t1");
                    }
                    if (!existing.Contains("Summary_Footer_Weight"))
                    {
                        sw.WriteLine("PARAM\tf5e6a7b8-c9d0-4e1f-2a3b-4c5d6e7f8a9b\tSummary_Footer_Weight\tTEXT\t\t1\t1\t\t1");
                    }
                }

                app.SharedParametersFilename = tempFile;
                DefinitionFile defFile = app.OpenSharedParameterFile();
                if (defFile == null) return;

                DefinitionGroup defGroup = defFile.Groups.get_Item("VME_Parameters");
                if (defGroup == null) return;

                using (Transaction trans = new Transaction(doc, "Create VME Shared Parameters"))
                {
                    trans.Start();

                    Category rebarCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Rebar);
                    Category wallCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                    Category gmCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);

                    BindTextParam(doc, app, defGroup, "Panel_Name", existing, wallCat, rebarCat, gmCat);
                    BindNumberParam(doc, app, defGroup, "Weight_Kg", existing, rebarCat);
                    BindNumberParam(doc, app, defGroup, "Weight_Dia_Total", existing, rebarCat);
                    BindTextParam(doc, app, defGroup, "Summary_Footer_Label", existing, gmCat);
                    BindTextParam(doc, app, defGroup, "Summary_Footer_Length", existing, gmCat);
                    BindTextParam(doc, app, defGroup, "Summary_Footer_Weight", existing, gmCat);

                    trans.Commit();
                }
            }
            catch (Exception)
            {
                // Silent fallback if binding fails in this environment.
            }
            finally
            {
                app.SharedParametersFilename = originalSharedParamFile;
                try
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
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

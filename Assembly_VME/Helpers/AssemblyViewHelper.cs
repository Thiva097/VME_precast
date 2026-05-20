using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace Assembly_VME.Helpers
{
    public static class AssemblyViewHelper
    {
        /// <summary>
        /// Finds an existing orthographic 3D view for the assembly, or creates one via AssemblyViewUtils.
        /// </summary>
        public static View3D GetOrCreateAssembly3DOrthoView(Document doc, ElementId assemblyId, string assemblyName)
        {
            View3D existing = FindAssembly3DOrthoView(doc, assemblyId, assemblyName);
            if (existing != null)
            {
                return existing;
            }

            View3D created = AssemblyViewUtils.Create3DOrthographic(doc, assemblyId);
            if (created == null)
            {
                return null;
            }

            try
            {
                string viewName = $"3D Ortho - {assemblyName}";
                created.Name = GetUniqueViewName(doc, viewName);
            }
            catch { }

            // Required after creating assembly 3D views before they can be placed on a sheet.
            doc.Regenerate();

            return created;
        }

        /// <summary>
        /// Places the assembly 3D orthographic view in the upper-right drawable area (clear of title block).
        /// </summary>
        public static bool Place3DOrthoViewOnSheet(
            Document doc,
            ViewSheet sheet,
            View3D view3d,
            XYZ centerPoint)
        {
            if (doc == null || sheet == null || view3d == null)
            {
                return false;
            }

            ElementId viewIdToPlace = view3d.Id;

            if (!Viewport.CanAddViewToSheet(doc, sheet.Id, viewIdToPlace))
            {
                try
                {
                    viewIdToPlace = view3d.Duplicate(ViewDuplicateOption.Duplicate);
                }
                catch
                {
                    return false;
                }
            }

            if (!Viewport.CanAddViewToSheet(doc, sheet.Id, viewIdToPlace))
            {
                return false;
            }

            Viewport viewport = Viewport.Create(doc, sheet.Id, viewIdToPlace, centerPoint);
            if (viewport == null)
            {
                return false;
            }

            try
            {
                viewport.SetBoxCenter(centerPoint);
            }
            catch { }

            return true;
        }

        private static View3D FindAssembly3DOrthoView(Document doc, ElementId assemblyId, string assemblyName)
        {
            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(v => !v.IsTemplate && !v.IsPerspective)
                .ToList();

            // Prefer views owned by this assembly instance.
            View3D byAssembly = views.FirstOrDefault(v => v.AssociatedAssemblyInstanceId == assemblyId);
            if (byAssembly != null)
            {
                return byAssembly;
            }

            // Fallback: view name contains the assembly name.
            if (!string.IsNullOrEmpty(assemblyName))
            {
                View3D byName = views.FirstOrDefault(v =>
                    v.Name != null &&
                    v.Name.IndexOf(assemblyName, StringComparison.OrdinalIgnoreCase) >= 0);
                if (byName != null)
                {
                    return byName;
                }
            }

            return null;
        }

        private static string GetUniqueViewName(Document doc, string baseName)
        {
            var existingNames = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Select(v => v.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existingNames.Contains(baseName))
            {
                return baseName;
            }

            int counter = 2;
            while (true)
            {
                string candidate = $"{baseName} ({counter})";
                if (!existingNames.Contains(candidate))
                {
                    return candidate;
                }
                counter++;
            }
        }
    }
}

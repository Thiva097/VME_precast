using System;
using Autodesk.Revit.DB;

namespace Assembly_VME.Helpers
{
    public static class AssemblyHelper
    {
        /// <summary>
        /// Gets the name of the assembly that an element belongs to.
        /// Returns null if the element is not part of an assembly.
        /// </summary>
        public static string GetAssemblyName(Element element)
        {
            if (element == null) return null;

            Document doc = element.Document;
            ElementId assemblyId = element.AssemblyInstanceId;

            if (assemblyId == null || assemblyId == ElementId.InvalidElementId)
            {
                return null;
            }

            AssemblyInstance assembly = doc.GetElement(assemblyId) as AssemblyInstance;
            if (assembly != null)
            {
                // Prioritize AssemblyName over Type name, or use Type name as fallback.
                return assembly.Name;
            }

            return null;
        }
    }
}

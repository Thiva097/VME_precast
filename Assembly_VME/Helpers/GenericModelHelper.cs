using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Assembly_VME.Helpers
{
    public static class GenericModelHelper
    {
        /// <summary>
        /// Gets all Generic Model family instances hosted by the specified host element.
        /// </summary>
        public static List<FamilyInstance> GetGenericModelsInHost(Document doc, ElementId hostId)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            
            // Get all Generic Model family instances in the document
            IList<Element> allGenericModels = collector
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .OfClass(typeof(FamilyInstance))
                .ToElements();
            
            List<FamilyInstance> hostedGenericModels = new List<FamilyInstance>();

            foreach (Element elem in allGenericModels)
            {
                FamilyInstance fi = elem as FamilyInstance;
                if (fi != null && fi.Host != null && fi.Host.Id == hostId)
                {
                    hostedGenericModels.Add(fi);
                }
            }

            return hostedGenericModels;
        }
    }
}

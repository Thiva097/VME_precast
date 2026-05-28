using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VME_Apollo
{
    public class DrawingCreationHandler : IExternalEventHandler
    {
        public double Length { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double WallThickness { get; set; }
        public double BaseSlabThickness { get; set; }
        public double CoverSlabThickness { get; set; }
        public string ElementType { get; set; }
        public bool ExportToPDF { get; set; }

        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;

            try
            {
                // 1. RETRIEVE EXISTING SHEET (Broad Search)
                string searchTag = string.IsNullOrEmpty(ElementType) ? "Tank" : ElementType.Replace(" Tank", "");
                ViewSheet sheet = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .FirstOrDefault(x => x.Name.Contains(searchTag, StringComparison.OrdinalIgnoreCase) ||
                                       x.Name.Contains("Plan & Sections", StringComparison.OrdinalIgnoreCase) ||
                                       x.Name.Contains("Precast", StringComparison.OrdinalIgnoreCase));

                // Fallback 1: WT Sheet Number
                if (sheet == null)
                {
                    sheet = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .FirstOrDefault(x => x.Name.Contains("Tank", StringComparison.OrdinalIgnoreCase) ||
                                           x.SheetNumber.StartsWith("WT", StringComparison.OrdinalIgnoreCase));
                }

                // Fallback 2: Any Sheet
                if (sheet == null)
                {
                    sheet = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().FirstOrDefault();
                }

                if (sheet == null)
                {
                    TaskDialog.Show("VME Apollo", "No sheets found in the Revit model. Please ensure at least one sheet exists.");
                    return;
                }

                // --- PREVIEW LOGIC: Export Sheet to Image ---
                string tempFolder = Path.GetTempPath();
                string previewPath = Path.Combine(tempFolder, "VME_Apollo_Preview.png");

                try
                {
                    if (File.Exists(previewPath)) File.Delete(previewPath);

                    ImageExportOptions imgOptions = new ImageExportOptions();
                    imgOptions.FilePath = previewPath.Replace(".png", "");
                    imgOptions.ExportRange = ExportRange.SetOfViews;
                    imgOptions.SetViewsAndSheets(new List<ElementId> { sheet.Id });
                    imgOptions.PixelSize = 1024;

                    doc.ExportImage(imgOptions);
                }
                catch { /* Ignore image export errors for stability */ }

                if (ExportToPDF)
                {
                    // --- PDF EXPORT LOGIC ---
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    string fileName = $"{searchTag}_Drawing_{DateTime.Now:yyyyMMdd_HHmm}";

                    PDFExportOptions options = new PDFExportOptions();
                    options.Combine = true;
                    options.FileName = fileName;

                    IList<ElementId> views = new List<ElementId> { sheet.Id };
                    doc.Export(desktop, views, options);

                    TaskDialog.Show("VME Apollo", $"Successfully exported drawing to PDF:\n{desktop}\\{fileName}.pdf");
                }
                else
                {
                    app.ActiveUIDocument.ActiveView = sheet;
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("VME Apollo Error", "An error occurred during drawing retrieval/export:\n" + ex.Message);
            }
        }

        public string GetName() => "VME Apollo Drawing Handler";
    }
}

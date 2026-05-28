using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VME_Apollo
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var modelHandler = new ModelGenerationHandler();
                var modelEvent = ExternalEvent.Create(modelHandler);

                var drawingHandler = new DrawingCreationHandler();
                var drawingEvent = ExternalEvent.Create(drawingHandler);

                var window = new ConfiguratorWindow(modelEvent, modelHandler, drawingEvent, drawingHandler);

                // Set Revit as owner window
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;

                window.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
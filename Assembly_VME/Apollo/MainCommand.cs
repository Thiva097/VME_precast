using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VME_Apollo
{
    [Transaction(TransactionMode.Manual)]
    public class MainCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Initialize handlers and events
                ModelGenerationHandler modelHandler = new ModelGenerationHandler();
                ExternalEvent modelEvent = ExternalEvent.Create(modelHandler);

                DrawingCreationHandler drawingHandler = new DrawingCreationHandler();
                ExternalEvent drawingEvent = ExternalEvent.Create(drawingHandler);

                // Initialize and show the WPF window (Modeless)
                ConfiguratorWindow window = new ConfiguratorWindow(modelEvent, modelHandler, drawingEvent, drawingHandler);
                
                // Set Revit as the owner to keep the window on top and prevent it from closing automatically
                System.Windows.Interop.WindowInteropHelper helper = new System.Windows.Interop.WindowInteropHelper(window);
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

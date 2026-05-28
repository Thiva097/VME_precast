using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Assembly_VME.UI;
using Assembly_VME.Helpers;

namespace Assembly_VME.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class VMECommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;

            try
            {
                // Create the ExternalEvent handler — this is the key to running
                // Revit transactions safely from WPF button clicks.
                var handler = new RevitActionHandler();
                ExternalEvent revitEvent = ExternalEvent.Create(handler);

                VMEWindow window = new VMEWindow(uidoc, handler, revitEvent);
                
                // Set Revit as owner
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

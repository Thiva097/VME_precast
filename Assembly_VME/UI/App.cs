using System;
using Autodesk.Revit.UI;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace Assembly_VME.UI
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            // Create a custom ribbon tab
            string tabName = "VME Tools";
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch (Exception)
            {
                // Tab might already exist
            }

            // Create a ribbon panel
            RibbonPanel panel = application.CreateRibbonPanel(tabName, "BBS Sync");

            // Get the assembly path
            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;

            // Create push button for the BBS Sync Command
            PushButtonData buttonData = new PushButtonData(
                "cmdSyncBbs",
                "Sync BBS\nParameters",
                thisAssemblyPath,
                "Assembly_VME.Commands.SyncAssemblyBbsCommand");

            buttonData.ToolTip = "Synchronizes Assembly Name to Wall, Rebar, and Generic Model Panel_Name parameter.";

            // Add the button to the panel
            PushButton pb = panel.AddItem(buttonData) as PushButton;

            // Note: You can add an icon here if you have a 32x32 image as a resource:
            // pb.LargeImage = new BitmapImage(new Uri("pack://application:,,,/Assembly_VME;component/Resources/icon32.png"));

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}

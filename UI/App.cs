using Autodesk.Revit.UI;
using System;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace Assembly_VME.UI
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {

            string tabName = "VME";
            try { application.CreateRibbonTab(tabName); }
            catch { /* already exists */ }

            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // ── Panel 1: VME Apollo (Configurator Tool) ────────────────────
            //RibbonPanel apolloPanel = application.CreateRibbonPanel(tabName, "VME Configurator");

            //PushButtonData apolloBtn = new PushButtonData(
            //    "cmdApollo",
            //    "Configurator",
            //    assemblyPath,
            //    "VME_Apollo.Command");
            //apolloBtn.ToolTip = "Open the VME Apollo Water Tank Configurator.";

            //PushButton pbApollo = apolloPanel.AddItem(apolloBtn) as PushButton;
            //try
            //{
            //    pbApollo.LargeImage = new BitmapImage(new Uri(
            //        "pack://application:,,,/Assembly_VME;component/Apollo/Assets/config_ribbon.png"));
            //}
            //catch { }

            // ── Panel 2: VME Workspace (BBS Tool) ─────────────────────────
            RibbonPanel assemblyPanel = application.CreateRibbonPanel(tabName, "VME BBS");

            PushButtonData bbsBtn = new PushButtonData(
                "cmdBBS",
                "BBS Workspace",
                assemblyPath,
                "Assembly_VME.Commands.VMECommand");
            bbsBtn.ToolTip = "Open the VME Precast BBS Workspace.";

            PushButton pbBBS = assemblyPanel.AddItem(bbsBtn) as PushButton;
            try
            {
                pbBBS.LargeImage = new BitmapImage(new Uri(
                    "pack://application:,,,/Assembly_VME;component/Apollo/Assets/config_ribbon.png"));
            }
            catch { }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
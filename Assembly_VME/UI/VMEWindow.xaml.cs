using System;
using System.Windows;
using Autodesk.Revit.UI;
using Assembly_VME.ViewModels;
using Assembly_VME.Helpers;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Assembly_VME.UI
{
    public partial class VMEWindow : Window
    {
        public VMEWindow(UIDocument uidoc, Assembly_VME.Helpers.RevitActionHandler handler, ExternalEvent revitEvent)
        {
            InitializeComponent();
            
            // Initialize the ViewModel which handles all BBS Sync logic
            // Pass the handler and event so the VM can run Revit API actions safely
            var viewModel = new SyncViewModel(uidoc, handler, revitEvent);
            this.DataContext = viewModel;
        }
        private void btnHelp_Click(object sender, RoutedEventArgs e)
        {
            OpenHelpPdf("VME_Workspace.pdf");
        }

        private void btnSOP_Click(object sender, RoutedEventArgs e)
        {
            OpenHelpPdf("Assembly_help.pdf");
        }

        private void OpenHelpPdf(string fileName)
        {
            try
            {
                // Use Assembly.Location to find the actual DLL folder, as AppContext.BaseDirectory
                // can point to the Revit install folder (e.g. C:\Program Files\Autodesk\Revit 2025\)
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string assemblyDir = Path.GetDirectoryName(assemblyPath);
                string pdfPath = Path.Combine(assemblyDir, "Apollo", "Assets", fileName);
                
                if (!File.Exists(pdfPath))
                {
                    // Fallback: look for the PDF directly in the output folder
                    pdfPath = Path.Combine(assemblyDir, fileName);
                }

                if (File.Exists(pdfPath))
                {
                    Process.Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true });
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"Could not find {fileName}.\n\nSearched in:\n{assemblyDir}\n\nPlease ensure the file is in the Apollo\\Assets folder.",
                        "Document Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error opening document {fileName}: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

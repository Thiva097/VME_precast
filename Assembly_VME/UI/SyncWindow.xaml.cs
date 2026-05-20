using System.Windows;
using Autodesk.Revit.UI;
using Assembly_VME.ViewModels;

namespace Assembly_VME.UI
{
    /// <summary>
    /// Interaction logic for SyncWindow.xaml
    /// </summary>
    public partial class SyncWindow : Window
    {
        public SyncWindow(UIDocument uidoc)
        {
            InitializeComponent();
            DataContext = new SyncViewModel(uidoc);
        }
    }
}

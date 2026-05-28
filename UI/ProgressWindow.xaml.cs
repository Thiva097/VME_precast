using System.Windows;

namespace Assembly_VME.Views
{
    public partial class ProgressWindow : Window
    {
        public ProgressWindow()
        {
            InitializeComponent();
        }

        // Call this from background thread safely
        public void UpdateProgress(int current, int total, string assemblyName)
        {
            Dispatcher.Invoke(() =>
            {
                double percent = total > 0 ? (current / (double)total) * 100.0 : 0;

                ExportProgressBar.Value = percent;
                AssemblyNameText.Text = $"Exporting: {assemblyName}";
                ProgressText.Text = $"{current} of {total}  ({(int)percent}%)";
            });
        }

        public void MarkComplete()
        {
            Dispatcher.Invoke(() =>
            {
                ExportProgressBar.Value = 100;
                AssemblyNameText.Text = "Export Complete!";
                ProgressText.Text = "Done";
            });
        }
    }
}
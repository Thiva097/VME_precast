using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.IO;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using WpfVisibility = System.Windows.Visibility;

namespace VME_Apollo
{
    public partial class ConfiguratorWindow : Window
    {
        private ExternalEvent _modelEvent;
        private ModelGenerationHandler _modelHandler;
        private ExternalEvent _drawingEvent;
        private DrawingCreationHandler _drawingHandler;
        private bool isUpdating = false;
        private ObservableCollection<BOQItem> boqItems;

        public ConfiguratorWindow(ExternalEvent modelEvent, ModelGenerationHandler modelHandler, ExternalEvent drawingEvent, DrawingCreationHandler drawingHandler)
        {
            InitializeComponent();
            _modelEvent = modelEvent;
            _modelHandler = modelHandler;
            _drawingEvent = drawingEvent;
            _drawingHandler = drawingHandler;
            
            _modelHandler.OnProcessCompleted = (msg, success) => {
                Dispatcher.Invoke(() => {
                    statusText.Text = msg;
                    if (success) {
                        btnUpdate.IsEnabled = true;
                        btnCreateDrawings.IsEnabled = true;
                        btnExportPDF.IsEnabled = true;
                        btnUpdateBOQ.IsEnabled = true;
                        btnUpdateBBS.IsEnabled = true;
                    }
                });
            };

            InitializeBOQ();
            UpdateSpecVisibility();
        }

        // --- Window Controls ---
        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Close();
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = (this.WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }
        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            OpenHelpPdf("configurator_VME.pdf");
        }

        private void SopButton_Click(object sender, RoutedEventArgs e)
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
                
                // Try Apollo/Assets folder first
                string pdfPath = Path.Combine(assemblyDir, "Apollo", "Assets", fileName);

                if (!File.Exists(pdfPath))
                {
                    // Fallback: Assets folder
                    pdfPath = Path.Combine(assemblyDir, "Assets", fileName);
                }

                if (!File.Exists(pdfPath))
                {
                    // Fallback: Directly in assembly folder
                    pdfPath = Path.Combine(assemblyDir, fileName);
                }

                if (File.Exists(pdfPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pdfPath) { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show(
                        $"Could not find PDF file: {fileName}\n\nSearched in:\n{assemblyDir}\n\nPlease ensure the file is present in the Apollo\\Assets folder.",
                        "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening {fileName}: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ElementType_Click(object sender, RoutedEventArgs e)
        {
            var clicked = sender as ToggleButton;
            if (clicked == null) return;

            rbWaterTank.IsChecked = (clicked == rbWaterTank);
            rbDrain.IsChecked = (clicked == rbDrain);
            rbWall.IsChecked = (clicked == rbWall);

            if (clicked == rbWaterTank) typeTag.Text = "Water Tank";
            else if (clicked == rbDrain) typeTag.Text = "Drain";
            else if (clicked == rbWall) typeTag.Text = "Wall";

            statusText.Text = $"{typeTag.Text} selected — configure specification below";

            UpdateSpecVisibility();
        }

        private void SpecType_Click(object sender, RoutedEventArgs e)
        {
            var clicked = sender as ToggleButton;
            if (clicked == null) return;

            btnMonolithic.IsChecked = (clicked == btnMonolithic);
            btnPT.IsChecked = (clicked == btnPT);
            btnCustomPT.IsChecked = (clicked == btnCustomPT);

            UpdateSpecVisibility();
        }

        private void UpdateSpecVisibility()
        {
            if (panelStandard == null || panelNonStandard == null || sectionSpecification == null) return;

            bool isWaterTank = rbWaterTank.IsChecked == true;
            
            // Toggle Placeholder and Tank-specific panels
            if (panelNotImplemented != null) panelNotImplemented.Visibility = isWaterTank ? WpfVisibility.Collapsed : WpfVisibility.Visible;
            if (panelStandard != null) panelStandard.Visibility = isWaterTank ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            if (panelNonStandard != null) panelNonStandard.Visibility = isWaterTank ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            if (gridTankTypes != null) gridTankTypes.Visibility = isWaterTank ? WpfVisibility.Visible : WpfVisibility.Collapsed;

            // Only show main specification container for Water Tank (or show it with placeholder)
            sectionSpecification.Visibility = WpfVisibility.Visible; // Keep border, but content changes
            
            // If not a water tank, hide results as well and disable actions
            if (!isWaterTank)
            {
                if (sectionBOQ != null) sectionBOQ.Visibility = WpfVisibility.Collapsed;
                if (sectionBBS != null) sectionBBS.Visibility = WpfVisibility.Collapsed;
                btnGenerate.IsEnabled = false;
                btnUpdate.IsEnabled = false;
                btnCreateDrawings.IsEnabled = false;
                btnExportPDF.IsEnabled = false;
                btnUpdateBOQ.IsEnabled = false;
                btnUpdateBBS.IsEnabled = false;
            }
            else
            {
                btnGenerate.IsEnabled = true;
            }

            if (!isWaterTank) return;

            bool isMonolithic = btnMonolithic.IsChecked == true;
            bool isPT = btnPT.IsChecked == true;
            bool isCustom = btnCustomPT.IsChecked == true;

            panelMonolithicCap.Visibility = isMonolithic ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            panelPTCap.Visibility = isPT ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            
            // Toggle visibility and editability of detail panels
            panelNonStandard.Visibility = WpfVisibility.Visible; // Always visible as per latest request
            panelReinforcement.Visibility = WpfVisibility.Collapsed; // Always hidden as requested

            // Set Editability state for Custom mode
            bool readOnly = !isCustom;
            Brush bg = readOnly ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(249, 249, 249)) : Brushes.White;

            txtLength.IsReadOnly = readOnly; txtLength.Background = bg;
            txtWidth.IsReadOnly = readOnly; txtWidth.Background = bg;
            txtHeight.IsReadOnly = readOnly; txtHeight.Background = bg;
            txtWallThickness.IsReadOnly = readOnly; txtWallThickness.Background = bg;
            txtBaseSlabThickness.IsReadOnly = readOnly; txtBaseSlabThickness.Background = bg;
            txtCoverSlabThickness.IsReadOnly = readOnly; txtCoverSlabThickness.Background = bg;

            string typeName = isMonolithic ? "Monolithic Tank" : (isPT ? "PT Tank" : "Custom PT Tank");
            statusText.Text = $"Type: {typeName} — {(isCustom ? "Enter custom parameters" : "Standard configuration selected")}";
            
            // Show capacity only for Custom PT Tank
            if (panelCapacity != null) panelCapacity.Visibility = isCustom ? WpfVisibility.Visible : WpfVisibility.Collapsed;

            UpdateCapacityDisplay();
        }

        private void Capacity_Click(object sender, RoutedEventArgs e)
        {
            var clicked = sender as ToggleButton;
            if (clicked == null) return;

            // Clear other selections in the groups
            if (panelMonolithicCap.Visibility == WpfVisibility.Visible)
            {
                cap6.IsChecked = (clicked == cap6);
                cap105.IsChecked = (clicked == cap105);
                cap125.IsChecked = (clicked == cap125);
            }
            else
            {
                cap25.IsChecked = (clicked == cap25);
                cap30.IsChecked = (clicked == cap30);
                cap45.IsChecked = (clicked == cap45);
                cap50.IsChecked = (clicked == cap50);
            }

            // Set Dimensions based on Image Data
            if (clicked == cap6) { SetDims(2.0, 1.5, 2.0, 0.085, 0.125, 0.15); }
            else if (clicked == cap105) { SetDims(3.0, 1.8, 2.0, 0.1, 0.125, 0.15); }
            else if (clicked == cap125) { SetDims(3.0, 1.8, 2.4, 0.1, 0.125, 0.15); }
            else if (clicked == cap25) { SetDims(3.5, 2.5, 3.0, 0.125, 0.175, 0.15); }
            else if (clicked == cap30) { SetDims(4.0, 2.5, 3.0, 0.125, 0.175, 0.15); }
            else if (clicked == cap45) { SetDims(6.0, 2.5, 3.0, 0.125, 0.15, 0.15); }
            else if (clicked == cap50) { SetDims(6.7, 2.5, 3.0, 0.125, 0.15, 0.15); }

            statusText.Text = $"{clicked.Content} capacity selected — Click Generate Model";
        }

        private void SetDims(double l, double w, double h, double wt, double bt, double ct)
        {
            txtLength.Text = l.ToString("F3");
            txtWidth.Text = w.ToString("F3");
            txtHeight.Text = h.ToString("F3");
            txtWallThickness.Text = wt.ToString("F3");
            txtBaseSlabThickness.Text = bt.ToString("F3");
            txtCoverSlabThickness.Text = ct.ToString("F3");
            
            UpdateCapacityDisplay();
        }

        private void UpdateCapacityDisplay()
        {
            if (lblCapacity == null) return;

            double.TryParse(txtLength.Text, out double l);
            double.TryParse(txtWidth.Text, out double w);
            double.TryParse(txtHeight.Text, out double h);

            // Capacity in KL = Length * Width * Height (assuming meters and internal dimensions)
            double capacity = l * w * h;
            lblCapacity.Text = $"{capacity:F2} KL";
        }

        private void Dimension_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCapacityDisplay();
        }

        private void btnGenerate_Click(object sender, RoutedEventArgs e)
        {
            GenerateOrUpdateModel(false);
        }

        private void btnUpdate_Click(object sender, RoutedEventArgs e)
        {
            GenerateOrUpdateModel(true);
        }

        private void GenerateOrUpdateModel(bool isUpdate)
        {
            // Set basic parameters
            _modelHandler.ElementType = typeTag.Text;
            _modelHandler.Length = double.TryParse(txtLength.Text, out double l) ? l * 1000 : 3000;
            _modelHandler.Width = double.TryParse(txtWidth.Text, out double w) ? w * 1000 : 2000;
            _modelHandler.Height = double.TryParse(txtHeight.Text, out double h) ? h * 1000 : 2000;
            _modelHandler.WallThickness = double.TryParse(txtWallThickness.Text, out double wt) ? wt * 1000 : 100;
            _modelHandler.BaseSlabThickness = double.TryParse(txtBaseSlabThickness.Text, out double bt) ? bt * 1000 : 150;
            _modelHandler.CoverSlabThickness = double.TryParse(txtCoverSlabThickness.Text, out double ct) ? ct * 1000 : 150;

            // Capture reinforcement values (even if panel is hidden, defaults are set)
            CaptureReinforcementValues();

            _modelHandler.IsUpdate = isUpdate;
            _modelEvent.Raise();

            // Update UI state
            btnUpdate.IsEnabled = true;
            btnCreateDrawings.IsEnabled = true;
            btnExportPDF.IsEnabled = true;
            btnUpdateBOQ.IsEnabled = true;
            btnUpdateBBS.IsEnabled = true;

            statusText.Text = isUpdate ? "Model update triggered" : "Model generation triggered";
        }

        private async void btnCreateDrawings_Click(object sender, RoutedEventArgs e)
        {
            _drawingHandler.ExportToPDF = false;
            _drawingHandler.ElementType = typeTag.Text;
            _drawingHandler.Length = double.TryParse(txtLength.Text, out double l) ? l * 1000 : 3000;
            _drawingHandler.Width = double.TryParse(txtWidth.Text, out double w) ? w * 1000 : 2000;
            _drawingHandler.Height = double.TryParse(txtHeight.Text, out double h) ? h * 1000 : 2000;
            _drawingHandler.WallThickness = double.TryParse(txtWallThickness.Text, out double wt) ? wt * 1000 : 100;
            _drawingHandler.BaseSlabThickness = double.TryParse(txtBaseSlabThickness.Text, out double bt) ? bt * 1000 : 150;
            _drawingHandler.CoverSlabThickness = double.TryParse(txtCoverSlabThickness.Text, out double ct) ? ct * 1000 : 150;

            _drawingEvent.Raise();

            previewStatus.Text = "Retrieving Revit Sheet...";
            statusText.Text = "Retrieving drawing from Revit...";

            // Wait a moment for Revit to export the image
            await Task.Delay(2000);
            LoadPreviewImage();
        }

        private void LoadPreviewImage()
        {
            try
            {
                string previewPath = Path.Combine(Path.GetTempPath(), "VME_Apollo_Preview.png");
                if (File.Exists(previewPath))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(previewPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // Crucial to release file handle
                    bitmap.EndInit();

                    imgDrawingPreview.Source = bitmap;
                    imgDrawingPreview.Visibility = WpfVisibility.Visible;
                    previewStatus.Visibility = WpfVisibility.Collapsed;
                    statusText.Text = "Drawing Preview Ready";
                }
            }
            catch { /* Ignore preview loading errors */ }
        }

        private void btnExportPDF_Click(object sender, RoutedEventArgs e)
        {
            _drawingHandler.ExportToPDF = true;
            _drawingHandler.ElementType = typeTag.Text;
            _drawingHandler.Length = double.TryParse(txtLength.Text, out double l) ? l * 1000 : 3000;
            _drawingHandler.Width = double.TryParse(txtWidth.Text, out double w) ? w * 1000 : 2000;
            _drawingHandler.Height = double.TryParse(txtHeight.Text, out double h) ? h * 1000 : 2000;
            _drawingHandler.WallThickness = double.TryParse(txtWallThickness.Text, out double wt) ? wt * 1000 : 100;
            _drawingHandler.BaseSlabThickness = double.TryParse(txtBaseSlabThickness.Text, out double bt) ? bt * 1000 : 150;
            _drawingHandler.CoverSlabThickness = double.TryParse(txtCoverSlabThickness.Text, out double ct) ? ct * 1000 : 150;

            _drawingEvent.Raise();
            statusText.Text = "PDF Export triggered...";
        }

        private void btnUpdateBOQ_Click(object sender, RoutedEventArgs e)
        {
            UpdateBOQQuantities();
            UpdateBBSReport();
            
            // Push values to Revit Model and Schedule
            SyncUIToRevit();

            sectionBOQ.Visibility = WpfVisibility.Visible;
            sectionBBS.Visibility = WpfVisibility.Visible;
            btnUpdateBBS.IsEnabled = true;
            btnExportBOQ.IsEnabled = true;
            btnExportBBS.IsEnabled = true;
            statusText.Text = "BOQ/BBS updated in UI and Revit Schedule";
        }

        private void SyncUIToRevit()
        {
            if (_modelHandler == null) return;

            // Sync dimensions to Revit Handler
            _modelHandler.Length = double.TryParse(txtLength.Text, out double l) ? l * 1000 : 2000;
            _modelHandler.Width = double.TryParse(txtWidth.Text, out double w) ? w * 1000 : 1500;
            _modelHandler.Height = double.TryParse(txtHeight.Text, out double h) ? h * 1000 : 2000;
            _modelHandler.WallThickness = double.TryParse(txtWallThickness.Text, out double wt) ? wt * 1000 : 100;
            _modelHandler.BaseSlabThickness = double.TryParse(txtBaseSlabThickness.Text, out double bt) ? bt * 1000 : 150;
            _modelHandler.CoverSlabThickness = double.TryParse(txtCoverSlabThickness.Text, out double ct) ? ct * 1000 : 150;

            // Sync Reinforcement parameters
            _modelHandler.WallVSpacing = double.TryParse(txtWallVSpacing.Text, out double vSp) ? vSp : 150;
            _modelHandler.WallHSpacing = double.TryParse(txtWallHSpacing.Text, out double hSp) ? hSp : 150;
            _modelHandler.SlabMainSpacing = double.TryParse(txtSlabMainSpacing.Text, out double sSp) ? sSp : 150;

            _modelHandler.IsUpdate = true; // Set to update mode
            _modelEvent.Raise();
        }

        private void btnExportBOQ_Click(object sender, RoutedEventArgs e)
        {
            ExportToCSV("BOQ_Export.csv", dgBOQ);
            statusText.Text = "BOQ exported successfully to Documents folder";
        }

        private void btnExportBBS_Click(object sender, RoutedEventArgs e)
        {
            ExportToCSV("BBS_Export.csv", dgBBS);
            statusText.Text = "BBS exported successfully to Documents folder";
        }

        private void ExportToCSV(string fileName, DataGrid dg)
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);
                using (StreamWriter sw = new StreamWriter(path))
                {
                    // Headers
                    var headers = dg.Columns.Select(c => c.Header.ToString());
                    sw.WriteLine(string.Join(",", headers));

                    // Rows
                    foreach (var item in dg.ItemsSource)
                    {
                        var values = dg.Columns.Select(c => {
                            var binding = (c as DataGridBoundColumn)?.Binding as System.Windows.Data.Binding;
                            if (binding == null) return "";
                            var prop = item.GetType().GetProperty(binding.Path.Path);
                            return prop?.GetValue(item)?.ToString() ?? "";
                        });
                        sw.WriteLine(string.Join(",", values));
                    }
                }
                MessageBox.Show($"Successfully exported to:\n{path}", "VME Apollo");
            }
            catch (Exception ex) { MessageBox.Show("Export failed: " + ex.Message); }
        }

        private void btnUpdateBBS_Click(object sender, RoutedEventArgs e)
        {
            UpdateBBSReport();
            sectionBBS.Visibility = WpfVisibility.Visible;
            statusText.Text = "Full Tank BBS report generated successfully";
        }

        private void UpdateBBSReport()
        {
            ObservableCollection<BBSItem> bbsItems = new ObservableCollection<BBSItem>();
            
            // Dimensions
            double.TryParse(txtLength.Text, out double l); l *= 1000;
            double.TryParse(txtWidth.Text, out double w); w *= 1000;
            double.TryParse(txtHeight.Text, out double h); h *= 1000;
            double.TryParse(txtWallThickness.Text, out double wt); wt *= 1000;
            double.TryParse(txtBaseSlabThickness.Text, out double bt); bt *= 1000;
            double.TryParse(txtCoverSlabThickness.Text, out double ct); ct *= 1000;
            double.TryParse(txtClearCover.Text, out double cc); cc = cc > 0 ? cc : 40;

            // Rebar Parameters from UI
            double vDia = double.Parse((cbWallVBarDia.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "10");
            double.TryParse(txtWallVSpacing.Text, out double vSp); vSp = vSp > 0 ? vSp : 150;
            double hDia = double.Parse((cbWallHBarDia.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "12");
            double.TryParse(txtWallHSpacing.Text, out double hSp); hSp = hSp > 0 ? hSp : 150;
            double sDia = double.Parse((cbSlabMainBarDia.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "12");
            double.TryParse(txtSlabMainSpacing.Text, out double sSp); sSp = sSp > 0 ? sSp : 150;
            double dDia = double.Parse((cbSlabDistBarDia.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "8");
            double.TryParse(txtSlabDistSpacing.Text, out double dSp); dSp = dSp > 0 ? dSp : 150;

            double totalWeight = 0;

            // 1. BASE SLAB REINFORCEMENT (Two layers: Top & Bottom)
            int countB1 = (int)(w / sSp) + 1;
            double lenB1 = l - (2 * cc);
            var itemB1 = new BBSItem { Mark = "B-M1", Diameter = (int)sDia, Shape = "M_00", B = (int)lenB1, BarLength = lenB1, Quantity = countB1 * 2 };
            bbsItems.Add(itemB1);
            
            int countB2 = (int)(l / dSp) + 1;
            double lenB2 = w - (2 * cc);
            var itemB2 = new BBSItem { Mark = "B-D1", Diameter = (int)dDia, Shape = "M_00", B = (int)lenB2, BarLength = lenB2, Quantity = countB2 * 2 };
            bbsItems.Add(itemB2);

            // 2. WALL REINFORCEMENT (4 Walls, Inner & Outer faces)
            double wallH = h - bt - ct;

            // Front/Back Walls (Length)
            int countWFV = (int)(l / vSp) + 1;
            double lenWFV = wallH + 400; // Height + hooks
            // Using Rebar shape 10 for vertical bars with hooks
            var itemWFV = new BBSItem { Mark = "W-V1", Diameter = (int)vDia, Shape = "Rebar shape 10", A = 200, B = (int)wallH, C = 200, D = 0, E = 0, BarLength = lenWFV, Quantity = countWFV * 2 * 2 };
            bbsItems.Add(itemWFV);

            int countWFH = (int)(wallH / hSp) + 1;
            double lenWFH = l - (2 * cc);
            // Using M_17A (L-bar) for horizontal distribution at corners
            var itemWFH = new BBSItem { Mark = "W-H1", Diameter = (int)hDia, Shape = "M_17A", A = 300, B = (int)lenWFH, C = 0, D = 0, E = 0, BarLength = lenWFH + 300, Quantity = countWFH * 2 * 2 };
            bbsItems.Add(itemWFH);

            // Left/Right Walls (Width)
            int countWLV = (int)(w / vSp) + 1;
            var itemWLV = new BBSItem { Mark = "W-V2", Diameter = (int)vDia, Shape = "Rebar shape 10", A = 200, B = (int)wallH, C = 200, D = 0, E = 0, BarLength = lenWFV, Quantity = countWLV * 2 * 2 };
            bbsItems.Add(itemWLV);

            double lenWLH = w - (2 * cc);
            var itemWLH = new BBSItem { Mark = "W-H2", Diameter = (int)hDia, Shape = "M_17A", A = 300, B = (int)lenWLH, C = 0, D = 0, E = 0, BarLength = lenWLH + 300, Quantity = countWFH * 2 * 2 };
            bbsItems.Add(itemWLH);

            // 3. COVER SLAB REINFORCEMENT
            if (ct > 0)
            {
                var itemC1 = new BBSItem { Mark = "C-M1", Diameter = (int)sDia, Shape = "M_00", A = 0, B = (int)lenB1, C = 0, D = 0, E = 0, BarLength = lenB1, Quantity = countB1 * 2 };
                bbsItems.Add(itemC1);
                var itemC2 = new BBSItem { Mark = "C-D1", Diameter = (int)dDia, Shape = "M_00", A = 0, B = (int)lenB2, C = 0, D = 0, E = 0, BarLength = lenB2, Quantity = countB2 * 2 };
                bbsItems.Add(itemC2);
            }

            // 4. CORNER LINKS / STIRRUPS
            // Using Rebar shape 1 for stirrups/links
            var itemL1 = new BBSItem { Mark = "L1", Diameter = 8, Shape = "Rebar shape 1", A = 100, B = 150, C = 100, D = 150, E = 50, BarLength = 550, Quantity = countWFH * 4 * 2 };
            bbsItems.Add(itemL1);

            foreach (var item in bbsItems) totalWeight += item.Weight;

            dgBBS.ItemsSource = bbsItems;
            txtTotalRebarWeight.Text = $"{totalWeight:F2} kg";
        }

        private void InitializeBOQ()
        {
            // ... (existing code remains same)
            boqItems = new ObservableCollection<BOQItem>
            {
                new BOQItem { Description = "A — Concrete Works", IsHeader = true },
                new BOQItem { Description = "  Base slab M30 concrete", Unit = "m³", Rate = 0 },
                new BOQItem { Description = "  Wall concrete M30", Unit = "m³", Rate = 0 },
                new BOQItem { Description = "  Cover slab M30 concrete", Unit = "m³", Rate = 0 },
                new BOQItem { Description = "B — Formwork", IsHeader = true },
                new BOQItem { Description = "  Base slab soffit formwork", Unit = "m²", Rate = 0 },
                new BOQItem { Description = "  Wall inner & outer formwork", Unit = "m²", Rate = 0 },
                new BOQItem { Description = "  Cover slab formwork", Unit = "m²", Rate = 0 },
                new BOQItem { Description = "C — Reinforcement", IsHeader = true },
                new BOQItem { Description = "  Fe500 TMT bars (walls)", Unit = "kg", Rate = 0 },
                new BOQItem { Description = "  Fe500 TMT bars (slabs)", Unit = "kg", Rate = 0 }
            };
            dgBOQ.ItemsSource = boqItems;
        }

        private void UpdateBOQQuantities()
        {
            if (boqItems == null) return;

            // Dimensions are already set in textboxes (meters)
            double.TryParse(txtLength.Text, out double l);
            double.TryParse(txtWidth.Text, out double w);
            double.TryParse(txtHeight.Text, out double h);
            double.TryParse(txtWallThickness.Text, out double wt);
            double.TryParse(txtBaseSlabThickness.Text, out double bt);
            double.TryParse(txtCoverSlabThickness.Text, out double ct);

            // Rebar info (spacing in mm, convert to m)
            double smd = double.Parse((cbSlabMainBarDia.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "8");
            double.TryParse(txtSlabMainSpacing.Text, out double sms); sms /= 1000.0;
            double sdd = double.Parse((cbSlabDistBarDia.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "8");
            double.TryParse(txtSlabDistSpacing.Text, out double sds); sds /= 1000.0;
            double wvd = double.Parse((cbWallVBarDia.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "8");
            double.TryParse(txtWallVSpacing.Text, out double wvs); wvs /= 1000.0;

            double wallH = h - bt - ct;

            // Get Rates from Settings
            double.TryParse(txtRateConcrete.Text, out double rConcrete);
            double.TryParse(txtRateFormwork.Text, out double rFormwork);
            double.TryParse(txtRateRebar.Text, out double rRebar);

            // A — Concrete (m³)
            boqItems[1].Qty = l * w * bt; boqItems[1].Rate = rConcrete;
            boqItems[2].Qty = (l * w - (l - 2*wt) * (w - 2*wt)) * wallH; boqItems[2].Rate = rConcrete;
            boqItems[3].Qty = l * w * ct; boqItems[3].Rate = rConcrete;

            // B — Formwork (m²)
            boqItems[5].Qty = l * w; boqItems[5].Rate = rFormwork;
            boqItems[6].Qty = 2 * (2 * (l + w)) * wallH; boqItems[6].Rate = rFormwork;
            boqItems[7].Qty = l * w; boqItems[7].Rate = rFormwork;

            // C — Reinforcement (kg) using formula: w = D² / 162.2 × L
            double wallWeight = 0;
            if (wvs > 0) wallWeight = (Math.Pow(wvd, 2) / 162.2) * wallH * ((2*(l+w)) / wvs);

            double slabWeight = 0;
            if (sms > 0) slabWeight += (Math.Pow(smd, 2) / 162.2) * l * (w / sms);
            if (sds > 0) slabWeight += (Math.Pow(sdd, 2) / 162.2) * w * (l / sds);

            boqItems[9].Qty  = wallWeight; boqItems[9].Rate = rRebar;
            boqItems[10].Qty = slabWeight; boqItems[10].Rate = rRebar;

            CalculateTotals();
        }

        private void btnShowRateSettings_Click(object sender, RoutedEventArgs e)
        {
            if (panelRateSettings == null) return;
            panelRateSettings.Visibility = panelRateSettings.Visibility == WpfVisibility.Visible ? WpfVisibility.Collapsed : WpfVisibility.Visible;
        }

        private void dgBOQ_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Trigger calculation after edit
            Dispatcher.BeginInvoke(new Action(() => CalculateTotals()), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void CalculateTotals()
        {
            double total = 0;
            foreach (var item in boqItems)
            {
                if (!item.IsHeader)
                {
                    item.Amount = item.Qty * item.Rate;
                    total += item.Amount;
                }
            }
            txtTotalCost.Text = $"₹ {total:N0}";
        }

        private void CaptureReinforcementValues()
        {
            // Wall Reinforcement
            _modelHandler.WallHBarDia = double.TryParse(cbWallHBarDia.Text, out double whd) ? whd : 12;
            _modelHandler.WallHSpacing = double.TryParse(txtWallHSpacing.Text, out double whs) ? whs : 150;
            _modelHandler.WallVBarDia = double.TryParse(cbWallVBarDia.Text, out double wvd) ? wvd : 10;
            _modelHandler.WallVSpacing = double.TryParse(txtWallVSpacing.Text, out double wvs) ? wvs : 200;

            // Slab Reinforcement
            _modelHandler.SlabMainBarDia = double.TryParse(cbSlabMainBarDia.Text, out double smd) ? smd : 12;
            _modelHandler.SlabMainSpacing = double.TryParse(txtSlabMainSpacing.Text, out double sms) ? sms : 150;
            _modelHandler.SlabDistBarDia = double.TryParse(cbSlabDistBarDia.Text, out double sdd) ? sdd : 8;
            _modelHandler.SlabDistSpacing = double.TryParse(txtSlabDistSpacing.Text, out double sds) ? sds : 200;

            _modelHandler.ClearCover = double.TryParse(txtClearCover.Text, out double cc) ? cc : 40;
        }

        private void btnNext_Click(object sender, RoutedEventArgs e)
        {
        }
    }

    public class BOQItem : INotifyPropertyChanged
    {
        private double _qty;
        private double _rate;
        private double _amount;

        public string Description { get; set; }
        public double Qty 
        { 
            get => _qty; 
            set { _qty = value; OnPropertyChanged(); OnPropertyChanged(nameof(Amount)); } 
        }
        public string Unit { get; set; }
        public double Rate 
        { 
            get => _rate; 
            set { _rate = value; OnPropertyChanged(); OnPropertyChanged(nameof(Amount)); } 
        }
        public double Amount 
        { 
            get => Qty * Rate; 
            set { _amount = value; OnPropertyChanged(); } 
        }
        public bool IsHeader { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class BBSItem : INotifyPropertyChanged
    {
        public string Mark { get; set; }
        public int Diameter { get; set; }
        public string Shape { get; set; }
        public int A { get; set; }
        public int B { get; set; }
        public int C { get; set; }
        public int D { get; set; }
        public int E { get; set; }
        public double BarLength { get; set; }
        public int Quantity { get; set; }
        public double Weight => (Math.Pow(Diameter, 2) / 162.2) * (BarLength / 1000.0) * Quantity;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

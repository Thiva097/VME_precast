using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Structure;
using Assembly_VME.Helpers;

namespace Assembly_VME.ViewModels
{
    public class SyncViewModel : INotifyPropertyChanged
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private AssemblyItem _selectedAssembly;
        private string _statusText = "Select an Assembly to generate, view, and export its Sheet.";
        private ElementId _lastCreatedSheetId;
        private bool _isBatchExporting = false;

        public ObservableCollection<AssemblyItem> Assemblies { get; set; } = new ObservableCollection<AssemblyItem>();
        
        // High-fidelity collections bound to DataGrids
        public ObservableCollection<BbsItem> CurrentBbs { get; set; } = new ObservableCollection<BbsItem>();
        public ObservableCollection<MaterialSummaryItem> CurrentMaterialSummary { get; set; } = new ObservableCollection<MaterialSummaryItem>();
        public ObservableCollection<WallItem> CurrentConcrete { get; set; } = new ObservableCollection<WallItem>();
        public ObservableCollection<HookItem> CurrentHooks { get; set; } = new ObservableCollection<HookItem>();

        public AssemblyItem SelectedAssembly
        {
            get => _selectedAssembly;
            set
            {
                if (_selectedAssembly != value)
                {
                    _selectedAssembly = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsAssemblySelected));
                    UpdateElementsList();
                    
                    if (_selectedAssembly != null)
                    {
                        // 1. Silent sync parameters to update Mark & Panel_Name
                        SilentSyncSelectedAssembly();
                        
                        // 2. Programmatically generate/retrieve the sheet and schedules and show them in Revit!
                        CreateAndShowAssemblySheet();
                    }
                }
            }
        }

        public bool IsAssemblySelected => SelectedAssembly != null;

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public ICommand ExportPdfCommand { get; }
        public ICommand SyncSelectedCommand { get; }
        public ICommand SyncAllCommand { get; }
        public ICommand BatchExportCommand { get; }

        public SyncViewModel(UIDocument uidoc)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;

            // Automatically verify or programmatically create and bind 'Panel_Name' and 'Weight_Kg' if they are missing!
            ParameterHelper.EnsureSharedParametersExist(_doc);

            ExportPdfCommand = new RelayCommand(ExecuteExportPdf, () => true);
            SyncSelectedCommand = new RelayCommand(ExecuteSyncSelected, () => SelectedAssembly != null);
            SyncAllCommand = new RelayCommand(ExecuteSyncAll, () => Assemblies.Count > 0);
            BatchExportCommand = new RelayCommand(ExecuteBatchExport, () => Assemblies.Count > 0);

            LoadAssemblies();
        }

        private void LoadAssemblies()
        {
            Assemblies.Clear();

            // 1. Optimize: Collect and group all Rebars (both standard Rebars and RebarsInSystem) by Host ID
            Dictionary<ElementId, List<Element>> rebarsByHost = new Dictionary<ElementId, List<Element>>();
            try
            {
                FilteredElementCollector rebarCollector = new FilteredElementCollector(_doc);
                ICollection<Element> allRebars = rebarCollector.OfCategory(BuiltInCategory.OST_Rebar).ToElements();
                foreach (Element elem in allRebars)
                {
                    ElementId hostId = null;
                    if (elem is Rebar rebar)
                    {
                        hostId = rebar.GetHostId();
                    }
                    else if (elem is RebarInSystem ris)
                    {
                        hostId = ris.GetHostId();
                    }

                    if (hostId == null || hostId == ElementId.InvalidElementId) continue;

                    if (!rebarsByHost.ContainsKey(hostId))
                    {
                        rebarsByHost[hostId] = new List<Element>();
                    }
                    rebarsByHost[hostId].Add(elem);
                }
            }
            catch {}

            // 2. Optimize: Collect and group all Generic Models by Host ID
            Dictionary<ElementId, List<FamilyInstance>> gmsByHost = new Dictionary<ElementId, List<FamilyInstance>>();
            try
            {
                FilteredElementCollector gmCollector = new FilteredElementCollector(_doc);
                ICollection<Element> allGms = gmCollector.OfCategory(BuiltInCategory.OST_GenericModel).OfClass(typeof(FamilyInstance)).ToElements();
                foreach (Element elem in allGms)
                {
                    FamilyInstance fi = elem as FamilyInstance;
                    if (fi == null || fi.Host == null) continue;

                    ElementId hostId = fi.Host.Id;
                    if (hostId == null || hostId == ElementId.InvalidElementId) continue;

                    if (!gmsByHost.ContainsKey(hostId))
                    {
                        gmsByHost[hostId] = new List<FamilyInstance>();
                    }
                    gmsByHost[hostId].Add(fi);
                }
            }
            catch {}

            // 3. Collect all assembly instances in the Revit document
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            ICollection<Element> assemblyInstances = collector.OfClass(typeof(AssemblyInstance)).ToElements();

            List<AssemblyItem> tempAssemblies = new List<AssemblyItem>();

            foreach (Element elem in assemblyInstances)
            {
                AssemblyInstance ai = elem as AssemblyInstance;
                if (ai == null) continue;

                AssemblyItem item = new AssemblyItem
                {
                    Name = ai.Name,
                    Instance = ai
                };

                // Get members of the assembly
                ICollection<ElementId> memberIds = ai.GetMemberIds();
                foreach (ElementId memberId in memberIds)
                {
                    Element member = _doc.GetElement(memberId);
                    if (member == null) continue;

                    // Check direct categories of the members (including both Rebar and RebarInSystem)
                    if (member is Wall)
                    {
                        if (!item.WallIds.Contains(memberId)) item.WallIds.Add(memberId);
                    }
                    else if (member is Rebar || member is RebarInSystem || (member.Category != null && member.Category.Id.Value == (long)BuiltInCategory.OST_Rebar))
                    {
                        if (!item.RebarIds.Contains(memberId)) item.RebarIds.Add(memberId);
                    }
                    else if (member.Category != null && member.Category.Id.Value == (long)BuiltInCategory.OST_GenericModel)
                    {
                        if (!item.GenericModelIds.Contains(memberId)) item.GenericModelIds.Add(memberId);
                    }

                    // Retrieve hosted Rebars from dictionary cache
                    if (rebarsByHost.TryGetValue(memberId, out List<Element> hostedRebars))
                    {
                        foreach (Element rebar in hostedRebars)
                        {
                            if (!item.RebarIds.Contains(rebar.Id))
                            {
                                item.RebarIds.Add(rebar.Id);
                            }
                        }
                    }

                    // Retrieve hosted Generic Models from dictionary cache
                    if (gmsByHost.TryGetValue(memberId, out List<FamilyInstance> hostedGms))
                    {
                        foreach (FamilyInstance gm in hostedGms)
                        {
                            if (!item.GenericModelIds.Contains(gm.Id))
                            {
                                item.GenericModelIds.Add(gm.Id);
                            }
                        }
                    }
                }

                // Show any assembly that has at least one of our target element categories!
                if (item.WallCount > 0 || item.RebarCount > 0 || item.GenericModelCount > 0)
                {
                    // Compute High-Fidelity quantities for this assembly
                    ComputeQuantities(item);
                    tempAssemblies.Add(item);
                }
            }

            // Natural Sorting to arrange assemblies in correct numeric-alphabetical order (e.g. PW-GF-32 before PW-GF-34)
            tempAssemblies.Sort((a, b) => CompareNatural(a.Name, b.Name));

            HashSet<string> addedNames = new HashSet<string>();
            foreach (var item in tempAssemblies)
            {
                if (addedNames.Add(item.Name))
                {
                    Assemblies.Add(item);
                }
            }

            StatusText = $"Loaded {Assemblies.Count} assemblies from model.";
        }

        private static int CompareNatural(string x, string y)
        {
            if (x == y) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int len1 = x.Length;
            int len2 = y.Length;
            int i = 0, j = 0;

            while (i < len1 && j < len2)
            {
                char c1 = x[i];
                char c2 = y[j];

                if (char.IsDigit(c1) && char.IsDigit(c2))
                {
                    // Parse numeric parts
                    string num1 = "";
                    while (i < len1 && char.IsDigit(x[i]))
                    {
                        num1 += x[i++];
                    }

                    string num2 = "";
                    while (j < len2 && char.IsDigit(y[j]))
                    {
                        num2 += y[j++];
                    }

                    if (long.TryParse(num1, out long val1) && long.TryParse(num2, out long val2))
                    {
                        int comp = val1.CompareTo(val2);
                        if (comp != 0) return comp;
                    }
                    else
                    {
                        int comp = string.Compare(num1, num2, StringComparison.Ordinal);
                        if (comp != 0) return comp;
                    }
                }
                else
                {
                    int comp = c1.CompareTo(c2);
                    if (comp != 0) return comp;
                    i++;
                    j++;
                }
            }

            return len1.CompareTo(len2);
        }

        private void ComputeQuantities(AssemblyItem item)
        {
            // Dictionary to store grouped rebar data by diameter: Key = Diameter (double), Value = (TotalLengthMm, TotalWeightKg)
            Dictionary<double, (double Length, double Weight)> summaryGroups = new Dictionary<double, (double Length, double Weight)>();

            // Process Rebars (BBS Row Generation)
            foreach (ElementId id in item.RebarIds)
            {
                Element elem = _doc.GetElement(id);
                if (elem == null) continue;

                RebarWeightInfo weightInfo = RebarHelper.CalculateWeight(elem);
                if (weightInfo.SingleLengthMm <= 0 && weightInfo.Quantity <= 0)
                {
                    continue;
                }

                int qty = weightInfo.Quantity;
                double diameterMm = weightInfo.DiameterMm;
                double singleLengthMm = weightInfo.SingleLengthMm;
                double totalLengthMm = weightInfo.TotalLengthMm;
                double weightKg = weightInfo.TotalWeightKg;

                ElementId shapeId = ElementId.InvalidElementId;
                Parameter shapeParam = elem.get_Parameter(BuiltInParameter.REBAR_SHAPE);
                if (shapeParam != null)
                {
                    shapeId = shapeParam.AsElementId();
                }

                string mark = item.Name;
                string size = $"{Math.Round(diameterMm)} mm";

                // Shape Code query via GetShapeId()
                string shape = "00";
                if (shapeId != ElementId.InvalidElementId)
                {
                    RebarShape rebarShape = _doc.GetElement(shapeId) as RebarShape;
                    if (rebarShape != null)
                    {
                        shape = rebarShape.Name;
                        if (shape.StartsWith("M_", StringComparison.OrdinalIgnoreCase))
                        {
                            shape = shape.Substring(2);
                        }
                    }
                }

                // Extract shape dimensions A, B, C, D, E
                string dimA = GetRebarShapeParameterValue(elem, "A");
                string dimB = GetRebarShapeParameterValue(elem, "B");
                string dimC = GetRebarShapeParameterValue(elem, "C");
                string dimD = GetRebarShapeParameterValue(elem, "D");
                string dimE = GetRebarShapeParameterValue(elem, "E");

                // Get Type Name
                string typeName = "-";
                if (elem.GetTypeId() != ElementId.InvalidElementId)
                {
                    Element typeElem = _doc.GetElement(elem.GetTypeId());
                    if (typeElem != null)
                    {
                        typeName = typeElem.Name;
                    }
                }

                item.RebarSchedules.Add(new BbsItem
                {
                    Mark = mark,
                    Type = typeName,
                    Size = size,
                    Shape = shape,
                    Quantity = qty.ToString(),
                    BarLength = $"{Math.Round(singleLengthMm)} mm",
                    TotalLength = $"{Math.Round(totalLengthMm)} mm",
                    Weight = $"{weightKg:F2} kg",
                    DimA = dimA,
                    DimB = dimB,
                    DimC = dimC,
                    DimD = dimD,
                    DimE = dimE
                });

                // Group by rounded diameter for summary
                double roundedDia = Math.Round(diameterMm);
                if (!summaryGroups.ContainsKey(roundedDia))
                {
                    summaryGroups[roundedDia] = (0.0, 0.0);
                }
                var currentGroup = summaryGroups[roundedDia];
                summaryGroups[roundedDia] = (currentGroup.Length + totalLengthMm, currentGroup.Weight + weightKg);
            }

            // Populate Material Summaries with Grouped data
            double grandTotalLength = 0.0;
            double grandTotalWeight = 0.0;

            var sortedGroups = new List<double>(summaryGroups.Keys);
            sortedGroups.Sort();

            foreach (double dia in sortedGroups)
            {
                var data = summaryGroups[dia];
                grandTotalLength += data.Length;
                grandTotalWeight += data.Weight;

                item.MaterialSummaries.Add(new MaterialSummaryItem
                {
                    BarDiameter = $"{dia} mm",
                    TotalLength = $"{Math.Round(data.Length)} mm",
                    Weight = $"{data.Weight:F2} kg"
                });
            }

            // Add Total row
            if (sortedGroups.Count > 0)
            {
                item.MaterialSummaries.Add(new MaterialSummaryItem
                {
                    BarDiameter = "Total",
                    TotalLength = $"{Math.Round(grandTotalLength)} mm",
                    Weight = $"{grandTotalWeight:F2} kg"
                });
            }

            if (item.GenericModelCount > 0)
            {
                item.MaterialSummaries.Add(new MaterialSummaryItem
                {
                    BarDiameter = "Generic Module",
                    TotalLength = "-",
                    Weight = $"{item.GenericModelCount} Nos"
                });
            }

            // 3. Populate Wall Schedules (Concrete Tab)
            item.WallSchedules.Clear();
            foreach (ElementId wallId in item.WallIds)
            {
                Element wall = _doc.GetElement(wallId);
                if (wall == null) continue;

                double volCf = 0;
                Parameter volParam = wall.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                if (volParam != null && volParam.HasValue) volCf = volParam.AsDouble();
                double weightKg = volCf * 0.0283168 * 2500; // CF to M3 then density

                item.WallSchedules.Add(new WallItem
                {
                    Mark = item.Name,
                    Length = ParameterHelper.GetBuiltInParameterValue(wall, BuiltInParameter.CURVE_ELEM_LENGTH, true),
                    Height = ParameterHelper.GetBuiltInParameterValue(wall, BuiltInParameter.WALL_USER_HEIGHT_PARAM, true),
                    Area = ParameterHelper.GetBuiltInParameterValue(wall, BuiltInParameter.HOST_AREA_COMPUTED, true),
                    Volume = ParameterHelper.GetBuiltInParameterValue(wall, BuiltInParameter.HOST_VOLUME_COMPUTED, true),
                    Weight = $"{weightKg:F2} kg"
                });
            }

            // 4. Populate Hook Schedules (Hooks Tab - Generic Models)
            item.HookSchedules.Clear();
            Dictionary<string, (string Type, int Count)> gmGroups = new Dictionary<string, (string, int)>();
            foreach (ElementId gmId in item.GenericModelIds)
            {
                Element gm = _doc.GetElement(gmId);
                if (gm == null) continue;

                // User requested: show the assembly name in Mark for Generic Module
                string mark = item.Name; 
                string typeName = "-";
                if (gm.GetTypeId() != ElementId.InvalidElementId)
                    typeName = _doc.GetElement(gm.GetTypeId())?.Name ?? "-";

                string key = $"{mark}_{typeName}";
                if (gmGroups.ContainsKey(key))
                {
                    var existing = gmGroups[key];
                    gmGroups[key] = (existing.Type, existing.Count + 1);
                }
                else
                {
                    gmGroups[key] = (typeName, 1);
                }
            }

            foreach (var key in gmGroups.Keys)
            {
                var data = gmGroups[key];
                item.HookSchedules.Add(new HookItem
                {
                    Mark = key.Split('_')[0],
                    Type = data.Type,
                    Count = data.Count.ToString()
                });
            }
        }


        private static string GetRebarShapeParameterValue(Element elem, string paramName)
        {
            if (elem == null) return "-";
            Parameter param = elem.LookupParameter(paramName);
            if (param == null) param = elem.LookupParameter(paramName.ToLower());
            if (param != null)
            {
                if (param.StorageType == StorageType.Double)
                {
                    double valMm = param.AsDouble() * 304.8;
                    return $"{Math.Round(valMm)} mm";
                }
                else if (param.StorageType == StorageType.String)
                {
                    return param.AsString();
                }
                else if (param.StorageType == StorageType.Integer)
                {
                    return param.AsInteger().ToString();
                }
            }
            return "-";
        }

        private void UpdateElementsList()
        {
            CurrentBbs.Clear();
            CurrentMaterialSummary.Clear();
            CurrentConcrete.Clear();
            CurrentHooks.Clear();

            if (SelectedAssembly == null) return;

            // Load BBS
            foreach (var bbs in SelectedAssembly.RebarSchedules)
            {
                CurrentBbs.Add(bbs);
            }

            // Load Material Summary
            foreach (var mat in SelectedAssembly.MaterialSummaries)
            {
                CurrentMaterialSummary.Add(mat);
            }

            // Load Concrete Data
            foreach (var wall in SelectedAssembly.WallSchedules)
            {
                CurrentConcrete.Add(wall);
            }

            // Load Hooks Data
            foreach (var hook in SelectedAssembly.HookSchedules)
            {
                CurrentHooks.Add(hook);
            }
        }

        private void SilentSyncSelectedAssembly()
        {
            if (SelectedAssembly == null || SelectedAssembly.Instance == null) return;

            try
            {
                using (Transaction trans = new Transaction(_doc, "Silent Parameter Sync"))
                {
                    trans.Start();

                    // Suppress duplicate mark warning popups and other Revit warnings dialogs!
                    FailureHandlingOptions failOptions = trans.GetFailureHandlingOptions();
                    failOptions.SetFailuresPreprocessor(new HideDuplicateMarkWarning());
                    trans.SetFailureHandlingOptions(failOptions);

                    // Add hosted rebars as direct members of the assembly so Revit's assembly schedule includes them!
                    AssemblyInstance ai = SelectedAssembly.Instance as AssemblyInstance;
                    if (ai != null)
                    {
                        List<ElementId> toAdd = new List<ElementId>();
                        foreach (ElementId rid in SelectedAssembly.RebarIds)
                        {
                            if (!ai.GetMemberIds().Contains(rid))
                            {
                                toAdd.Add(rid);
                            }
                        }
                        if (toAdd.Count > 0)
                        {
                            try { ai.AddMemberIds(toAdd); } catch {}
                        }
                    }

                    int w = 0, r = 0, g = 0, f = 0;
                    SyncAssemblyItem(SelectedAssembly, ref w, ref r, ref g, ref f);
                    trans.Commit();
                }
            }
            catch {}
        }

        private void CreateAndShowAssemblySheet()
        {
            if (SelectedAssembly == null || SelectedAssembly.Instance == null) return;

            string assemblyName = SelectedAssembly.Name;
            ElementId assemblyId = SelectedAssembly.Instance.Id;

            try
            {
                using (Transaction trans = new Transaction(_doc, "Create Assembly Sheet & Schedules"))
                {
                    trans.Start();

                    // Ensure Weight_Kg exists and is schedulable before building schedule columns.
                    ParameterHelper.EnsureSharedParametersExist(_doc);
                    try { _doc.Regenerate(); } catch { }

                    // Suppress warnings/dialogs
                    FailureHandlingOptions failOptions = trans.GetFailureHandlingOptions();
                    failOptions.SetFailuresPreprocessor(new HideDuplicateMarkWarning());
                    trans.SetFailureHandlingOptions(failOptions);

                    // 1. Find or load a Titleblock Symbol
                    FilteredElementCollector titleBlockCollector = new FilteredElementCollector(_doc)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .OfClass(typeof(FamilySymbol));
                    FamilySymbol titleBlock = titleBlockCollector.FirstElement() as FamilySymbol;
                    ElementId titleBlockId = titleBlock != null ? titleBlock.Id : ElementId.InvalidElementId;

                    // 2. Always generate a fresh sheet. (User requested NOT to delete old ones).
                    ViewSheet sheet = AssemblyViewUtils.CreateSheet(_doc, assemblyId, titleBlockId);

                    if (sheet != null)
                    {
                        try { sheet.Name = GetUniqueSheetName(_doc, $"Assembly Sheet - {assemblyName}"); } catch { }
                        try { sheet.SheetNumber = GetNextAvailableSheetNumber(_doc); } catch { }
                    }

                    // 2.5 Do NOT delete old schedules (User requested to keep them).
                    // DeleteOldAssemblySchedules(_doc, assemblyId, assemblyName);


                    // 3. Create fresh new schedules (don't touch existing ones)
                    // Rebar Schedule
                    ViewSchedule rebarSchedule = AssemblyViewUtils.CreateSingleCategorySchedule(_doc, assemblyId, new ElementId(BuiltInCategory.OST_Rebar));
                    AssignUniqueScheduleName(rebarSchedule, $"Rebar Schedule - {assemblyName}");
                    ConfigureBbsSchedule(_doc, rebarSchedule);

                    // 4. Material Takeoff / Summary Schedule
                    ViewSchedule materialSchedule = AssemblyViewUtils.CreateSingleCategorySchedule(_doc, assemblyId, new ElementId(BuiltInCategory.OST_Rebar));
                    AssignUniqueScheduleName(materialSchedule, $"Material Takeoff - {assemblyName}");
                    ConfigureSummarySchedule(_doc, materialSchedule);

                    SheetLayoutHelper.SheetLayout layout = SheetLayoutHelper.GetLayout(_doc, sheet);
                    double summaryTableWidth = layout.DrawableWidth * 0.42;
                    double bbsTableWidth = layout.DrawableWidth * 0.96;

                    ScheduleHelper.ConfigureScheduleSheetAppearance(materialSchedule, summaryTableWidth);
                    ScheduleHelper.ConfigureScheduleSheetAppearance(rebarSchedule, bbsTableWidth);

                    try { _doc.Regenerate(); } catch { }

                    XYZ summaryPoint = SheetLayoutHelper.GetSummaryPlacementPoint(layout);
                    ScheduleSheetInstance.Create(_doc, sheet.Id, materialSchedule.Id, summaryPoint);

                    // Append generic-module row directly under the summary schedule (last table row).
                    if (SelectedAssembly.GenericModelCount > 0)
                    {
                        _doc.Regenerate();
                        double summaryHeight = ScheduleHelper.GetScheduleHeightOnSheet(materialSchedule);
                        XYZ footerPoint = SheetLayoutHelper.GetFooterPlacementPoint(layout, summaryPoint, summaryHeight);

                        ViewSchedule footerSchedule = ScheduleHelper.CreateGenericModuleFooterSchedule(
                            _doc, assemblyId, SelectedAssembly.GenericModelCount, assemblyName);

                        if (footerSchedule != null)
                        {
                            ScheduleHelper.FitScheduleColumnsToWidth(footerSchedule.Definition, summaryTableWidth);
                            ScheduleSheetInstance.Create(_doc, sheet.Id, footerSchedule.Id, footerPoint);
                        }
                    }

                    XYZ rebarPoint = SheetLayoutHelper.GetBbsPlacementPoint(layout);
                    ScheduleSheetInstance.Create(_doc, sheet.Id, rebarSchedule.Id, rebarPoint);

                    try { _doc.Regenerate(); } catch { }

                    // 6. Find or create the assembly 3D orthographic view and place it on the sheet.
                    try
                    {
                        View3D assembly3dView = AssemblyViewHelper.GetOrCreateAssembly3DOrthoView(_doc, assemblyId, assemblyName);
                        if (assembly3dView != null)
                        {
                            XYZ viewCenter = SheetLayoutHelper.Get3DViewCenter(layout);
                            bool placed = AssemblyViewHelper.Place3DOrthoViewOnSheet(
                                _doc, sheet, assembly3dView, viewCenter);
                            if (!placed)
                            {
                                StatusText = $"Warning: 3D ortho view created but could not be placed on sheet for '{assemblyName}'.";
                            }
                        }
                        else
                        {
                            StatusText = $"Warning: Could not create 3D ortho view for assembly '{assemblyName}'.";
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusText = "Note: 3D view placement failed: " + ex.Message;
                    }



                    trans.Commit();

                    // 7. Track the newly created sheet for PDF export (Safely!)
                    if (sheet != null && sheet.IsValidObject)
                    {
                        _lastCreatedSheetId = sheet.Id;

                        // 8. Make the Sheet the active view in Revit screen! (Only if not batch exporting)
                        if (!_isBatchExporting)
                        {
                            try { _uidoc.ActiveView = sheet; } catch { }
                            StatusText = $"Active Revit Sheet set to: {sheet.SheetNumber} ({sheet.Name})";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Error generating assembly sheet: {ex.Message}";
                MessageBox.Show($"Failed to generate sheet. Please report this error:\n\n{ex.ToString()}", "Sheet Generation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void ConfigureBbsSchedule(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;

            // Clear any default/existing fields to start fresh!
            IList<ScheduleFieldId> currentFieldIds = def.GetFieldOrder();
            for (int i = currentFieldIds.Count - 1; i >= 0; i--)
            {
                ScheduleField field = def.GetField(currentFieldIds[i]);
                string heading = field.ColumnHeading ?? "";
                if (heading.IndexOf("Weight", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue; // Skip removal for Weight columns (likely user's manual calculated value)
                }
                try { def.RemoveField(currentFieldIds[i]); } catch { }
            }

            // 1. Add fields safely (only add ONE of the duplicate fields to prevent squishing)
            if (AddScheduleFieldByName(doc, def, "Bar Diameter") == null)
            {
                AddScheduleFieldByName(doc, def, "Size");
            }
            AddScheduleFieldByName(doc, def, "Shape");
            
            AddScheduleFieldByName(doc, def, "A");
            AddScheduleFieldByName(doc, def, "B");
            AddScheduleFieldByName(doc, def, "C");
            AddScheduleFieldByName(doc, def, "D");
            AddScheduleFieldByName(doc, def, "E");

            if (AddScheduleFieldByName(doc, def, "Bar Length") == null)
            {
                AddScheduleFieldByName(doc, def, "Length");
            }
            
            if (AddScheduleFieldByName(doc, def, "Total Length") == null)
            {
                AddScheduleFieldByName(doc, def, "Total Bar Length");
            }
            
            AddScheduleFieldByName(doc, def, "Quantity");

            ScheduleField weightField = ScheduleHelper.AddWeightColumn(doc, def, "Weight");

            def.IsItemized = true;

            try { def.ClearSortGroupFields(); } catch { }

            ScheduleField diameterField = FindFieldByHeading(def, "Bar Diameter");
            if (diameterField == null) diameterField = FindFieldByHeading(def, "Size");

            ScheduleField lengthField = FindFieldByHeading(def, "Bar Length");
            if (lengthField == null) lengthField = FindFieldByHeading(def, "Length");

            ScheduleField shapeField = FindFieldByHeading(def, "Shape");
            ScheduleField qtyField = FindFieldByHeading(def, "Quantity");

            // Group by diameter, length, shape, and quantity so Weight_Kg does not show "<varies>".
            ScheduleHelper.AddSortGroupField(def, diameterField);
            ScheduleHelper.AddSortGroupField(def, lengthField);
            ScheduleHelper.AddSortGroupField(def, shapeField);
            ScheduleHelper.AddSortGroupField(def, qtyField);

            foreach (ScheduleFieldId fieldId in def.GetFieldOrder())
            {
                ScheduleField field = def.GetField(fieldId);
                string colHeader = field.ColumnHeading ?? "";
                bool isTotalable = colHeader.IndexOf("Quantity", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   colHeader.IndexOf("Length", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   colHeader.IndexOf("Weight", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isTotalable)
                {
                    ScheduleHelper.ConfigureNumericTotalField(field);
                }
            }

            ScheduleHelper.EnableGrandTotals(def, "Weight", "Weight (kg)");
            ScheduleHelper.ApplyFieldAlignment(def);
        }

        private static void ConfigureSummarySchedule(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;

            // Clear any default/existing fields to start fresh!
            IList<ScheduleFieldId> currentSummaryFieldIds = def.GetFieldOrder();
            for (int i = currentSummaryFieldIds.Count - 1; i >= 0; i--)
            {
                ScheduleField field = def.GetField(currentSummaryFieldIds[i]);
                string heading = field.ColumnHeading ?? "";
                if (heading.IndexOf("Weight", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue; // Preserve the user's manual calculated weight field
                }
                try { def.RemoveField(currentSummaryFieldIds[i]); } catch { }
            }

            // 1. Add fields safely (only add ONE of the duplicate fields to prevent squishing)
            if (AddScheduleFieldByName(doc, def, "Bar Diameter") == null)
            {
                AddScheduleFieldByName(doc, def, "Size");
            }
            
            if (AddScheduleFieldByName(doc, def, "Total Length") == null)
            {
                AddScheduleFieldByName(doc, def, "Total Bar Length");
            }
            
            ScheduleField summaryWeightField = ScheduleHelper.AddWeightColumn(doc, def, "Weight");

            def.IsItemized = false;

            try { def.ClearSortGroupFields(); } catch { }

            ScheduleField diameterField = FindFieldByHeading(def, "Bar Diameter");
            if (diameterField == null) diameterField = FindFieldByHeading(def, "Size");

            ScheduleHelper.AddSortGroupField(def, diameterField);

            foreach (ScheduleFieldId fieldId in def.GetFieldOrder())
            {
                ScheduleField field = def.GetField(fieldId);
                string colHeader = field.ColumnHeading ?? "";
                if (colHeader.Contains("Length"))
                {
                    ScheduleHelper.ConfigureNumericTotalField(field);
                }
            }

            if (summaryWeightField != null)
            {
                ScheduleHelper.ConfigureNumericTotalField(summaryWeightField);
            }

            ScheduleHelper.EnableGrandTotals(def, "Weight", "Weight (kg)");
            ScheduleHelper.ApplyFieldAlignment(def);
        }


        private static ScheduleField AddScheduleFieldByName(Document doc, ScheduleDefinition def, string name, string heading = null)
        {
            return ScheduleHelper.AddFieldByName(doc, def, name, heading);
        }

        /// Adds the first schedulable field whose name contains the keyword (case-insensitive)
        private static ScheduleField AddScheduleFieldContaining(Document doc, ScheduleDefinition def, string keyword, string heading)
        {
            return ScheduleHelper.AddFieldContaining(doc, def, keyword, heading);
        }

        private static ScheduleField FindFieldByHeading(ScheduleDefinition def, string heading)
        {
            foreach (ScheduleFieldId fieldId in def.GetFieldOrder())
            {
                ScheduleField field = def.GetField(fieldId);
                if (field.ColumnHeading != null && field.ColumnHeading.Equals(heading, StringComparison.OrdinalIgnoreCase))
                {
                    return field;
                }
            }
            return null;
        }

        private void ExecuteExportPdf()
        {
            if (SelectedAssembly == null)
            {
                MessageBox.Show("please select any assemby", "Export PDF", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Force a fresh sync and sheet generation before export to ensure "Always Create New Sheet" logic applies.
            SilentSyncSelectedAssembly();
            CreateAndShowAssemblySheet();

            string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            ExportToPdfInternal(SelectedAssembly, docsPath, true);
        }

        private void ExecuteBatchExport()
        {
            if (Assemblies.Count == 0) return;

            _isBatchExporting = true;
            try
            {
                string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string folderName = $"Assembly_BBS_Exports_{timestamp}";
                string targetFolder = System.IO.Path.Combine(docsPath, folderName);

                if (!System.IO.Directory.Exists(targetFolder))
                {
                    System.IO.Directory.CreateDirectory(targetFolder);
                }

                int successCount = 0;
                int failureCount = 0;

                // Save current selection to restore later
                var previousSelection = SelectedAssembly;

                foreach (var item in Assemblies)
                {
                    try
                    {
                        // 1. Set as selected to trigger sync and sheet generation
                        SelectedAssembly = item;
                        
                        // 2. Export to PDF in the new folder
                        bool success = ExportToPdfInternal(item, targetFolder, false);
                        if (success) successCount++;
                        else failureCount++;
                    }
                    catch
                    {
                        failureCount++;
                    }
                }

                // Restore previous selection
                SelectedAssembly = previousSelection;

                StatusText = $"Batch Export Completed: {successCount} successful, {failureCount} failed. Folder: {targetFolder}";

                MessageBox.Show(
                    $"Batch PDF Export Completed!\n\n" +
                    $"- Total Assemblies: {Assemblies.Count}\n" +
                    $"- Successfully Exported: {successCount}\n" +
                    $"- Failures: {failureCount}\n\n" +
                    $"Location: {targetFolder}",
                    "Batch Export Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Open the folder
                try { System.Diagnostics.Process.Start("explorer.exe", targetFolder); } catch { }
            }
            catch (Exception ex)
            {
                StatusText = $"Batch Export failed: {ex.Message}";
                MessageBox.Show($"Batch Export failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isBatchExporting = false;
            }
        }

        private bool ExportToPdfInternal(AssemblyItem item, string targetFolder, bool showMessage)
        {
            if (item == null || item.Instance == null) return false;

            string assemblyName = item.Name;
            ElementId assemblyId = item.Instance.Id;

            // Find the last created sheet (the one with our schedules)
            ViewSheet sheet = null;
            if (_lastCreatedSheetId != null && _lastCreatedSheetId != ElementId.InvalidElementId)
            {
                sheet = _doc.GetElement(_lastCreatedSheetId) as ViewSheet;
                // Double check if this sheet belongs to the item
                if (sheet != null && sheet.AssociatedAssemblyInstanceId != assemblyId)
                {
                    sheet = null;
                }
            }

            if (sheet == null)
            {
                // Fallback: find any assembly sheet
                sheet = FindExistingAssemblySheet(_doc, assemblyId, assemblyName);
            }

            if (sheet == null)
            {
                if (showMessage)
                {
                    MessageBox.Show($"No sheet found for assembly '{assemblyName}'. Please generate its sheet before exporting.", "Export PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return false;
            }

            try
            {
                string fileName = $"BBS_Sheet_{assemblyName}.pdf";

                // Setup export options (Native Revit PDF Engine)
                PDFExportOptions options = new PDFExportOptions
                {
                    FileName = fileName,
                    Combine = true,
                    PaperFormat = ExportPaperFormat.Default
                };

                List<ElementId> viewIds = new List<ElementId> { sheet.Id };

                // Native Export runs directly outside transactions
                _doc.Export(targetFolder, viewIds, options);

                string fullPath = System.IO.Path.Combine(targetFolder, fileName);
                StatusText = $"Successfully exported sheet to PDF: {fullPath}";
                
                if (showMessage)
                {
                    MessageBox.Show(
                        $"PDF Export Completed Successfully!\n\n" +
                        $"File: {fileName}\n" +
                        $"Location: {targetFolder}",
                        "Export PDF Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return true;
            }
            catch (Exception ex)
            {
                StatusText = $"PDF Export failed for '{assemblyName}': {ex.Message}";
                if (showMessage)
                {
                    MessageBox.Show($"PDF Export failed for '{assemblyName}':\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return false;
            }
        }

        private void ExecuteSyncSelected()
        {
            if (SelectedAssembly == null || SelectedAssembly.Instance == null) return;

            try
            {
                using (Transaction trans = new Transaction(_doc, "Sync Selected Assembly"))
                {
                    trans.Start();

                    FailureHandlingOptions failOptions = trans.GetFailureHandlingOptions();
                    failOptions.SetFailuresPreprocessor(new HideDuplicateMarkWarning());
                    trans.SetFailureHandlingOptions(failOptions);

                    AssemblyInstance ai = SelectedAssembly.Instance as AssemblyInstance;
                    if (ai != null)
                    {
                        List<ElementId> toAdd = new List<ElementId>();
                        foreach (ElementId rid in SelectedAssembly.RebarIds)
                        {
                            if (!ai.GetMemberIds().Contains(rid))
                            {
                                toAdd.Add(rid);
                            }
                        }
                        if (toAdd.Count > 0)
                        {
                            try { ai.AddMemberIds(toAdd); } catch {}
                        }
                    }

                    int w = 0, r = 0, g = 0, f = 0;
                    SyncAssemblyItem(SelectedAssembly, ref w, ref r, ref g, ref f);
                    trans.Commit();

                    StatusText = $"Successfully synced Selected Assembly '{SelectedAssembly.Name}': {w} Walls, {r} Rebars, {g} Generic Models updated.";
                    MessageBox.Show(
                        $"Sync Selected Assembly Completed Successfully!\n\n" +
                        $"- Assembly: {SelectedAssembly.Name}\n" +
                        $"- Walls Updated: {w}\n" +
                        $"- Rebars Updated: {r}\n" +
                        $"- Generic Models Updated: {g}\n" +
                        $"- Failures/Skipped: {f}",
                        "Sync Selected Assembly",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Sync Selected failed: {ex.Message}";
                MessageBox.Show($"Sync Selected Assembly failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteSyncAll()
        {
            if (Assemblies.Count == 0) return;

            try
            {
                using (Transaction trans = new Transaction(_doc, "Sync All Assemblies"))
                {
                    trans.Start();

                    FailureHandlingOptions failOptions = trans.GetFailureHandlingOptions();
                    failOptions.SetFailuresPreprocessor(new HideDuplicateMarkWarning());
                    trans.SetFailureHandlingOptions(failOptions);

                    int totalWalls = 0;
                    int totalRebars = 0;
                    int totalGenericModels = 0;
                    int totalFailures = 0;

                    foreach (var item in Assemblies)
                    {
                        AssemblyInstance ai = item.Instance as AssemblyInstance;
                        if (ai != null)
                        {
                            List<ElementId> toAdd = new List<ElementId>();
                            foreach (ElementId rid in item.RebarIds)
                            {
                                if (!ai.GetMemberIds().Contains(rid))
                                {
                                    toAdd.Add(rid);
                                }
                            }
                            if (toAdd.Count > 0)
                            {
                                try { ai.AddMemberIds(toAdd); } catch {}
                            }
                        }

                        SyncAssemblyItem(item, ref totalWalls, ref totalRebars, ref totalGenericModels, ref totalFailures);
                    }

                    trans.Commit();

                    StatusText = $"Successfully synced all assemblies: {totalWalls} Walls, {totalRebars} Rebars, {totalGenericModels} Generic Models updated.";
                    MessageBox.Show(
                        $"Sync All Assemblies Completed Successfully!\n\n" +
                        $"- Total Assemblies Processed: {Assemblies.Count}\n" +
                        $"- Total Walls Updated: {totalWalls}\n" +
                        $"- Total Rebars Updated: {totalRebars}\n" +
                        $"- Total Generic Models Updated: {totalGenericModels}\n" +
                        $"- Total Failures/Skipped: {totalFailures}",
                        "Sync All Assemblies",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Sync All failed: {ex.Message}";
                MessageBox.Show($"Sync All Assemblies failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static ViewSheet FindExistingAssemblySheet(Document doc, ElementId assemblyId, string assemblyName)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet));
                
            foreach (Element elem in collector)
            {
                ViewSheet vs = elem as ViewSheet;
                if (vs != null)
                {
                    if (vs.AssociatedAssemblyInstanceId == assemblyId) return vs;
                    if (vs.Name != null && vs.Name.IndexOf(assemblyName, StringComparison.OrdinalIgnoreCase) >= 0) return vs;
                }
            }
            return null;
        }

        private static string GetNextAvailableSheetNumber(Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet));
            
            HashSet<string> numbers = new HashSet<string>();
            foreach (Element elem in collector)
            {
                ViewSheet vs = elem as ViewSheet;
                if (vs != null) numbers.Add(vs.SheetNumber);
            }

            int count = 100;
            while (true)
            {
                string num = $"A-{count}";
                if (!numbers.Contains(num)) return num;
                count++;
            }
        }

        private static string GetUniqueSheetName(Document doc, string baseName)
        {
            var existingNames = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Select(v => v.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existingNames.Contains(baseName)) return baseName;

            int counter = 2;
            while (true)
            {
                string candidate = $"{baseName} ({counter})";
                if (!existingNames.Contains(candidate)) return candidate;
                counter++;
            }
        }

        private static void AssignUniqueScheduleName(ViewSchedule schedule, string baseName)
        {
            try
            {
                schedule.Name = baseName;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Name already exists, append a suffix until it's unique
                int counter = 2;
                while (true)
                {
                    try
                    {
                        schedule.Name = $"{baseName} ({counter})";
                        break;
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        counter++;
                    }
                }
            }
        }

        private static void DeleteOldAssemblySchedules(Document doc, ElementId assemblyId, string assemblyName)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule));

            List<ElementId> toDelete = new List<ElementId>();
            foreach (Element elem in collector)
            {
                ViewSchedule vs = elem as ViewSchedule;
                if (vs == null) continue;

                // Check if belongs to assembly
                bool isAssemblyView = vs.AssociatedAssemblyInstanceId == assemblyId;

                // Also check by name as fallback for non-associated schedules that we created
                bool nameMatches = !string.IsNullOrEmpty(assemblyName) &&
                                   vs.Name.IndexOf(assemblyName, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                   (vs.Name.IndexOf("Rebar Schedule", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    vs.Name.IndexOf("Material Takeoff", StringComparison.OrdinalIgnoreCase) >= 0);

                if (isAssemblyView || nameMatches)
                {
                    toDelete.Add(vs.Id);
                }
            }

            foreach (ElementId id in toDelete)
            {
                try { doc.Delete(id); } catch { }
            }
        }

        private void SyncAssemblyItem(AssemblyItem item, ref int wallsUpdated, ref int rebarsUpdated, ref int genericModelsUpdated, ref int failures)
        {
            string assemblyName = item.Name;

            // Sync Walls
            foreach (ElementId wallId in item.WallIds)
            {
                Element wall = _doc.GetElement(wallId);
                if (wall == null) continue;
                bool wallParamSet = ParameterHelper.SetParameterByName(wall, "Panel_Name", assemblyName);
                bool wallMarkSet = ParameterHelper.SetBuiltInParameter(wall, BuiltInParameter.ALL_MODEL_MARK, assemblyName);
                
                if (wallParamSet || wallMarkSet) wallsUpdated++;
                else failures++;
            }

            // Sync Rebars
            foreach (ElementId rebarId in item.RebarIds)
            {
                Element elem = _doc.GetElement(rebarId);
                if (elem == null) continue;
                bool rebarParamSet = ParameterHelper.SetParameterByName(elem, "Panel_Name", assemblyName);
                bool rebarMarkSet = ParameterHelper.SetBuiltInParameter(elem, BuiltInParameter.ALL_MODEL_MARK, assemblyName);
                
                try
                {
                    RebarWeightInfo weightInfo = RebarHelper.CalculateWeight(elem);
                    RebarHelper.SetWeightParameters(elem, weightInfo);
                }
                catch { }

                if (rebarParamSet || rebarMarkSet) rebarsUpdated++;
                else failures++;
            }

            // Sync Generic Models
            string genericCountText = $"{item.GenericModelCount} Nos";
            foreach (ElementId gmId in item.GenericModelIds)
            {
                Element gm = _doc.GetElement(gmId);
                if (gm == null) continue;
                bool gmParamSet = ParameterHelper.SetParameterByName(gm, "Panel_Name", assemblyName);
                bool gmMarkSet = ParameterHelper.SetBuiltInParameter(gm, BuiltInParameter.ALL_MODEL_MARK, assemblyName);

                ParameterHelper.SetParameterByName(gm, "Summary_Footer_Label", "Generic Module");
                ParameterHelper.SetParameterByName(gm, "Summary_Footer_Length", "-");
                ParameterHelper.SetParameterByName(gm, "Summary_Footer_Weight", genericCountText);

                if (gmParamSet || gmMarkSet) genericModelsUpdated++;
                else failures++;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class HideDuplicateMarkWarning : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failureMessages = failuresAccessor.GetFailureMessages();
            foreach (FailureMessageAccessor f in failureMessages)
            {
                if (f.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(f);
                }
                else if (f.GetSeverity() == FailureSeverity.Error)
                {
                    if (failuresAccessor.IsFailureResolutionPermitted(f))
                    {
                        failuresAccessor.ResolveFailure(f);
                    }
                }
            }
            return FailureProcessingResult.Continue;
        }
    }
}

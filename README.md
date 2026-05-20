# VME Precast - Revit Assembly Export Tool

A powerful Revit Add-in for managing assembly schedules, quantities, and PDF exports.

## Features
- **Tabbed Dashboard**: Manage Steel (Rebar), Concrete (Walls), and Hooks (Embeds) in one view.
- **Itemized BBS**: Synchronized rebar bending schedules that match UI previews exactly.
- **Automated Calculations**: Dynamic weight calculation using `Volume * 2500 kg/m³`.
- **Batch Export**: Export entire assembly sets to PDF with unique, non-overlapping sheets.
- **Parameter Synchronization**: Automatic syncing of `Panel_Name` and `Mark` across all assembly components.

## Technical Details
- Built for Revit using C# / .NET.
- Uses `Autodesk.Revit.DB` and `Autodesk.Revit.UI`.
- Modern WPF interface with premium styling.

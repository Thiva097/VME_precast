using System;
using Autodesk.Revit.UI;

namespace Assembly_VME.Helpers
{
    /// <summary>
    /// A generic IExternalEventHandler that runs any stored Action inside the Revit API context.
    /// Use this to safely start transactions from WPF UI events (buttons, combo-box changes, etc.).
    /// </summary>
    public class RevitActionHandler : IExternalEventHandler
    {
        private Action<UIApplication> _pendingAction;

        /// <summary>
        /// Store the action to run. Call ExternalEvent.Raise() immediately after.
        /// </summary>
        public void SetAction(Action<UIApplication> action)
        {
            _pendingAction = action;
        }

        // Called by Revit on its own API thread when the ExternalEvent fires.
        public void Execute(UIApplication app)
        {
            var action = _pendingAction;
            _pendingAction = null;

            try
            {
                action?.Invoke(app);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    "An error occurred in a Revit operation:\n\n" + ex.Message,
                    "VME Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        public string GetName() => "VME Revit Action Handler";
    }
}

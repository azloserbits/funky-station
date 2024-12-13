using Content.Client.UserInterface.Systems.Alerts.Controls;
using Content.Shared.Alert;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;

namespace Content.Client.UserInterface.Systems.Alerts.Widgets;

/// <summary>
///     The status effects display on the right side of the screen.
/// </summary>
[GenerateTypedNameReferences]
public sealed partial class AlertsUI : UIWidget
{
    // also known as Control.Children?
    private readonly Dictionary<AlertKey, AlertControl> _alertControls = new();

    public AlertsUI()
    {
        RobustXamlLoader.Load(this);
        LayoutContainer.SetGrowHorizontal(this, LayoutContainer.GrowDirection.Begin);
    }

    public void SyncControls(AlertsSystem alertsSystem,
        AlertOrderPrototype? alertOrderPrototype,
        IReadOnlyDictionary<AlertKey,
        AlertState> alertStates)
    {
        // remove any controls with keys no longer present
        if (SyncRemoveControls(alertStates))
            return;

        // now we know that alertControls contains alerts that should still exist but
        // may need to updated,
        // also there may be some new alerts we need to show.
        // further, we need to ensure they are ordered w.r.t their configured order
        SyncUpdateControls(alertsSystem, alertOrderPrototype, alertStates);
    }

    public void ClearAllControls()
    {
        foreach (var alertControl in _alertControls.Values)
        {
            alertControl.OnPressed -= AlertControlPressed;
            alertControl.Dispose();
        }

        _alertControls.Clear();
    }

    public event EventHandler<ProtoId<AlertPrototype>>? AlertPressed;

    private bool SyncRemoveControls(IReadOnlyDictionary<AlertKey, AlertState> alertStates)
    {
        var toRemove = new List<AlertKey>();
        foreach (var existingKey in _alertControls.Keys)
        {
            if (!alertStates.ContainsKey(existingKey))
                toRemove.Add(existingKey);
        }

        foreach (var alertKeyToRemove in toRemove)
        {
            _alertControls.Remove(alertKeyToRemove, out var control);
            if (control == null)
                return true;
            AlertContainer.Children.Remove(control);
        }

        return false;
    }

    private void SyncUpdateControls(AlertsSystem alertsSystem, AlertOrderPrototype? alertOrderPrototype,
        IReadOnlyDictionary<AlertKey, AlertState> alertStates)
    {
        foreach (var (alertKey, alertState) in alertStates)
        {
            if (!alertKey.AlertType.HasValue)
            {
                Logger.WarningS("alert", "found alertkey without alerttype," +
                                         " alert keys should never be stored without an alerttype set: {0}", alertKey);
                continue;
            }

            var alertType = alertKey.AlertType.Value;
            if (!alertsSystem.TryGet(alertType, out var newAlert))
            {
                Logger.ErrorS("alert", "Unrecognized alertType {0}", alertType);
                continue;
            }

            if (_alertControls.TryGetValue(newAlert.AlertKey, out var existingAlertControl) &&
                existingAlertControl.Alert.ID == newAlert.ID)
            {
                // key is the same, simply update the existing control severity / cooldown
                existingAlertControl.SetSeverity(alertState.Severity);
                if (alertState.ShowCooldown)
                    existingAlertControl.Cooldown = alertState.Cooldown;
                existingAlertControl.DynamicMessage = alertState.DynamicMessage;
            }
            else
            {
                if (existingAlertControl != null)
                    AlertContainer.Children.Remove(existingAlertControl);

                // this is a new alert + alert key or just a different alert with the same
                // key, create the control and add it in the appropriate order
                var newAlertControl = CreateAlertControl(newAlert, alertState);

                //TODO: Can the presenter sort the states before giving it to us?
                if (alertOrderPrototype != null)
                {
                    var added = false;
                    foreach (var alertControl in AlertContainer.Children)
                    {
                        if (alertOrderPrototype.Compare(newAlert, ((AlertControl) alertControl).Alert) >= 0)
                            continue;

                        var idx = alertControl.GetPositionInParent();
                        AlertContainer.Children.Add(newAlertControl);
                        newAlertControl.SetPositionInParent(idx);
                        added = true;
                        break;
                    }

                    if (!added)
                        AlertContainer.Children.Add(newAlertControl);
                }
                else
                {
                    AlertContainer.Children.Add(newAlertControl);
                }

                _alertControls[newAlert.AlertKey] = newAlertControl;
            }
        }
    }

    private AlertControl CreateAlertControl(AlertPrototype alert, AlertState alertState)
    {
        (TimeSpan, TimeSpan)? cooldown = null;
        if (alertState.ShowCooldown)
            cooldown = alertState.Cooldown;

        string? dynamicMessage = alertState.DynamicMessage;

        var alertControl = new AlertControl(alert, alertState.Severity)
        {
            Cooldown = cooldown,
            DynamicMessage = dynamicMessage,
        };
        alertControl.OnPressed += AlertControlPressed;
        return alertControl;
    }

    private void AlertControlPressed(BaseButton.ButtonEventArgs args)
    {
        if (args.Button is not AlertControl control)
            return;

        if (args.Event.Function != EngineKeyFunctions.UIClick)
            return;

        AlertPressed?.Invoke(this, control.Alert.ID);
    }
}

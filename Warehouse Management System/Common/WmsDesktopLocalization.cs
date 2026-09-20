using System.Globalization;
using System.Resources;
using System.Windows.Forms;
using Wms.Application.Localization;

namespace Wms.WinForms.Common;

public static class WmsDesktopLocalization
{
    private static readonly ResourceManager Resources =
        new(typeof(WmsSharedResource));

    private static readonly Dictionary<string, string> TextKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["📦 Warehouse Management System"] = "Desktop.WarehouseSystem",
            ["Dashboard"] = "Dashboard.Title",
            ["Inventory Management"] = "Inventory.Title",
            ["Location Management"] = "Locations.Title",
            ["Item Management"] = "Items.Title",
            ["Receiving"] = "Nav.Receiving",
            ["Putaway"] = "Nav.Putaway",
            ["Picking"] = "Nav.Picking",
            ["Reports"] = "Reports.Title",
            ["Management"] = "Nav.Management",
            ["Operations"] = "Nav.Operations",
            ["Search"] = "Common.Search",
            ["Refresh (F5)"] = "Desktop.RefreshHotkey",
            ["Search (Enter)"] = "Desktop.SearchHotkey",
            ["Add (F1)"] = "Desktop.AddHotkey",
            ["Edit (F2)"] = "Desktop.EditHotkey",
            ["Delete (Del)"] = "Desktop.DeleteHotkey",
            ["Save (F1)"] = "Desktop.SaveHotkey",
            ["Clear (F2)"] = "Desktop.ClearHotkey",
            ["Cancel"] = "Common.Cancel",
            ["Save"] = "Common.Save",
            ["Ready"] = "Desktop.Ready",
            ["Loading..."] = "Desktop.Loading",
            ["Searching..."] = "Desktop.Searching",
            ["Error loading data"] = "Desktop.DataError",
            ["Last Refreshed: --"] = "Desktop.LastRefreshed",
            ["Total Items"] = "Dashboard.TotalItems",
            ["SKUs with Stock"] = "Desktop.SkusWithStock",
            ["Total Stock Qty"] = "Desktop.TotalStockQty",
            ["Active Locations"] = "Desktop.ActiveLocations",
            ["Low Stock Alerts"] = "Desktop.LowStockAlerts",
            ["Recent Movements"] = "Desktop.RecentMovements",
            ["SKU"] = "Field.ItemSku",
            ["Item"] = "Desktop.Item",
            ["Item Name"] = "Desktop.ItemName",
            ["Location"] = "Field.Location",
            ["Location Name"] = "Desktop.LocationName",
            ["Full Path"] = "Desktop.FullPath",
            ["Available"] = "Field.Available",
            ["Reserved"] = "Field.Reserved",
            ["Net Available"] = "Field.NetAvailable",
            ["Total Qty"] = "Desktop.TotalQty",
            ["Locations"] = "Desktop.Locations",
            ["Qty"] = "Desktop.Qty",
            ["Type"] = "Movement.Type",
            ["Time"] = "Movement.Time",
            ["User"] = "Movement.User",
            ["Code"] = "Field.Code",
            ["Name"] = "Field.Name",
            ["Capacity"] = "Field.Capacity",
            ["Pickable"] = "Field.Pickable",
            ["Receivable"] = "Field.Receivable",
            ["Active"] = "Common.Active",
            ["Delete Location"] = "Common.Delete",
            ["Confirm Delete"] = "Desktop.ConfirmDelete",
            ["Are you sure you want to delete this location?"] = "Desktop.DeleteLocationPrompt"
        };

    public static string Get(string key, params object[] arguments)
    {
        var value = Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        return arguments.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, arguments);
    }

    public static void SetCulture(string? locale)
    {
        var normalized = WmsLocaleCatalog.Normalize(locale);
        var culture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static void Apply(Form form)
    {
        var isRtl = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;
        form.RightToLeft = isRtl ? RightToLeft.Yes : RightToLeft.No;
        form.RightToLeftLayout = isRtl;
        ApplyControl(form, isRtl);
    }

    private static void ApplyControl(Control control, bool isRtl)
    {
        control.RightToLeft = isRtl ? RightToLeft.Yes : RightToLeft.No;
        if (TextKeys.TryGetValue(control.Text.Trim(), out var key))
        {
            control.Text = Get(key);
        }

        if (control is TextBox textBox && TextKeys.TryGetValue(textBox.PlaceholderText.Trim(), out var placeholderKey))
        {
            textBox.PlaceholderText = Get(placeholderKey);
        }

        if (control is DataGridView grid)
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (TextKeys.TryGetValue(column.HeaderText.Trim(), out var columnKey))
                {
                    column.HeaderText = Get(columnKey);
                }
            }
        }

        foreach (Control child in control.Controls)
        {
            ApplyControl(child, isRtl);
        }
    }
}

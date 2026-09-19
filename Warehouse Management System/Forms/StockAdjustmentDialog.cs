// Wms.WinForms/Forms/StockAdjustmentDialog.cs

using System.Globalization;
using Wms.WinForms.Common;

namespace Wms.WinForms.Forms;

public partial class StockAdjustmentDialog : Form
{
    private readonly decimal _currentQuantity;

    private readonly string _itemSku;
    private readonly string _locationCode;

    public StockAdjustmentDialog(string itemSku, string locationCode, string currentQuantity)
    {
        _itemSku = itemSku;
        _locationCode = locationCode;
        if (!decimal.TryParse(currentQuantity, NumberStyles.Number, CultureInfo.CurrentCulture, out _currentQuantity))
        {
            _currentQuantity = 0m;
        }

        InitializeComponent();
        SetupEventHandlers();
        SetupForm();
    }

    public decimal NewQuantity { get; private set; }
    public string Reason { get; private set; } = "";

    private void SetupEventHandlers()
    {
        btnOK.Click += BtnOK_Click;
        btnCancel.Click += BtnCancel_Click;
        txtNewQuantity.KeyPress += TxtNewQuantity_KeyPress;
        KeyDown += StockAdjustmentDialog_KeyDown;
    }

    private void SetupForm()
    {
        ModernUIHelper.StyleForm(this);
        KeyPreview = true;

        // Apply modern styling
        ModernUIHelper.StyleModernTextBox(txtNewQuantity);
        ModernUIHelper.StyleModernTextBox(txtReason);
        ModernUIHelper.StyleSuccessButton(btnOK);
        ModernUIHelper.StyleSecondaryButton(btnCancel);

        // Populate initial values
        lblItemInfo.Text = $"Item: {_itemSku}\nLocation: {_locationCode}\nCurrent Quantity: {_currentQuantity:N2}";
        txtNewQuantity.Text = _currentQuantity.ToString("N2", CultureInfo.CurrentCulture);

        txtNewQuantity.Focus();
        txtNewQuantity.SelectAll();
    }

    private void TxtNewQuantity_KeyPress(object? sender, KeyPressEventArgs e)
    {
        // Allow only numbers and decimal point
        if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.')
        {
            e.Handled = true;
            return;
        }

        // Only allow one decimal point
        if (e.KeyChar == '.' && ((TextBox)sender!).Text.Contains('.'))
        {
            e.Handled = true;
            return;
        }

        if (e.KeyChar == (char)Keys.Enter)
        {
            e.Handled = true;
            txtReason.Focus();
        }
    }

    private void BtnOK_Click(object? sender, EventArgs e)
    {
        if (!ValidateInput()) return;

        NewQuantity = decimal.Parse(txtNewQuantity.Text, CultureInfo.CurrentCulture);
        Reason = txtReason.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void StockAdjustmentDialog_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.F1:
                btnOK.PerformClick();
                break;
            case Keys.Escape:
                btnCancel.PerformClick();
                break;
        }
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(txtNewQuantity.Text) || !decimal.TryParse(txtNewQuantity.Text, out var quantity))
        {
            ModernUIHelper.ShowModernError("Please enter a valid quantity");
            txtNewQuantity.Focus();
            return false;
        }

        if (quantity < 0)
        {
            ModernUIHelper.ShowModernError("Quantity cannot be negative");
            txtNewQuantity.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(txtReason.Text))
        {
            ModernUIHelper.ShowModernError("Please enter a reason for the adjustment");
            txtReason.Focus();
            return false;
        }

        return true;
    }
}

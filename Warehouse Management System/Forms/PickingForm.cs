// Wms.WinForms/Forms/PickingForm.cs

using System.Globalization;
using Microsoft.Extensions.Logging;
using Wms.Application.Identity;
using Wms.Application.UseCases.Items;
using Wms.Application.UseCases.Picking;
using Wms.WinForms.Common;

namespace Wms.WinForms.Forms;

public partial class PickingForm : Form
{
    private readonly ICurrentUser _currentUser;
    private readonly IGetItemsUseCase _getItemsUseCase;
    private readonly ILogger<PickingForm> _logger;
    private readonly IPickOrderUseCase _pickOrderUseCase;

    public PickingForm(
        IPickOrderUseCase pickOrderUseCase,
        IGetItemsUseCase getItemsUseCase,
        ICurrentUser currentUser,
        ILogger<PickingForm> logger)
    {
        _pickOrderUseCase = pickOrderUseCase;
        _getItemsUseCase = getItemsUseCase;
        _currentUser = currentUser;
        _logger = logger;
        InitializeComponent();
        SetupEventHandlers();
        SetupForm();
    }

    private void SetupEventHandlers()
    {
        txtBarcode.KeyPress += TxtBarcode_KeyPress;
        txtQuantity.KeyPress += TxtQuantity_KeyPress;
        txtFromLocation.KeyPress += TxtFromLocation_KeyPress;
        btnPick.Click += BtnPick_Click;
        btnClear.Click += BtnClear_Click;
        KeyDown += PickingForm_KeyDown;
    }

    private void SetupForm()
    {
        ModernUIHelper.StyleForm(this);
        KeyPreview = true;

        // Apply modern styling
        ModernUIHelper.StyleModernTextBox(txtBarcode);
        ModernUIHelper.StyleModernTextBox(txtQuantity);
        ModernUIHelper.StyleModernTextBox(txtFromLocation);
        ModernUIHelper.StyleModernTextBox(txtOrderNumber);
        ModernUIHelper.StyleModernTextBox(txtNotes);

        ModernUIHelper.StyleDangerButton(btnPick);
        ModernUIHelper.StyleSecondaryButton(btnClear);

        txtBarcode.Focus();
    }

    private async void TxtBarcode_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (e.KeyChar == (char)Keys.Enter)
        {
            e.Handled = true;
            await ProcessBarcodeAsync();
        }
    }

    private void TxtQuantity_KeyPress(object? sender, KeyPressEventArgs e)
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
            txtFromLocation.Focus();
        }
    }

    private void TxtFromLocation_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (e.KeyChar == (char)Keys.Enter)
        {
            e.Handled = true;
            btnPick.PerformClick();
        }
    }

    private async Task ProcessBarcodeAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtBarcode.Text))
            {
                ModernUIHelper.ShowModernError("Please scan or enter a barcode");
                return;
            }

            SetBusy(true);

            var result = await _getItemsUseCase.GetByBarcodeAsync(txtBarcode.Text.Trim());

            if (result.IsFailure)
            {
                ModernUIHelper.ShowModernError($"Item not found: {result.Error}");
                txtBarcode.SelectAll();
                return;
            }

            // Populate item information
            var item = result.Value;
            lblItemInfo.Text = $"SKU: {item.Sku}\nName: {item.Name}\nUOM: {item.UnitOfMeasure}";
            lblItemInfo.ForeColor = ModernUIHelper.Colors.Success;

            // Set focus to quantity
            txtQuantity.Focus();
            txtQuantity.SelectAll();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing barcode {Barcode}", txtBarcode.Text);
            ModernUIHelper.ShowModernError($"Error processing barcode: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BtnPick_Click(object? sender, EventArgs e)
    {
        try
        {
            if (!ValidateInput())
                return;

            SetBusy(true);

            var request = new PickItemDto(
                ExtractSkuFromLabel(),
                txtFromLocation.Text.Trim(),
                decimal.Parse(txtQuantity.Text, CultureInfo.CurrentCulture),
                txtOrderNumber.Text.Trim(),
                null, // LotNumber
                null, // SerialNumber
                txtNotes.Text.Trim()
            );

            var result = await _pickOrderUseCase.ExecuteAsync(request, _currentUser.RequireUserId());

            if (result.IsFailure)
            {
                ModernUIHelper.ShowModernError(result.Error);
                return;
            }

            ModernUIHelper.ShowModernSuccess($"Item picked successfully!\nMovement ID: {result.Value.MovementId}");

            ClearForm();
            txtBarcode.Focus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error picking item");
            ModernUIHelper.ShowModernError($"Error picking item: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BtnClear_Click(object? sender, EventArgs e)
    {
        ClearForm();
        txtBarcode.Focus();
    }

    private void PickingForm_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.F1:
                if (btnPick.Enabled)
                    btnPick.PerformClick();
                break;
            case Keys.F2:
                btnClear.PerformClick();
                break;
            case Keys.Escape:
                Close();
                break;
        }
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(lblItemInfo.Text) || lblItemInfo.Text == "No item selected")
        {
            ModernUIHelper.ShowModernError("Please scan a valid barcode first");
            txtBarcode.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(txtQuantity.Text) || !decimal.TryParse(txtQuantity.Text, out var quantity) ||
            quantity <= 0)
        {
            ModernUIHelper.ShowModernError("Please enter a valid quantity");
            txtQuantity.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(txtFromLocation.Text))
        {
            ModernUIHelper.ShowModernError("Please enter a from location code");
            txtFromLocation.Focus();
            return false;
        }

        return true;
    }

    private string ExtractSkuFromLabel()
    {
        // Extract SKU from "SKU: WIDGET-001\nName: ..." format
        var lines = lblItemInfo.Text.Split('\n');
        if (lines.Length > 0 && lines[0].StartsWith("SKU: ", StringComparison.Ordinal))
        {
            return lines[0].Substring(5);
        }

        return string.Empty;
    }

    private void ClearForm()
    {
        txtBarcode.Clear();
        txtQuantity.Clear();
        txtFromLocation.Clear();
        txtOrderNumber.Clear();
        txtNotes.Clear();
        lblItemInfo.Text = "No item selected";
        lblItemInfo.ForeColor = ModernUIHelper.Colors.TextMuted;
    }

    private void SetBusy(bool isBusy)
    {
        Cursor = isBusy ? Cursors.WaitCursor : Cursors.Default;
        btnPick.Enabled = !isBusy;
        btnClear.Enabled = !isBusy;
    }
}

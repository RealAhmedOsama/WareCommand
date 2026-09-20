// Wms.WinForms/Forms/PutawayForm.cs

using System.Globalization;
using Microsoft.Extensions.Logging;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Application.UseCases.Items;
using Wms.Application.UseCases.Receiving;
using Wms.WinForms.Common;

namespace Wms.WinForms.Forms;

public partial class PutawayForm : Form
{
    private readonly ICurrentUser _currentUser;
    private readonly IGetItemsUseCase _getItemsUseCase;
    private readonly ILogger<PutawayForm> _logger;
    private readonly IPutawayUseCase _putawayUseCase;

    public PutawayForm(
        IPutawayUseCase putawayUseCase,
        IGetItemsUseCase getItemsUseCase,
        ICurrentUser currentUser,
        ILogger<PutawayForm> logger)
    {
        _putawayUseCase = putawayUseCase;
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
        txtToLocation.KeyPress += TxtToLocation_KeyPress;
        btnPutaway.Click += BtnPutaway_Click;
        btnClear.Click += BtnClear_Click;
        KeyDown += PutawayForm_KeyDown;
    }

    private void SetupForm()
    {
        ModernUIHelper.StyleForm(this);
        KeyPreview = true;

        // Apply modern styling
        ModernUIHelper.StyleModernTextBox(txtBarcode);
        ModernUIHelper.StyleModernTextBox(txtQuantity);
        ModernUIHelper.StyleModernTextBox(txtFromLocation);
        ModernUIHelper.StyleModernTextBox(txtToLocation);
        ModernUIHelper.StyleModernTextBox(txtNotes);

        ModernUIHelper.StylePrimaryButton(btnPutaway);
        ModernUIHelper.StyleSecondaryButton(btnClear);

        txtFromLocation.Text = "RECEIVE"; // Default from receiving
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
            txtToLocation.Focus();
        }
    }

    private void TxtToLocation_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (e.KeyChar == (char)Keys.Enter)
        {
            e.Handled = true;
            btnPutaway.PerformClick();
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
            _logger.LogError(ex, "Error processing barcode input");
            ModernUIHelper.ShowModernError($"Error processing barcode: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BtnPutaway_Click(object? sender, EventArgs e)
    {
        try
        {
            if (!ValidateInput())
                return;

            SetBusy(true);

            var request = new PutawayDto(
                ExtractSkuFromLabel(),
                txtFromLocation.Text.Trim(),
                txtToLocation.Text.Trim(),
                decimal.Parse(txtQuantity.Text, CultureInfo.CurrentCulture),
                txtNotes.Text.Trim()
            );

            var result = await _putawayUseCase.ExecuteAsync(request, _currentUser.RequireUserId());

            if (result.IsFailure)
            {
                ModernUIHelper.ShowModernError(result.Error);
                return;
            }

            ModernUIHelper.ShowModernSuccess($"Item put away successfully!\nMovement ID: {result.Value.MovementId}");

            ClearForm();
            txtBarcode.Focus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error putting away item");
            ModernUIHelper.ShowModernError($"Error putting away item: {ex.Message}");
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

    private void PutawayForm_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.F1:
                if (btnPutaway.Enabled)
                    btnPutaway.PerformClick();
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

        if (string.IsNullOrWhiteSpace(txtToLocation.Text))
        {
            ModernUIHelper.ShowModernError("Please enter a to location code");
            txtToLocation.Focus();
            return false;
        }

        if (txtFromLocation.Text.Trim().Equals(txtToLocation.Text.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            ModernUIHelper.ShowModernError("From and To locations cannot be the same");
            txtToLocation.Focus();
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
        txtToLocation.Clear();
        txtNotes.Clear();
        lblItemInfo.Text = "No item selected";
        lblItemInfo.ForeColor = ModernUIHelper.Colors.TextMuted;
        // Keep the from location as it's typically the same
    }

    private void SetBusy(bool isBusy)
    {
        Cursor = isBusy ? Cursors.WaitCursor : Cursors.Default;
        btnPutaway.Enabled = !isBusy;
        btnClear.Enabled = !isBusy;
    }
}

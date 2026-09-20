// Wms.WinForms/Forms/ItemEditDialog.cs

using Microsoft.Extensions.Logging;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Application.UseCases.Items;
using Wms.WinForms.Common;
using CreateItemDto = Wms.Application.UseCases.Items.CreateItemDto;
using UpdateItemDto = Wms.Application.UseCases.Items.UpdateItemDto;

namespace Wms.WinForms.Forms;

public partial class ItemEditDialog : Form
{
    private readonly ICurrentUser _currentUser;
    private readonly ICreateItemUseCase _createItemUseCase;
    private readonly IGetItemsUseCase _getItemsUseCase;

    private readonly int? _itemId;
    private readonly ILogger<ItemEditDialog> _logger;
    private readonly IUpdateItemUseCase _updateItemUseCase;
    private ItemDto? _originalItem;

    public ItemEditDialog(ICreateItemUseCase createItemUseCase, IUpdateItemUseCase updateItemUseCase,
        IGetItemsUseCase getItemsUseCase, ILogger<ItemEditDialog> logger, ICurrentUser currentUser, int? itemId = null)
    {
        _createItemUseCase = createItemUseCase;
        _updateItemUseCase = updateItemUseCase;
        _getItemsUseCase = getItemsUseCase;
        _logger = logger;
        _currentUser = currentUser;
        _itemId = itemId;

        InitializeComponent();
        Wms.WinForms.Common.WmsDesktopLocalization.Apply(this);
        SetupEventHandlers();
        SetupForm();

        if (IsEditMode)
        {
            LoadItemDataAsync();
        }
    }

    public bool IsEditMode => _itemId.HasValue;

    private void SetupEventHandlers()
    {
        btnSave.Click += BtnSave_Click;
        btnCancel.Click += BtnCancel_Click;
        btnAddBarcode.Click += BtnAddBarcode_Click;
        btnRemoveBarcode.Click += BtnRemoveBarcode_Click;
        lstBarcodes.SelectedIndexChanged += LstBarcodes_SelectedIndexChanged;
        KeyDown += ItemEditDialog_KeyDown;
    }

    private void SetupForm()
    {
        ModernUIHelper.StyleForm(this);
        KeyPreview = true;

        // Apply modern styling
        ModernUIHelper.StyleModernTextBox(txtSku);
        ModernUIHelper.StyleModernTextBox(txtName);
        ModernUIHelper.StyleModernTextBox(txtDescription);
        ModernUIHelper.StyleModernTextBox(txtUnitOfMeasure);
        ModernUIHelper.StyleModernTextBox(txtNewBarcode);

        ModernUIHelper.StylePrimaryButton(btnSave);
        ModernUIHelper.StyleSecondaryButton(btnCancel);
        ModernUIHelper.StyleSuccessButton(btnAddBarcode);
        ModernUIHelper.StyleDangerButton(btnRemoveBarcode);

        // Set up numeric up-down styling
        numShelfLifeDays.Font = ModernUIHelper.Fonts.Body;
        numShelfLifeDays.Height = 35;

        btnRemoveBarcode.Enabled = false;

        Text = IsEditMode ? "Edit Item" : "Add New Item";
        lblFormTitle.Text = IsEditMode ? "Edit Item" : "Add New Item";

        if (!IsEditMode)
        {
            txtSku.ReadOnly = false;
            txtSku.BackColor = Color.White;
        }
        else
        {
            txtSku.ReadOnly = true;
            txtSku.BackColor = ModernUIHelper.Colors.BackgroundSecondary;
        }
    }

    private async void LoadItemDataAsync()
    {
        if (!_itemId.HasValue) return;

        try
        {
            SetBusy(true);

            var result = await _getItemsUseCase.GetByIdAsync(_itemId.Value);
            if (result.IsFailure)
            {
                ModernUIHelper.ShowModernError($"Error loading item: {result.Error}");
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            _originalItem = result.Value;
            PopulateForm(_originalItem);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading item {ItemId}", _itemId);
            ModernUIHelper.ShowModernError($"Error loading item: {ex.Message}");
            DialogResult = DialogResult.Cancel;
            Close();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PopulateForm(ItemDto item)
    {
        txtSku.Text = item.Sku;
        txtName.Text = item.Name;
        txtDescription.Text = item.Description;
        txtUnitOfMeasure.Text = item.UnitOfMeasure;
        chkRequiresLot.Checked = item.RequiresLot;
        chkRequiresSerial.Checked = item.RequiresSerial;
        numShelfLifeDays.Value = item.ShelfLifeDays;

        lstBarcodes.Items.Clear();
        foreach (var barcode in item.Barcodes)
        {
            lstBarcodes.Items.Add(barcode);
        }
    }

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        try
        {
            if (!ValidateInput()) return;

            SetBusy(true);

            var barcodes = lstBarcodes.Items.Cast<string>().ToList();

            if (IsEditMode)
            {
                var request = new UpdateItemDto(
                    _itemId!.Value,
                    txtName.Text.Trim(),
                    txtDescription.Text.Trim(),
                    (int)numShelfLifeDays.Value,
                    barcodes
                );

                var result = await _updateItemUseCase.ExecuteAsync(request, _currentUser.RequireUserId());
                if (result.IsFailure)
                {
                    ModernUIHelper.ShowModernError(result.Error);
                    return;
                }
            }
            else
            {
                var request = new CreateItemDto(
                    txtSku.Text.Trim(),
                    txtName.Text.Trim(),
                    txtDescription.Text.Trim(),
                    txtUnitOfMeasure.Text.Trim(),
                    chkRequiresLot.Checked,
                    chkRequiresSerial.Checked,
                    (int)numShelfLifeDays.Value,
                    barcodes
                );

                var result = await _createItemUseCase.ExecuteAsync(request, _currentUser.RequireUserId());
                if (result.IsFailure)
                {
                    ModernUIHelper.ShowModernError(result.Error);
                    return;
                }
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving item");
            ModernUIHelper.ShowModernError($"Error saving item: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void BtnAddBarcode_Click(object? sender, EventArgs e)
    {
        var barcode = txtNewBarcode.Text.Trim();
        if (string.IsNullOrWhiteSpace(barcode))
        {
            ModernUIHelper.ShowModernError("Please enter a barcode");
            txtNewBarcode.Focus();
            return;
        }

        if (barcode.Length < 3)
        {
            ModernUIHelper.ShowModernError("Barcode must be at least 3 characters long");
            txtNewBarcode.Focus();
            return;
        }

        if (lstBarcodes.Items.Contains(barcode))
        {
            ModernUIHelper.ShowModernWarning("This barcode is already added");
            txtNewBarcode.Focus();
            return;
        }

        lstBarcodes.Items.Add(barcode);
        txtNewBarcode.Clear();
        txtNewBarcode.Focus();
    }

    private void BtnRemoveBarcode_Click(object? sender, EventArgs e)
    {
        if (lstBarcodes.SelectedIndex >= 0)
        {
            lstBarcodes.Items.RemoveAt(lstBarcodes.SelectedIndex);
        }
    }

    private void LstBarcodes_SelectedIndexChanged(object? sender, EventArgs e)
    {
        btnRemoveBarcode.Enabled = lstBarcodes.SelectedIndex >= 0;
    }

    private void ItemEditDialog_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.F1:
                btnSave.PerformClick();
                break;
            case Keys.Escape:
                btnCancel.PerformClick();
                break;
            case Keys.Enter:
                if (txtNewBarcode.Focused)
                {
                    e.Handled = true;
                    btnAddBarcode.PerformClick();
                }

                break;
        }
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(txtSku.Text))
        {
            ModernUIHelper.ShowModernError("SKU is required");
            txtSku.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(txtName.Text))
        {
            ModernUIHelper.ShowModernError("Name is required");
            txtName.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(txtUnitOfMeasure.Text))
        {
            ModernUIHelper.ShowModernError("Unit of Measure is required");
            txtUnitOfMeasure.Focus();
            return false;
        }

        return true;
    }

    private void SetBusy(bool isBusy)
    {
        Cursor = isBusy ? Cursors.WaitCursor : Cursors.Default;
        btnSave.Enabled = !isBusy;
        btnCancel.Enabled = !isBusy;
        pnlMain.Enabled = !isBusy;
    }
}

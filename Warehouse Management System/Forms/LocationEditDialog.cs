// Wms.WinForms/Forms/LocationEditDialog.cs

using Microsoft.Extensions.Logging;
using Wms.Application.DTOs;
using Wms.Application.UseCases.Locations;
using Wms.WinForms.Common;
using CreateLocationDto = Wms.Application.UseCases.Locations.CreateLocationDto;
using UpdateLocationDto = Wms.Application.UseCases.Locations.UpdateLocationDto;

namespace Wms.WinForms.Forms;

public partial class LocationEditDialog : Form
{
    private const string CurrentUserId = "SYSTEM";
    private readonly ICreateLocationUseCase _createLocationUseCase;
    private readonly IGetLocationsUseCase _getLocationsUseCase;

    private readonly int? _locationId;
    private readonly ILogger<LocationEditDialog> _logger;
    private readonly IUpdateLocationUseCase _updateLocationUseCase;
    private LocationDto? _originalLocation;

    public LocationEditDialog(ICreateLocationUseCase createLocationUseCase,
        IUpdateLocationUseCase updateLocationUseCase,
        IGetLocationsUseCase getLocationsUseCase, ILogger<LocationEditDialog> logger, int? locationId = null)
    {
        _createLocationUseCase = createLocationUseCase;
        _updateLocationUseCase = updateLocationUseCase;
        _getLocationsUseCase = getLocationsUseCase;
        _logger = logger;
        _locationId = locationId;

        InitializeComponent();
        SetupEventHandlers();
        SetupForm();

        if (IsEditMode)
        {
            _ = LoadLocationDataAsync();
        }
        else
        {
            _ = LoadParentLocationsAsync();
        }
    }

    public bool IsEditMode => _locationId.HasValue;

    private void SetupEventHandlers()
    {
        btnSave.Click += BtnSave_Click;
        btnCancel.Click += BtnCancel_Click;
        KeyDown += LocationEditDialog_KeyDown;
    }

    private void SetupForm()
    {
        ModernUIHelper.StyleForm(this);
        KeyPreview = true;

        // Apply modern styling
        ModernUIHelper.StyleModernTextBox(txtCode);
        ModernUIHelper.StyleModernTextBox(txtName);
        ModernUIHelper.StyleModernComboBox(cmbParentLocation);

        ModernUIHelper.StylePrimaryButton(btnSave);
        ModernUIHelper.StyleSecondaryButton(btnCancel);

        // Set default values
        chkIsPickable.Checked = true;
        chkIsReceivable.Checked = true;
        numCapacity.Value = 1000;

        Text = IsEditMode ? "Edit Location" : "Add New Location";
        lblFormTitle.Text = IsEditMode ? "Edit Location" : "Add New Location";

        if (!IsEditMode)
        {
            txtCode.ReadOnly = false;
            txtCode.BackColor = Color.White;
        }
        else
        {
            txtCode.ReadOnly = true;
            txtCode.BackColor = ModernUIHelper.Colors.BackgroundSecondary;
        }
    }

    private async Task LoadLocationDataAsync()
    {
        if (!_locationId.HasValue) return;

        try
        {
            SetBusy(true);

            // For simplicity, we'll load all locations and find the one we need
            var result = await _getLocationsUseCase.GetAllAsync();
            if (result.IsFailure)
            {
                ModernUIHelper.ShowModernError($"Error loading location: {result.Error}");
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            _originalLocation = result.Value.FirstOrDefault(l => l.Id == _locationId.Value);
            if (_originalLocation == null)
            {
                ModernUIHelper.ShowModernError("Location not found");
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            await LoadParentLocationsAsync();
            PopulateForm(_originalLocation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading location {LocationId}", _locationId);
            ModernUIHelper.ShowModernError($"Error loading location: {ex.Message}");
            DialogResult = DialogResult.Cancel;
            Close();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadParentLocationsAsync()
    {
        try
        {
            var result = await _getLocationsUseCase.GetAllAsync();
            if (result.IsSuccess)
            {
                var locations = result.Value
                    .Where(l => l.Id != _locationId) // Don't include self as parent
                    .OrderBy(l => l.FullPath)
                    .ToList();

                cmbParentLocation.Items.Clear();
                cmbParentLocation.Items.Add("(No Parent)");

                foreach (var location in locations)
                {
                    cmbParentLocation.Items.Add(new LocationComboItem(location.Id, location.FullPath));
                }

                cmbParentLocation.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading parent locations");
        }
    }

    private void PopulateForm(LocationDto location)
    {
        txtCode.Text = location.Code;
        txtName.Text = location.Name;
        chkIsPickable.Checked = location.IsPickable;
        chkIsReceivable.Checked = location.IsReceivable;
        numCapacity.Value = location.Capacity;

        // Set parent location
        if (location.ParentLocationId.HasValue)
        {
            for (var i = 1; i < cmbParentLocation.Items.Count; i++) // Skip "(No Parent)"
            {
                if (cmbParentLocation.Items[i] is LocationComboItem item &&
                    item.Id == location.ParentLocationId.Value)
                {
                    cmbParentLocation.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        try
        {
            if (!ValidateInput()) return;

            SetBusy(true);

            var parentLocationId = GetSelectedParentLocationId();

            if (IsEditMode)
            {
                var request = new UpdateLocationDto(
                    _locationId!.Value,
                    txtName.Text.Trim(),
                    chkIsPickable.Checked,
                    chkIsReceivable.Checked,
                    (int)numCapacity.Value
                );

                var result = await _updateLocationUseCase.ExecuteAsync(request, CurrentUserId);
                if (result.IsFailure)
                {
                    ModernUIHelper.ShowModernError(result.Error);
                    return;
                }
            }
            else
            {
                var request = new CreateLocationDto(
                    txtCode.Text.Trim(),
                    txtName.Text.Trim(),
                    1, // Default warehouse ID - TODO: Make this configurable
                    parentLocationId,
                    chkIsPickable.Checked,
                    chkIsReceivable.Checked,
                    (int)numCapacity.Value
                );

                var result = await _createLocationUseCase.ExecuteAsync(request, CurrentUserId);
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
            _logger.LogError(ex, "Error saving location");
            ModernUIHelper.ShowModernError($"Error saving location: {ex.Message}");
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

    private void LocationEditDialog_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.F1:
                btnSave.PerformClick();
                break;
            case Keys.Escape:
                btnCancel.PerformClick();
                break;
        }
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(txtCode.Text))
        {
            ModernUIHelper.ShowModernError("Code is required");
            txtCode.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(txtName.Text))
        {
            ModernUIHelper.ShowModernError("Name is required");
            txtName.Focus();
            return false;
        }

        if (numCapacity.Value < 0)
        {
            ModernUIHelper.ShowModernError("Capacity cannot be negative");
            numCapacity.Focus();
            return false;
        }

        return true;
    }

    private int? GetSelectedParentLocationId()
    {
        if (cmbParentLocation.SelectedIndex <= 0) return null;

        if (cmbParentLocation.SelectedItem is LocationComboItem item)
        {
            return item.Id;
        }

        return null;
    }

    private void SetBusy(bool isBusy)
    {
        Cursor = isBusy ? Cursors.WaitCursor : Cursors.Default;
        btnSave.Enabled = !isBusy;
        btnCancel.Enabled = !isBusy;
        pnlMain.Enabled = !isBusy;
    }

    // Helper class for combo box items
    private sealed class LocationComboItem
    {
        public LocationComboItem(int id, string fullPath)
        {
            Id = id;
            FullPath = fullPath;
        }

        public int Id { get; }
        public string FullPath { get; }

        public override string ToString()
        {
            return FullPath;
        }
    }
}

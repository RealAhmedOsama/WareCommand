// Wms.WinForms/Forms/LocationManagementForm.cs

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wms.Application.Identity;
using Wms.Application.Settings;
using Wms.Application.UseCases.Locations;
using Wms.WinForms.Common;

namespace Wms.WinForms.Forms;

public partial class LocationManagementForm : Form
{
    private readonly ICurrentUser _currentUser;
    private readonly IGetLocationsUseCase _getLocationsUseCase;
    private readonly ILogger<LocationManagementForm> _logger;

    public LocationManagementForm(
        IGetLocationsUseCase getLocationsUseCase,
        ICurrentUser currentUser,
        ILogger<LocationManagementForm> logger)
    {
        _getLocationsUseCase = getLocationsUseCase;
        _currentUser = currentUser;
        _logger = logger;
        InitializeComponent();
        Wms.WinForms.Common.WmsDesktopLocalization.Apply(this);
        SetupEventHandlers();
        SetupForm();
        _ = LoadLocationsAsync();
    }

    private void SetupEventHandlers()
    {
        btnAdd.Click += BtnAdd_Click;
        btnEdit.Click += BtnEdit_Click;
        btnDelete.Click += BtnDelete_Click;
        btnRefresh.Click += BtnRefresh_Click;
        txtSearch.KeyPress += TxtSearch_KeyPress;
        dgvLocations.SelectionChanged += DgvLocations_SelectionChanged;
        dgvLocations.CellDoubleClick += DgvLocations_CellDoubleClick;
        KeyDown += LocationManagementForm_KeyDown;
    }

    private void SetupForm()
    {
        ModernUIHelper.StyleForm(this);
        KeyPreview = true;
        btnEdit.Enabled = false;
        btnDelete.Enabled = false;
    }

    private async void TxtSearch_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (e.KeyChar == (char)Keys.Enter)
        {
            e.Handled = true;
            await SearchLocationsAsync();
        }
    }

    private async void BtnRefresh_Click(object? sender, EventArgs e)
    {
        await LoadLocationsAsync();
    }

    private void BtnAdd_Click(object? sender, EventArgs e)
    {
        ShowLocationDialog();
    }

    private void BtnEdit_Click(object? sender, EventArgs e)
    {
        if (dgvLocations.SelectedRows.Count == 0) return;

        var selectedRow = dgvLocations.SelectedRows[0];
        var locationId = Convert.ToInt32(selectedRow.Cells["Id"].Value, CultureInfo.CurrentCulture);
        ShowLocationDialog(locationId);
    }

    private async void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (dgvLocations.SelectedRows.Count == 0) return;

        var selectedRow = dgvLocations.SelectedRows[0];
        var locationCode = selectedRow.Cells["Code"].Value?.ToString() ?? "";
        var locationName = selectedRow.Cells["Name"].Value?.ToString() ?? "";

        var result = MessageBox.Show(
            $"Are you sure you want to delete location '{locationCode} - {locationName}'?\n\nThis action cannot be undone.",
            "Confirm Delete",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result == DialogResult.Yes)
        {
            // TODO: Implement delete functionality
            ModernUIHelper.ShowModernWarning("Delete functionality not implemented yet.");
        }
    }

    private void DgvLocations_SelectionChanged(object? sender, EventArgs e)
    {
        var hasSelection = dgvLocations.SelectedRows.Count > 0;
        btnEdit.Enabled = hasSelection;
        btnDelete.Enabled = hasSelection;
    }

    private void DgvLocations_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
        {
            BtnEdit_Click(sender, e);
        }
    }

    private void LocationManagementForm_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.F1:
                btnAdd.PerformClick();
                break;
            case Keys.F2:
                if (btnEdit.Enabled)
                    btnEdit.PerformClick();
                break;
            case Keys.Delete:
                if (btnDelete.Enabled)
                    btnDelete.PerformClick();
                break;
            case Keys.F5:
                btnRefresh.PerformClick();
                break;
            case Keys.Escape:
                Close();
                break;
        }
    }

    private async Task LoadLocationsAsync()
    {
        try
        {
            SetBusy(true);
            lblStatus.Text = "Loading locations...";

            var result = await _getLocationsUseCase.ExecuteAsync();

            if (result.IsFailure)
            {
                ModernUIHelper.ShowModernError($"Error loading locations: {result.Error}");
                return;
            }

            var locations = result.Value.OrderBy(l => l.Code).ToList();

            dgvLocations.DataSource = null;
            dgvLocations.DataSource = locations;

            ConfigureGridColumns();

            lblStatus.Text = $"Loaded {locations.Count} locations";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading locations");
            ModernUIHelper.ShowModernError($"Error loading locations: {ex.Message}");
            lblStatus.Text = "Error loading locations";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SearchLocationsAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                await LoadLocationsAsync();
                return;
            }

            SetBusy(true);
            lblStatus.Text = "Searching locations...";

            var result = await _getLocationsUseCase.ExecuteAsync(txtSearch.Text.Trim());

            if (result.IsFailure)
            {
                ModernUIHelper.ShowModernError($"Search error: {result.Error}");
                return;
            }

            var locations = result.Value.OrderBy(l => l.Code).ToList();

            dgvLocations.DataSource = null;
            dgvLocations.DataSource = locations;

            ConfigureGridColumns();

            lblStatus.Text = $"Found {locations.Count} matching locations";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching locations");
            ModernUIHelper.ShowModernError($"Error searching: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ConfigureGridColumns()
    {
        if (dgvLocations?.Columns == null || dgvLocations.Columns.Count == 0) return;

        try
        {
            ModernUIHelper.StyleModernDataGridView(dgvLocations);

            // Hide ID columns safely
            SetColumnPropertySafely("Id", col => col.Visible = false);
            SetColumnPropertySafely("WarehouseId", col => col.Visible = false);
            SetColumnPropertySafely("ParentLocationId", col => col.Visible = false);
            SetColumnPropertySafely("CreatedAt", col => col.Visible = false);
            SetColumnPropertySafely("UpdatedAt", col => col.Visible = false);

            // Configure visible columns - use only FillWeight, no MinimumWidth
            SetColumnPropertySafely("Code", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Code";
                    col.FillWeight = 15;
                }
            });

            SetColumnPropertySafely("Name", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Name";
                    col.FillWeight = 25;
                }
            });

            SetColumnPropertySafely("FullPath", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Full Path";
                    col.FillWeight = 35;
                }
            });

            SetColumnPropertySafely("IsActive", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Active";
                    col.FillWeight = 8;
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                }
            });

            SetColumnPropertySafely("IsPickable", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Pickable";
                    col.FillWeight = 8;
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                }
            });

            SetColumnPropertySafely("IsReceivable", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Receivable";
                    col.FillWeight = 9;
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                }
            });

            // Use FillWeight instead of fixed Width for better responsiveness
            dgvLocations.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring grid columns");
            // Fallback to basic auto-sizing if configuration fails
            try
            {
                dgvLocations.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            }
            catch
            {
                // If even basic auto-sizing fails, just leave as-is
            }
        }
    }

    private void SetColumnPropertySafely(string columnName, Action<DataGridViewColumn> configureAction)
    {
        try
        {
            if (dgvLocations?.Columns?.Contains(columnName) == true)
            {
                var column = dgvLocations.Columns[columnName];
                if (column != null && !column.IsDataBound) // Check if column is in valid state
                {
                    configureAction(column);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error configuring column {ColumnName}", columnName);
        }
    }

    private void ShowLocationDialog(int? locationId = null)
    {
        try
        {
            var createLocationUseCase = Program.ServiceProvider.GetRequiredService<ICreateLocationUseCase>();
            var updateLocationUseCase = Program.ServiceProvider.GetRequiredService<IUpdateLocationUseCase>();
            var logger = Program.ServiceProvider.GetRequiredService<ILogger<LocationEditDialog>>();
            var settingsService = Program.ServiceProvider.GetRequiredService<IWmsSettingsService>();

            var dialog = new LocationEditDialog(
                createLocationUseCase,
                updateLocationUseCase,
                _getLocationsUseCase,
                logger,
                _currentUser,
                settingsService,
                locationId);

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                // Reload the locations grid
                _ = LoadLocationsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing location dialog");
            ModernUIHelper.ShowModernError($"Error opening location dialog: {ex.Message}");
        }
    }

    private void SetBusy(bool isBusy)
    {
        Cursor = isBusy ? Cursors.WaitCursor : Cursors.Default;
        btnAdd.Enabled = !isBusy;
        btnEdit.Enabled = !isBusy && dgvLocations.SelectedRows.Count > 0;
        btnDelete.Enabled = !isBusy && dgvLocations.SelectedRows.Count > 0;
        btnRefresh.Enabled = !isBusy;
        dgvLocations.Enabled = !isBusy;
    }
}

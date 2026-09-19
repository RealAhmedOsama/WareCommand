// Wms.WinForms/Forms/InventoryForm.cs

using Microsoft.Extensions.Logging;
using Wms.Application.DTOs;
using Wms.Application.UseCases.Inventory;
using Wms.Application.UseCases.Items;
using Wms.WinForms.Common;

namespace Wms.WinForms.Forms;

public partial class InventoryForm : Form
{
    private const string CurrentUserId = "SYSTEM"; // TODO: Implement proper user management
    private readonly IGetItemsUseCase _getItemsUseCase;
    private readonly IGetStockUseCase _getStockUseCase;
    private readonly ILogger<InventoryForm> _logger;
    private readonly IStockAdjustmentUseCase _stockAdjustmentUseCase;

    public InventoryForm(IGetStockUseCase getStockUseCase, IStockAdjustmentUseCase stockAdjustmentUseCase,
        IGetItemsUseCase getItemsUseCase, ILogger<InventoryForm> logger)
    {
        _getStockUseCase = getStockUseCase;
        _stockAdjustmentUseCase = stockAdjustmentUseCase;
        _getItemsUseCase = getItemsUseCase;
        _logger = logger;
        InitializeComponent();
        SetupEventHandlers();
        SetupForm();
        _ = LoadStockDataAsync();
    }

    private void SetupEventHandlers()
    {
        btnRefresh.Click += BtnRefresh_Click;
        btnAdjustment.Click += BtnAdjustment_Click;
        btnSummary.Click += BtnSummary_Click;
        txtSearch.KeyPress += TxtSearch_KeyPress;
        dgvStock.SelectionChanged += DgvStock_SelectionChanged;
        KeyDown += InventoryForm_KeyDown;
    }

    private void SetupForm()
    {
        ModernUIHelper.StyleForm(this);
        KeyPreview = true;

        // Apply modern styling
        ModernUIHelper.StyleModernTextBox(txtSearch);
        ModernUIHelper.StylePrimaryButton(btnRefresh);
        ModernUIHelper.StyleSuccessButton(btnAdjustment);
        ModernUIHelper.StyleSecondaryButton(btnSummary);
        ModernUIHelper.StyleModernDataGridView(dgvStock);
    }

    private async void TxtSearch_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (e.KeyChar == (char)Keys.Enter)
        {
            e.Handled = true;
            await SearchStockAsync();
        }
    }

    private async void BtnRefresh_Click(object? sender, EventArgs e)
    {
        await LoadStockDataAsync();
    }

    private void BtnAdjustment_Click(object? sender, EventArgs e)
    {
        if (dgvStock.SelectedRows.Count == 0)
        {
            ModernUIHelper.ShowModernError("Please select a stock item to adjust");
            return;
        }

        var selectedRow = dgvStock.SelectedRows[0];
        var itemSku = GetCellValueSafely(selectedRow, "ItemSku");
        var locationCode = GetCellValueSafely(selectedRow, "LocationCode");
        var currentQuantity = GetCellValueSafely(selectedRow, "QuantityAvailable");

        if (string.IsNullOrWhiteSpace(itemSku) || string.IsNullOrWhiteSpace(locationCode))
        {
            ModernUIHelper.ShowModernError("Selected row does not contain valid item or location information");
            return;
        }

        ShowAdjustmentDialog(itemSku, locationCode, currentQuantity);
    }

    private async void BtnSummary_Click(object? sender, EventArgs e)
    {
        try
        {
            SetBusy(true);
            lblStatus.Text = "Loading stock summary...";

            var result = await _getStockUseCase.GetStockSummaryAsync();

            if (result.IsFailure)
            {
                ModernUIHelper.ShowModernError($"Error loading stock summary: {result.Error}");
                return;
            }

            var summaryData = result.Value.OrderBy(s => s.ItemSku).ToList();

            dgvStock.DataSource = null;
            dgvStock.DataSource = summaryData;

            ConfigureSummaryGridColumns();

            lblStatus.Text = $"Showing summary for {summaryData.Count} items";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading stock summary");
            ModernUIHelper.ShowModernError($"Error loading stock summary: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void DgvStock_SelectionChanged(object? sender, EventArgs e)
    {
        btnAdjustment.Enabled = dgvStock.SelectedRows.Count > 0;
    }

    private void InventoryForm_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.F1:
                btnRefresh.PerformClick();
                break;
            case Keys.F2:
                if (btnAdjustment.Enabled)
                    btnAdjustment.PerformClick();
                break;
            case Keys.F3:
                btnSummary.PerformClick();
                break;
            case Keys.Escape:
                Close();
                break;
        }
    }

    private async Task LoadStockDataAsync()
    {
        try
        {
            SetBusy(true);
            lblStatus.Text = "Loading stock data...";

            var result = await _getStockUseCase.GetAllStockAsync();

            if (result.IsFailure)
            {
                ModernUIHelper.ShowModernError($"Error loading stock: {result.Error}");
                return;
            }

            var stockData = result.Value.Where(s => s.QuantityAvailable > 0).ToList();

            dgvStock.DataSource = null;
            dgvStock.DataSource = stockData;

            ConfigureGridColumns();

            lblStatus.Text = $"Showing {stockData.Count} stock items";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading stock data");
            ModernUIHelper.ShowModernError($"Error loading stock data: {ex.Message}");
            lblStatus.Text = "Error loading data";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SearchStockAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                await LoadStockDataAsync();
                return;
            }

            SetBusy(true);
            lblStatus.Text = "Searching...";

            // Search by item first
            var itemResult = await _getItemsUseCase.ExecuteAsync(txtSearch.Text.Trim());

            if (itemResult.IsFailure)
            {
                ModernUIHelper.ShowModernError($"Search error: {itemResult.Error}");
                return;
            }

            var items = itemResult.Value.ToList();
            var allStockData = new List<StockDto>();

            foreach (var item in items)
            {
                var stockResult = await _getStockUseCase.GetStockByItemAsync(item.Id);
                if (stockResult.IsSuccess)
                {
                    allStockData.AddRange(stockResult.Value.Where(s => s.QuantityAvailable > 0));
                }
            }

            dgvStock.DataSource = null;
            dgvStock.DataSource = allStockData;

            ConfigureGridColumns();

            lblStatus.Text = $"Found {allStockData.Count} matching stock items";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching stock");
            ModernUIHelper.ShowModernError($"Error searching: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ConfigureGridColumns()
    {
        if (dgvStock?.Columns == null || dgvStock.Columns.Count == 0)
        {
            _logger.LogDebug("DataGridView has no columns to configure");
            return;
        }

        try
        {
            // Disable auto-sizing temporarily to prevent conflicts
            dgvStock.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

            // Hide ID columns safely
            SetColumnPropertySafely("Id", col => col.Visible = false);
            SetColumnPropertySafely("ItemId", col => col.Visible = false);
            SetColumnPropertySafely("LocationId", col => col.Visible = false);
            SetColumnPropertySafely("LotId", col => col.Visible = false);
            SetColumnPropertySafely("CreatedAt", col => col.Visible = false);
            SetColumnPropertySafely("UpdatedAt", col => col.Visible = false);

            // Configure visible columns using only FillWeight for responsiveness
            SetColumnPropertySafely("ItemSku", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "SKU";
                    col.FillWeight = 12;
                }
            });

            SetColumnPropertySafely("ItemName", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Item Name";
                    col.FillWeight = 25;
                }
            });

            SetColumnPropertySafely("LocationCode", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Location";
                    col.FillWeight = 12;
                }
            });

            SetColumnPropertySafely("LocationName", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Location Name";
                    col.FillWeight = 20;
                }
            });

            SetColumnPropertySafely("QuantityAvailable", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Available";
                    col.FillWeight = 10;
                    col.DefaultCellStyle.Format = "N2";
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
            });

            SetColumnPropertySafely("QuantityReserved", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Reserved";
                    col.FillWeight = 10;
                    col.DefaultCellStyle.Format = "N2";
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
            });

            SetColumnPropertySafely("AvailableQuantity", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Net Available";
                    col.FillWeight = 11;
                    col.DefaultCellStyle.Format = "N2";
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    col.DefaultCellStyle.ForeColor = ModernUIHelper.Colors.Primary;
                }
            });

            // Apply auto-sizing last
            dgvStock.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring DataGridView columns");
            // Fallback to basic configuration
            try
            {
                dgvStock.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            }
            catch
            {
                // If even basic config fails, just leave it as-is
            }
        }
    }

    private void ConfigureSummaryGridColumns()
    {
        if (dgvStock?.Columns == null || dgvStock.Columns.Count == 0) return;

        try
        {
            ModernUIHelper.StyleModernDataGridView(dgvStock);

            SetColumnPropertySafely("ItemSku", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "SKU";
                    col.FillWeight = 15;
                }
            });

            SetColumnPropertySafely("ItemName", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Item Name";
                    col.FillWeight = 35;
                }
            });

            SetColumnPropertySafely("TotalQuantity", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Total Qty";
                    col.FillWeight = 12;
                    col.DefaultCellStyle.Format = "N2";
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
            });

            SetColumnPropertySafely("TotalReserved", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Reserved";
                    col.FillWeight = 12;
                    col.DefaultCellStyle.Format = "N2";
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
            });

            SetColumnPropertySafely("TotalAvailable", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Available";
                    col.FillWeight = 12;
                    col.DefaultCellStyle.Format = "N2";
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    col.DefaultCellStyle.ForeColor = ModernUIHelper.Colors.Success;
                }
            });

            SetColumnPropertySafely("LocationCount", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Locations";
                    col.FillWeight = 14;
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                }
            });

            dgvStock.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring summary grid columns");
        }
    }

    // Helper method for safe column property setting
    private void SetColumnPropertySafely(string columnName, Action<DataGridViewColumn> configureAction)
    {
        try
        {
            if (dgvStock?.Columns?.Contains(columnName) == true)
            {
                var column = dgvStock.Columns[columnName];
                if (column != null && dgvStock.DataSource != null) // Ensure DataSource is bound
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

    // Helper method for safe cell value retrieval  
    private string GetCellValueSafely(DataGridViewRow row, string columnName)
    {
        try
        {
            if (row?.Cells != null && dgvStock?.Columns?.Contains(columnName) == true)
            {
                var cell = row.Cells[columnName];
                if (cell?.Value != null)
                {
                    return cell.Value.ToString() ?? "";
                }
            }

            return "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting cell value for column {ColumnName}", columnName);
            return "";
        }
    }

    private async void ShowAdjustmentDialog(string itemSku, string locationCode, string currentQuantity)
    {
        var adjustmentForm = new StockAdjustmentDialog(itemSku, locationCode, currentQuantity);

        if (adjustmentForm.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                SetBusy(true);

                var request = new StockAdjustmentDto(
                    itemSku,
                    locationCode,
                    adjustmentForm.NewQuantity,
                    adjustmentForm.Reason
                );

                var result = await _stockAdjustmentUseCase.ExecuteAsync(request, CurrentUserId);

                if (result.IsFailure)
                {
                    ModernUIHelper.ShowModernError(result.Error);
                    return;
                }

                ModernUIHelper.ShowModernSuccess(
                    $"Stock adjusted successfully!\nMovement ID: {result.Value.MovementId}");
                await LoadStockDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adjusting stock");
                ModernUIHelper.ShowModernError($"Error adjusting stock: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }
    }

    private void SetBusy(bool isBusy)
    {
        Cursor = isBusy ? Cursors.WaitCursor : Cursors.Default;
        btnRefresh.Enabled = !isBusy;
        btnAdjustment.Enabled = !isBusy && dgvStock.SelectedRows.Count > 0;
        btnSummary.Enabled = !isBusy;
        dgvStock.Enabled = !isBusy;
    }
}

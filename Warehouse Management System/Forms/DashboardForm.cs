// Wms.WinForms/Forms/DashboardForm.cs

using System.Globalization;
using Microsoft.Extensions.Logging;
using Wms.Application.Settings;
using Wms.Application.UseCases.Inventory;
using Wms.Application.UseCases.Items;
using Wms.Application.UseCases.Reports;
using Wms.WinForms.Common;
using Timer = System.Windows.Forms.Timer;

namespace Wms.WinForms.Forms;

public partial class DashboardForm : Form
{
    private readonly IGetItemsUseCase _getItemsUseCase;
    private readonly IGetStockUseCase _getStockUseCase;
    private readonly ILogger<DashboardForm> _logger;
    private readonly IMovementReportUseCase _movementReportUseCase;
    private readonly IWmsSettingsService _settingsService;
    private WmsSettingsValues _settings = WmsSettingsDefaults.Create();
    private Timer? _refreshTimer;

    public DashboardForm(IGetStockUseCase getStockUseCase, IGetItemsUseCase getItemsUseCase,
        IMovementReportUseCase movementReportUseCase,
        IWmsSettingsService settingsService,
        ILogger<DashboardForm> logger)
    {
        _getStockUseCase = getStockUseCase;
        _getItemsUseCase = getItemsUseCase;
        _movementReportUseCase = movementReportUseCase;
        _settingsService = settingsService;
        _logger = logger;
        InitializeComponent();
        Wms.WinForms.Common.WmsDesktopLocalization.Apply(this);
        SetupForm();
        SetupRefreshTimer();
        _ = LoadDashboardDataAsync();
    }

    private void SetupForm()
    {
        ModernUIHelper.StyleForm(this);
        KeyPreview = true;
    }

    private void SetupRefreshTimer()
    {
        _refreshTimer = new Timer();
        _refreshTimer.Interval = _settings.Dashboard.RefreshIntervalSeconds * 1000;
        _refreshTimer.Tick += async (s, e) => await LoadDashboardDataAsync();
        _refreshTimer.Start();
    }

    private async Task LoadDashboardDataAsync()
    {
        try
        {
            var settingsResult = await _settingsService.GetAsync();
            if (settingsResult.IsSuccess)
            {
                _settings = settingsResult.Value.Values;
                if (_refreshTimer is not null)
                {
                    _refreshTimer.Interval = _settings.Dashboard.RefreshIntervalSeconds * 1000;
                }
            }
            else
            {
                _logger.LogWarning(
                    "Desktop dashboard settings could not be loaded: {ErrorCode}",
                    settingsResult.ErrorCode);
            }

            lblLastRefresh.Text = WmsDesktopLocalization.Get(
                "Desktop.LastRefreshedAt",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture));

            // Load KPI data
            await LoadKpiDataAsync();

            // Load recent movements
            await LoadRecentMovementsAsync();

            // Load low stock alerts
            await LoadLowStockAlertsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading dashboard data");
            ModernUIHelper.ShowModernError($"Error loading dashboard: {ex.Message}");
        }
    }

    private async Task LoadKpiDataAsync()
    {
        try
        {
            // Total Items
            var itemsResult = await _getItemsUseCase.ExecuteAsync();
            if (itemsResult.IsSuccess)
            {
                var totalItems = itemsResult.Value.Count();
                var activeItems = itemsResult.Value.Count(i => i.IsActive);
                lblTotalItems.Text = totalItems.ToString("N0", CultureInfo.CurrentCulture);
                lblActiveItems.Text = $"{activeItems:N0} {WmsDesktopLocalization.Get("Common.Active")}";
            }

            // Stock Summary
            var stockResult = await _getStockUseCase.GetStockSummaryAsync();
            if (stockResult.IsSuccess)
            {
                var summary = stockResult.Value.ToList();
                lblTotalSKUs.Text = summary.Count.ToString("N0", CultureInfo.CurrentCulture);
                var totalValue = summary.Sum(s => s.TotalQuantity);
                lblTotalStockValue.Text = totalValue.ToString("N0", CultureInfo.CurrentCulture);
            }

            // Stock Locations
            var allStockResult = await _getStockUseCase.GetAllStockAsync();
            if (allStockResult.IsSuccess)
            {
                var locations = allStockResult.Value.Select(s => s.LocationId).Distinct().Count();
                lblStockLocations.Text = locations.ToString("N0", CultureInfo.CurrentCulture);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading KPI data");
        }
    }

    private async Task LoadRecentMovementsAsync()
    {
        try
        {
            var request = new MovementReportRequest(
                DateTime.Today.AddDays(-_settings.Reports.DefaultPeriodDays),
                DateTime.Now
            );

            var result = await _movementReportUseCase.ExecuteAsync(request);
            if (result.IsSuccess)
            {
                var recentMovements = result.Value
                    .OrderByDescending(m => m.Timestamp)
                    .Take(_settings.Dashboard.RecentMovementLimit)
                    .ToList();

                dgvRecentMovements.DataSource = recentMovements;
                ConfigureRecentMovementsGrid();
                WmsDesktopLocalization.Apply(this);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading recent movements");
        }
    }

    private async Task LoadLowStockAlertsAsync()
    {
        try
        {
            var stockResult = await _getStockUseCase.GetAllStockAsync();
            if (stockResult.IsSuccess)
            {
                var lowStockItems = stockResult.Value
                    .Where(s => s.AvailableQuantity < _settings.Dashboard.LowStockThreshold)
                    .OrderBy(s => s.AvailableQuantity)
                    .Take(_settings.Dashboard.LowStockAlertLimit)
                    .ToList();

                dgvLowStock.DataSource = lowStockItems;
                ConfigureLowStockGrid();
                WmsDesktopLocalization.Apply(this);

                lblLowStockCount.Text = lowStockItems.Count.ToString(CultureInfo.CurrentCulture);

                if (lowStockItems.Count > 0)
                {
                    pnlLowStock.BackColor = ModernUIHelper.Colors.WarningLight;
                }
                else
                {
                    pnlLowStock.BackColor = ModernUIHelper.Colors.SuccessLight;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading low stock alerts");
        }
    }

    private void ConfigureRecentMovementsGrid()
    {
        if (dgvRecentMovements?.Columns == null) return;

        try
        {
            ModernUIHelper.StyleModernDataGridView(dgvRecentMovements);

            SetColumnPropertySafely(dgvRecentMovements, "Id", col => col.Visible = false);

            SetColumnPropertySafely(dgvRecentMovements, "Type", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Type";
                    col.FillWeight = 12;
                }
            });

            SetColumnPropertySafely(dgvRecentMovements, "ItemSku", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "SKU";
                    col.FillWeight = 15;
                }
            });

            SetColumnPropertySafely(dgvRecentMovements, "Quantity", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Qty";
                    col.FillWeight = 10;
                    col.DefaultCellStyle.Format = "N1";
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
            });

            SetColumnPropertySafely(dgvRecentMovements, "Timestamp", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Time";
                    col.FillWeight = 20;
                    col.DefaultCellStyle.Format = "MM-dd HH:mm";
                }
            });

            SetColumnPropertySafely(dgvRecentMovements, "UserId", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "User";
                    col.FillWeight = 12;
                }
            });

            dgvRecentMovements.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring recent movements grid");
        }
    }

    private void ConfigureLowStockGrid()
    {
        if (dgvLowStock?.Columns == null) return;

        try
        {
            ModernUIHelper.StyleModernDataGridView(dgvLowStock);

            SetColumnPropertySafely(dgvLowStock, "Id", col => col.Visible = false);
            SetColumnPropertySafely(dgvLowStock, "ItemId", col => col.Visible = false);
            SetColumnPropertySafely(dgvLowStock, "LocationId", col => col.Visible = false);
            SetColumnPropertySafely(dgvLowStock, "CreatedAt", col => col.Visible = false);
            SetColumnPropertySafely(dgvLowStock, "UpdatedAt", col => col.Visible = false);

            SetColumnPropertySafely(dgvLowStock, "ItemSku", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "SKU";
                    col.FillWeight = 15;
                }
            });

            SetColumnPropertySafely(dgvLowStock, "ItemName", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Item";
                    col.FillWeight = 30;
                }
            });

            SetColumnPropertySafely(dgvLowStock, "LocationCode", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Location";
                    col.FillWeight = 12;
                }
            });

            SetColumnPropertySafely(dgvLowStock, "AvailableQuantity", col =>
            {
                if (col.Visible)
                {
                    col.HeaderText = "Available";
                    col.FillWeight = 12;
                    col.DefaultCellStyle.Format = "N1";
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    col.DefaultCellStyle.ForeColor = ModernUIHelper.Colors.Danger;
                }
            });

            dgvLowStock.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring low stock grid");
        }
    }

    private void SetColumnPropertySafely(DataGridView dgv, string columnName,
        Action<DataGridViewColumn> configureAction)
    {
        try
        {
            if (dgv?.Columns?.Contains(columnName) == true)
            {
                var column = dgv.Columns[columnName];
                if (column != null && dgv.DataSource != null) // Ensure DataSource is bound first
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

    private async void BtnRefresh_Click(object? sender, EventArgs e)
    {
        await LoadDashboardDataAsync();
    }

    private void DashboardForm_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.F5:
                BtnRefresh_Click(sender, e);
                break;
            case Keys.Escape:
                Close();
                break;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        base.OnFormClosed(e);
    }
}

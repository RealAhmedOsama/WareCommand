// Wms.WinForms/Forms/ReportsForm.cs

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Wms.Application.Context;
using Wms.Application.Settings;
using Wms.Application.Time;
using Wms.Application.UseCases.Reports;
using Wms.Domain.Enums;
using Wms.WinForms.Common;

namespace Wms.WinForms.Forms;

public partial class ReportsForm : Form
{
    private readonly ILogger<ReportsForm> _logger;
    private readonly IMovementReportUseCase _movementReportUseCase;
    private readonly IClock _clock;
    private readonly IWmsSettingsService _settingsService;
    private WmsSettingsValues _settings = WmsSettingsDefaults.Create();

    public ReportsForm(
        IMovementReportUseCase movementReportUseCase,
        ILogger<ReportsForm> logger,
        IClock clock,
        IWmsSettingsService settingsService)
    {
        _movementReportUseCase = movementReportUseCase;
        _logger = logger;
        _clock = clock;
        _settingsService = settingsService;
        InitializeComponent();
        Wms.WinForms.Common.WmsDesktopLocalization.Apply(this);
        SetupEventHandlers();
        SetupForm();
        _ = LoadSettingsAsync();
    }

    private void SetupEventHandlers()
    {
        btnGenerateReport.Click += BtnGenerateReport_Click;
        btnExport.Click += BtnExport_Click;
        KeyDown += ReportsForm_KeyDown;
    }

    private void SetupForm()
    {
        ModernUIHelper.StyleForm(this);
        var today = GetBusinessToday();
        dtpFromDate.Value = today.AddDays(-30);
        dtpToDate.Value = today;

        // Setup movement type combo
        cmbMovementType.Items.Add("All Types");
        cmbMovementType.Items.Add("Receipt");
        cmbMovementType.Items.Add("Putaway");
        cmbMovementType.Items.Add("Pick");
        cmbMovementType.Items.Add("Adjustment");
        cmbMovementType.SelectedIndex = 0;

        KeyPreview = true;

        // Apply modern styling
        ModernUIHelper.StyleModernTextBox(txtItemSku);
        ModernUIHelper.StyleModernComboBox(cmbMovementType);
        ModernUIHelper.StylePrimaryButton(btnGenerateReport);
        ModernUIHelper.StyleSuccessButton(btnExport);
        ModernUIHelper.StyleModernDataGridView(dgvMovements);

        btnExport.Enabled = false;
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var settingsResult = await _settingsService.GetAsync();
            if (settingsResult.IsFailure)
            {
                _logger.LogWarning(
                    "Desktop report settings could not be loaded: {ErrorCode}",
                    settingsResult.ErrorCode);
                return;
            }

            _settings = settingsResult.Value.Values;
            var today = GetBusinessToday();
            dtpFromDate.Value = today.AddDays(-_settings.Reports.DefaultPeriodDays);
            dtpToDate.Value = today;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error loading desktop report settings");
        }
    }

    private DateTime GetBusinessToday() =>
        WmsBusinessTime.GetBusinessDate(_clock.UtcNow, _settings.Localization.TimeZone)
            .ToDateTime(TimeOnly.MinValue);

    private async void BtnGenerateReport_Click(object? sender, EventArgs e)
    {
        try
        {
            SetBusy(true);
            lblStatus.Text = "Generating report...";

            var request = new MovementReportRequest(
                dtpFromDate.Value.Date,
                dtpToDate.Value.Date,
                string.IsNullOrWhiteSpace(txtItemSku.Text) ? null : txtItemSku.Text.Trim(),
                MovementType: GetSelectedMovementType()
            );

            var result = await _movementReportUseCase.ExecuteAsync(request);

            if (result.IsFailure)
            {
                ModernUIHelper.ShowModernError($"Error generating report: {result.Error}");
                lblStatus.Text = "Error generating report";
                return;
            }

            var reportData = result.Value.OrderByDescending(m => m.Timestamp).ToList();

            dgvMovements.DataSource = null;
            dgvMovements.DataSource = reportData;

            ConfigureGridColumns();

            lblStatus.Text = $"Report generated: {reportData.Count} movements found";
            btnExport.Enabled = reportData.Count > 0;

            if (reportData.Count > 0)
            {
                ModernUIHelper.ShowModernSuccess(
                    $"Report generated successfully!\nFound {reportData.Count} movement records.");
            }
            else
            {
                ModernUIHelper.ShowModernWarning("No movement records found for the selected criteria.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating movement report");
            ModernUIHelper.ShowModernError($"Error generating report: {ex.Message}");
            lblStatus.Text = "Error generating report";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        try
        {
            if (dgvMovements.DataSource == null)
            {
                ModernUIHelper.ShowModernError("No data to export. Please generate a report first.");
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                FileName = $"Movement_Report_{_clock.UtcNow:yyyyMMdd_HHmmss}.csv",
                Title = "Export Movement Report"
            };

            if (saveDialog.ShowDialog() == DialogResult.OK)
            {
                ExportToCsv(saveDialog.FileName);
                ModernUIHelper.ShowModernSuccess($"Report exported successfully to:\n{saveDialog.FileName}");

                // Ask if user wants to open the file
                var result = MessageBox.Show("Would you like to open the exported file?", "Export Complete",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = saveDialog.FileName,
                        UseShellExecute = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting report");
            ModernUIHelper.ShowModernError($"Error exporting report: {ex.Message}");
        }
    }

    private void BtnClear_Click(object? sender, EventArgs e)
    {
        dgvMovements.DataSource = null;
        txtItemSku.Clear();
        cmbMovementType.SelectedIndex = 0;
        var today = GetBusinessToday();
        dtpFromDate.Value = today.AddDays(-_settings.Reports.DefaultPeriodDays);
        dtpToDate.Value = today;
        btnExport.Enabled = false;
        lblStatus.Text = "Ready";
    }

    private void ReportsForm_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.F1:
                btnGenerateReport.PerformClick();
                break;
            case Keys.F2:
                if (btnExport.Enabled)
                    btnExport.PerformClick();
                break;
            case Keys.Escape:
                Close();
                break;
        }
    }

    private MovementType? GetSelectedMovementType()
    {
        return cmbMovementType.SelectedIndex switch
        {
            1 => MovementType.Receipt,
            2 => MovementType.Putaway,
            3 => MovementType.Pick,
            4 => MovementType.Adjustment,
            _ => null
        };
    }

    private void ConfigureGridColumns()
    {
        if (dgvMovements?.Columns == null || dgvMovements.Columns.Count == 0) return;

        try
        {
            SetColumnPropertySafely("Id", col =>
            {
                col.HeaderText = "ID";
                col.Width = 60;
            });
            SetColumnPropertySafely("Type", col =>
            {
                col.HeaderText = "Type";
                col.Width = 80;
            });
            SetColumnPropertySafely("ItemSku", col =>
            {
                col.HeaderText = "Item SKU";
                col.Width = 100;
            });
            SetColumnPropertySafely("ItemName", col =>
            {
                col.HeaderText = "Item Name";
                col.Width = 150;
            });
            SetColumnPropertySafely("FromLocationCode", col =>
            {
                col.HeaderText = "From";
                col.Width = 80;
            });
            SetColumnPropertySafely("ToLocationCode", col =>
            {
                col.HeaderText = "To";
                col.Width = 80;
            });
            SetColumnPropertySafely("Quantity", col =>
            {
                col.HeaderText = "Quantity";
                col.Width = 80;
                col.DefaultCellStyle.Format = "N2";
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                col.DefaultCellStyle.ForeColor = ModernUIHelper.Colors.Primary;
            });
            SetColumnPropertySafely("LotNumber", col =>
            {
                col.HeaderText = "Lot";
                col.Width = 80;
            });
            SetColumnPropertySafely("SerialNumber", col =>
            {
                col.HeaderText = "Serial";
                col.Width = 100;
            });
            SetColumnPropertySafely("UserId", col =>
            {
                col.HeaderText = "User";
                col.Width = 80;
            });
            SetColumnPropertySafely("ReferenceNumber", col =>
            {
                col.HeaderText = "Reference";
                col.Width = 100;
            });
            SetColumnPropertySafely("Notes", col =>
            {
                col.HeaderText = "Notes";
                col.Width = 150;
            });
            SetColumnPropertySafely("Timestamp", col =>
            {
                col.HeaderText = "Date/Time";
                col.Width = 130;
                col.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
                col.DefaultCellStyle.ForeColor = ModernUIHelper.Colors.TextSecondary;
            });

            dgvMovements.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring grid columns");
        }
    }

    // Helper method for safe column configuration
    private void SetColumnPropertySafely(string columnName, Action<DataGridViewColumn> configureAction)
    {
        try
        {
            if (dgvMovements?.Columns?.Contains(columnName) == true)
            {
                var column = dgvMovements.Columns[columnName];
                if (column != null)
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

    private void ExportToCsv(string fileName)
    {
        if (dgvMovements.DataSource is not IEnumerable<MovementReportDto> reportData)
        {
            return;
        }

        var csv = new StringBuilder();
        csv.AppendLine(
            "ID,Type,ItemSku,ItemName,FromLocation,ToLocation,Quantity,LotNumber,SerialNumber,UserId,ReferenceNumber,Notes,Timestamp");

        foreach (var row in reportData)
        {
            csv.AppendLine(
                CultureInfo.InvariantCulture,
                $"{row.Id},{row.Type},{row.ItemSku},\"{row.ItemName}\",{row.FromLocationCode},{row.ToLocationCode},{row.Quantity},{row.LotNumber},{row.SerialNumber},{row.UserId},{row.ReferenceNumber},\"{row.Notes}\",{row.Timestamp:yyyy-MM-dd HH:mm:ss}");
        }

        File.WriteAllText(fileName, csv.ToString());
    }

    private void SetBusy(bool isBusy)
    {
        Cursor = isBusy ? Cursors.WaitCursor : Cursors.Default;
        btnGenerateReport.Enabled = !isBusy;
        btnExport.Enabled = !isBusy && dgvMovements.DataSource != null;
        dgvMovements.Enabled = !isBusy;
    }
}

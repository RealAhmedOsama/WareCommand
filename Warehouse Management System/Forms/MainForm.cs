// Wms.WinForms/Forms/MainForm.cs

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wms.WinForms.Common;

namespace Wms.WinForms.Forms;

public partial class MainForm : Form
{
    private readonly ILogger<MainForm> _logger;
    private readonly IServiceProvider _serviceProvider;
    private Form? _currentChildForm;

    public MainForm(ILogger<MainForm> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        InitializeComponent();
        Wms.WinForms.Common.WmsDesktopLocalization.Apply(this);
        SetupEventHandlers();
        SetupForm();
    }

    private void SetupEventHandlers()
    {
        btnDashboard.Click += BtnDashboard_Click;
        btnReceiving.Click += BtnReceiving_Click;
        btnPutaway.Click += BtnPutaway_Click;
        btnPicking.Click += BtnPicking_Click;
        btnInventory.Click += BtnInventory_Click;
        btnItemManagement.Click += BtnItemManagement_Click;
        btnLocationManagement.Click += BtnLocationManagement_Click;
        btnReports.Click += BtnReports_Click;
        KeyDown += MainForm_KeyDown;
    }

    private void SetupForm()
    {
        ModernUIHelper.StyleForm(this);
        KeyPreview = true;

        // Apply modern styling to buttons
        ModernUIHelper.StylePrimaryButton(btnDashboard);
        ModernUIHelper.StyleSuccessButton(btnReceiving);
        ModernUIHelper.StyleInfoButton(btnPutaway);
        ModernUIHelper.StyleDangerButton(btnPicking);
        ModernUIHelper.StyleWarningButton(btnInventory);
        ModernUIHelper.StyleSecondaryButton(btnItemManagement);
        ModernUIHelper.StyleSecondaryButton(btnLocationManagement);
        ModernUIHelper.StyleSecondaryButton(btnReports);

        // Style navigation panels
        ModernUIHelper.StyleCard(pnlOperations);
        ModernUIHelper.StyleCard(pnlManagement);

        ShowDashboard();
    }

    private void ShowChildFormInPanel<T>() where T : Form
    {
        try
        {
            // Clear current child form
            ClearContentPanel();

            // Create new child form
            var childForm = _serviceProvider.GetRequiredService<T>();

            // Configure form for embedding in panel
            childForm.TopLevel = false;
            childForm.FormBorderStyle = FormBorderStyle.None;
            childForm.Dock = DockStyle.Fill;
            childForm.WindowState = FormWindowState.Normal;

            // Add to content panel
            pnlContent.Controls.Add(childForm);
            childForm.Show();
            _currentChildForm = childForm;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing child form {FormType}", typeof(T).Name);
            ModernUIHelper.ShowModernError($"Error opening {typeof(T).Name}: {ex.Message}");
        }
    }

    private void ShowDialogForm<T>() where T : Form
    {
        try
        {
            var dialogForm = _serviceProvider.GetRequiredService<T>();
            dialogForm.StartPosition = FormStartPosition.CenterParent;
            dialogForm.ShowDialog(this);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing dialog form {FormType}", typeof(T).Name);
            ModernUIHelper.ShowModernError($"Error opening {typeof(T).Name}: {ex.Message}");
        }
    }

    private void ClearContentPanel()
    {
        if (_currentChildForm != null)
        {
            _currentChildForm.Hide();
            pnlContent.Controls.Remove(_currentChildForm);
            _currentChildForm.Dispose();
            _currentChildForm = null;
        }
    }

    private void ShowDashboard()
    {
        ShowChildFormInPanel<DashboardForm>();
        lblStatus.Text = WmsDesktopLocalization.Get("Desktop.DashboardStatus");
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.F1:
                btnDashboard.PerformClick();
                break;
            case Keys.F2:
                btnReceiving.PerformClick();
                break;
            case Keys.F3:
                btnPutaway.PerformClick();
                break;
            case Keys.F4:
                btnPicking.PerformClick();
                break;
            case Keys.F5:
                btnInventory.PerformClick();
                break;
            case Keys.F6:
                btnItemManagement.PerformClick();
                break;
            case Keys.F7:
                btnLocationManagement.PerformClick();
                break;
            case Keys.F8:
                btnReports.PerformClick();
                break;
            case Keys.Escape:
                Close();
                break;
        }
    }

    private void BtnDashboard_Click(object? sender, EventArgs e)
    {
        ShowDashboard();
    }

    private void BtnReceiving_Click(object? sender, EventArgs e)
    {
        ShowDialogForm<ReceivingForm>();
        lblStatus.Text = WmsDesktopLocalization.Get("Desktop.ReceivingStatus");
    }

    private void BtnPutaway_Click(object? sender, EventArgs e)
    {
        ShowDialogForm<PutawayForm>();
        lblStatus.Text = WmsDesktopLocalization.Get("Desktop.PutawayStatus");
    }

    private void BtnInventory_Click(object? sender, EventArgs e)
    {
        ShowChildFormInPanel<InventoryForm>();
        lblStatus.Text = WmsDesktopLocalization.Get("Desktop.InventoryStatus");
    }

    private void BtnPicking_Click(object? sender, EventArgs e)
    {
        ShowDialogForm<PickingForm>();
        lblStatus.Text = WmsDesktopLocalization.Get("Desktop.PickingStatus");
    }

    private void BtnItemManagement_Click(object? sender, EventArgs e)
    {
        ShowChildFormInPanel<ItemManagementForm>();
        lblStatus.Text = WmsDesktopLocalization.Get("Desktop.ItemsStatus");
    }

    private void BtnLocationManagement_Click(object? sender, EventArgs e)
    {
        ShowChildFormInPanel<LocationManagementForm>();
        lblStatus.Text = WmsDesktopLocalization.Get("Desktop.LocationsStatus");
    }

    private void BtnReports_Click(object? sender, EventArgs e)
    {
        ShowDialogForm<ReportsForm>();
        lblStatus.Text = WmsDesktopLocalization.Get("Desktop.ReportsStatus");
    }
}

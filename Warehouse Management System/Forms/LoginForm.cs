using Microsoft.Extensions.Logging;
using Wms.Application.Logging;
using Wms.Infrastructure.Identity;
using Wms.WinForms.Common;

namespace Wms.WinForms.Forms;

public sealed class LoginForm : Form
{
    private readonly IDesktopAuthenticationService _authenticationService;
    private readonly ILogger<LoginForm> _logger;
    private readonly Button _signInButton = new();
    private readonly Label _errorLabel = new();
    private readonly TextBox _passwordTextBox = new();
    private readonly TextBox _userNameTextBox = new();

    public LoginForm(
        IDesktopAuthenticationService authenticationService,
        ILogger<LoginForm> logger)
    {
        _authenticationService = authenticationService;
        _logger = logger;
        Text = $"{WmsDesktopLocalization.Get("App.ShortName")} - {WmsDesktopLocalization.Get("Account.SignIn")}";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(440, 300);

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            Text = WmsDesktopLocalization.Get("Account.SignInTitle")
        };
        var instructionLabel = new Label
        {
            AutoSize = true,
            Text = WmsDesktopLocalization.Get("Account.SignInHelp")
        };

        _userNameTextBox.Dock = DockStyle.Fill;
        _userNameTextBox.AccessibleName = WmsDesktopLocalization.Get("Account.UsernameOrEmail");
        _passwordTextBox.Dock = DockStyle.Fill;
        _passwordTextBox.AccessibleName = WmsDesktopLocalization.Get("Account.Password");
        _passwordTextBox.UseSystemPasswordChar = true;
        _signInButton.Text = WmsDesktopLocalization.Get("Account.SignIn");
        _signInButton.AutoSize = true;
        _signInButton.Click += SignInButton_Click;
        AcceptButton = _signInButton;

        _errorLabel.AutoSize = true;
        _errorLabel.ForeColor = Color.Firebrick;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(24)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(titleLabel, 0, 0);
        layout.SetColumnSpan(titleLabel, 2);
        layout.Controls.Add(instructionLabel, 0, 1);
        layout.SetColumnSpan(instructionLabel, 2);
        layout.Controls.Add(new Label { Text = WmsDesktopLocalization.Get("Account.UsernameOrEmail"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(_userNameTextBox, 1, 3);
        layout.Controls.Add(new Label { Text = WmsDesktopLocalization.Get("Account.Password"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        layout.Controls.Add(_passwordTextBox, 1, 4);
        layout.Controls.Add(_errorLabel, 0, 5);
        layout.SetColumnSpan(_errorLabel, 2);
        layout.Controls.Add(_signInButton, 1, 6);
        Controls.Add(layout);
        WmsDesktopLocalization.Apply(this);
    }

    private async void SignInButton_Click(object? sender, EventArgs e)
    {
        _errorLabel.Text = string.Empty;
        _signInButton.Enabled = false;

        try
        {
            var result = await _authenticationService.AuthenticateAsync(
                _userNameTextBox.Text.Trim(),
                _passwordTextBox.Text);
            if (result.Succeeded)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            _errorLabel.Text = result.Message switch
            {
                "Invalid username or password." => WmsDesktopLocalization.Get("Account.InvalidCredentials"),
                "This account is disabled." => WmsDesktopLocalization.Get("Account.AccessDenied"),
                "This account is temporarily locked." => WmsDesktopLocalization.Get("Account.Locked"),
                _ => result.Message
            };
            _passwordTextBox.SelectAll();
            _passwordTextBox.Focus();
        }
        catch (Exception exception)
        {
            _errorLabel.Text = WmsDesktopLocalization.Get("Account.SignInFailed");
            _logger.LogError(
                WmsLogEvents.AuthenticationFailed,
                exception,
                "Desktop sign-in failed");
        }
        finally
        {
            _signInButton.Enabled = true;
        }
    }
}

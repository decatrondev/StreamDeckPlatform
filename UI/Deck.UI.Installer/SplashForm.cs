namespace Deck.UI.Installer;

// Toda la "marca propia" del instalador vive acá: una ventanita simple,
// sin chrome de Windows, colores de Flowdeck (mismos tokens que
// FlowdeckTheme.axaml en la app principal), sin nada que el usuario tenga
// que hacer clic — se cierra sola cuando termina.
internal sealed class SplashForm : Form
{
    private static readonly Color Graphite = Color.FromArgb(0x14, 0x17, 0x1C);
    private static readonly Color Ink = Color.FromArgb(0xE7, 0xEA, 0xEE);
    private static readonly Color InkMuted = Color.FromArgb(0x8A, 0x94, 0xA6);
    private static readonly Color Accent = Color.FromArgb(0x25, 0x63, 0xEB);
    private static readonly Color Danger = Color.FromArgb(0xEF, 0x44, 0x44);

    private readonly Label _statusLabel;
    private readonly ProgressBar _progress;

    public SplashForm()
    {
        Text = "Flowdeck";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(360, 160);
        BackColor = Graphite;
        ShowInTaskbar = true;
        TopMost = true;

        var title = new Label
        {
            Text = "Flowdeck",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Ink,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(24, 28),
            Size = new Size(300, 30),
        };

        _statusLabel = new Label
        {
            Text = "Preparando instalación…",
            Font = new Font("Segoe UI", 9),
            ForeColor = InkMuted,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(24, 64),
            Size = new Size(312, 24),
        };

        _progress = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30,
            Location = new Point(24, 100),
            Size = new Size(312, 8),
            ForeColor = Accent,
        };

        Controls.Add(title);
        Controls.Add(_statusLabel);
        Controls.Add(_progress);
    }

    public void SetStatus(string text)
    {
        if (InvokeRequired) { BeginInvoke(SetStatus, text); return; }
        _statusLabel.Text = text;
    }

    public void ShowError(string message)
    {
        if (InvokeRequired) { BeginInvoke(ShowError, message); return; }
        _progress.Style = ProgressBarStyle.Blocks;
        _progress.Value = 0;
        _statusLabel.ForeColor = Danger;
        _statusLabel.Text = message;
        ClientSize = new Size(360, 190);

        var closeButton = new Button
        {
            Text = "Cerrar",
            Location = new Point(248, 140),
            Size = new Size(88, 30),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Ink,
            BackColor = Color.FromArgb(0x24, 0x2A, 0x35),
        };
        closeButton.Click += (_, _) => Close();
        Controls.Add(closeButton);
    }

    public void CloseFromBackground()
    {
        if (InvokeRequired) { BeginInvoke(CloseFromBackground); return; }
        Close();
    }
}

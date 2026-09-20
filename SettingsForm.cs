using System.Diagnostics;
using System.Drawing;

namespace AudioFollow;

sealed class SettingsForm : Form
{
    static readonly (string Code, string Name)[] Storefronts =
    [
        ("us", "United States"), ("gb", "United Kingdom"), ("ca", "Canada"), ("au", "Australia"),
        ("nz", "New Zealand"), ("ie", "Ireland"), ("de", "Germany"), ("fr", "France"),
        ("es", "Spain"), ("it", "Italy"), ("nl", "Netherlands"), ("be", "Belgium"),
        ("at", "Austria"), ("ch", "Switzerland"), ("se", "Sweden"), ("no", "Norway"),
        ("dk", "Denmark"), ("fi", "Finland"), ("pl", "Poland"), ("pt", "Portugal"),
        ("br", "Brazil"), ("mx", "Mexico"), ("ar", "Argentina"), ("cl", "Chile"),
        ("co", "Colombia"), ("jp", "Japan"), ("kr", "South Korea"), ("tw", "Taiwan"),
        ("hk", "Hong Kong"), ("sg", "Singapore"), ("in", "India"), ("za", "South Africa"),
    ];

    readonly RadioButton _resumeOff = new() { Text = "Off", AutoSize = true };
    readonly RadioButton _resumeAlbum = new() { Text = "Reopen the album (nothing plays)", AutoSize = true };
    readonly RadioButton _resumePlay = new() { Text = "Reopen the album and play the song", AutoSize = true };
    readonly CheckBox _startup = new() { Text = "Start with Windows", AutoSize = true };
    readonly CheckBox _debug = new() { Text = "Write a debug log (audio-follow.log)", AutoSize = true };
    readonly ComboBox _storefront = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    readonly NumericUpDown _wait = new() { Minimum = 500, Maximum = 10000, Increment = 100, Width = 80 };
    readonly Config _current;

    public Config Result { get; private set; }

    public SettingsForm(Config current, bool startupEnabled, string configPath, Icon icon)
    {
        _current = current;
        Result = current;

        Text = "audio-follow settings";
        Icon = icon;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);

        _resumeOff.Checked = !current.ResumeAlbum && !current.ResumePlayback;
        _resumePlay.Checked = current.ResumePlayback;
        _resumeAlbum.Checked = current.ResumeAlbum && !current.ResumePlayback;
        _startup.Checked = startupEnabled;
        _debug.Checked = current.Debug;
        _wait.Value = Math.Clamp(current.DebounceMs, (int)_wait.Minimum, (int)_wait.Maximum);

        foreach (var (code, name) in Storefronts) _storefront.Items.Add($"{name} ({code})");
        var idx = Array.FindIndex(Storefronts, s => s.Code == current.Storefront.ToLowerInvariant());
        if (idx < 0) { _storefront.Items.Add($"({current.Storefront})"); idx = _storefront.Items.Count - 1; }
        _storefront.SelectedIndex = idx;

        var resumeNote = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Text = "Apple Music forgets what was playing when it restarts. Resume looks the song up through " +
                   "Apple's public search service, so the song, artist and album are sent to Apple. " +
                   "Playing the song briefly moves the mouse pointer and brings Apple Music to the front.",
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 6, 3, 3)
        };

        var resumeFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 6)
        };
        resumeFlow.Controls.AddRange([_resumeOff, _resumeAlbum, _resumePlay, resumeNote]);

        var resumeGroup = new GroupBox
        {
            Text = "After switching audio output (Apple Music)", AutoSize = true, Dock = DockStyle.Fill
        };
        resumeGroup.Controls.Add(resumeFlow);

        var storeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        storeRow.Controls.Add(new Label { Text = "Storefront (for song lookup):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 0) });
        storeRow.Controls.Add(_storefront);

        var waitRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        waitRow.Controls.Add(new Label { Text = "Wait after a device change:", AutoSize = true, Margin = new Padding(3, 6, 6, 0) });
        waitRow.Controls.Add(_wait);
        waitRow.Controls.Add(new Label { Text = "ms", AutoSize = true, Margin = new Padding(3, 6, 0, 0) });

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(80, 0) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(80, 0) };
        var edit = new Button { Text = "Edit config file…", AutoSize = true };
        edit.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(configPath) { UseShellExecute = true }); }
            catch { }
        };
        ok.Click += (_, _) => Result = Build();

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0)
        };
        buttons.Controls.AddRange([cancel, ok, edit]);

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true
        };
        layout.Controls.AddRange([resumeGroup, storeRow, waitRow, _startup, _debug, buttons]);
        _startup.Margin = new Padding(3, 12, 3, 3);
        buttons.Width = 460;

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    Config Build()
    {
        var code = Storefronts.Length > _storefront.SelectedIndex && _storefront.SelectedIndex >= 0
            ? Storefronts[_storefront.SelectedIndex].Code
            : _current.Storefront;

        return _current with
        {
            ResumeAlbum = _resumeAlbum.Checked || _resumePlay.Checked,
            ResumePlayback = _resumePlay.Checked,
            Storefront = code,
            DebounceMs = (int)_wait.Value,
            Debug = _debug.Checked,
            StartWithWindows = _startup.Checked
        };
    }
}

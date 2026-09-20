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

    const int ContentWidth = 440;

    readonly RadioButton _resumeOff = new() { Text = "Off", AutoSize = true };
    readonly RadioButton _resumeAlbum = new() { Text = "Reopen the album", AutoSize = true };
    readonly RadioButton _resumePlay = new() { Text = "Reopen the album and play the song", AutoSize = true };
    readonly CheckBox _startup = new() { Text = "Start with Windows", AutoSize = true };
    readonly CheckBox _debug = new() { Text = "Write a debug log", AutoSize = true };
    readonly ComboBox _storefront = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    readonly NumericUpDown _wait = new() { Minimum = 500, Maximum = 10000, Increment = 100, Width = 90 };
    readonly ToolTip _tip = new() { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 30000, ShowAlways = true };
    readonly Config _current;

    public Config Result { get; private set; }

    public SettingsForm(Config current, bool startupEnabled, string configPath, Icon icon)
    {
        _current = current;
        Result = current;

        Font = new Font("Segoe UI", 10f);
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "audio-follow settings";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(28, 24, 28, 22);

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

        var headerFont = new Font(Font.FontFamily, 12f, FontStyle.Bold);
        var noteFont = new Font(Font.FontFamily, 9f);

        Label Header(string text) => new()
        {
            Text = text, AutoSize = true, Font = headerFont, Margin = new Padding(0, 0, 0, 4)
        };
        Label Note(string text) => new()
        {
            Text = text, AutoSize = true, Font = noteFont, ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(ContentWidth, 0), Margin = new Padding(0, 0, 0, 10)
        };
        Label Divider() => new()
        {
            AutoSize = false, Height = 1, BackColor = SystemColors.ControlDark,
            Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 18, 0, 18)
        };
        Label FieldLabel(string text) => new()
        {
            Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 16, 6)
        };

        foreach (var radio in new[] { _resumeOff, _resumeAlbum, _resumePlay })
            radio.Margin = new Padding(4, 3, 3, 3);
        _startup.Margin = new Padding(4, 10, 3, 3);
        _debug.Margin = new Padding(4, 3, 3, 3);

        var storeLabel = FieldLabel("Storefront");
        var waitLabel = FieldLabel("Wait after device change");
        _storefront.Margin = new Padding(0, 3, 0, 3);
        _wait.Margin = new Padding(0, 3, 6, 3);
        var msLabel = new Label { Text = "ms", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6) };

        var waitFlow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        waitFlow.Controls.AddRange([_wait, msLabel]);

        var fields = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.Controls.Add(storeLabel, 0, 0);
        fields.Controls.Add(_storefront, 1, 0);
        fields.Controls.Add(waitLabel, 0, 1);
        fields.Controls.Add(waitFlow, 1, 1);

        var ok = new Button
        {
            Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(96, 34),
            FlatStyle = FlatStyle.Flat, BackColor = SystemColors.Highlight, ForeColor = SystemColors.HighlightText,
            Margin = new Padding(8, 0, 0, 0)
        };
        ok.FlatAppearance.BorderSize = 0;
        var cancel = new Button
        {
            Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(96, 34),
            FlatStyle = FlatStyle.Flat, Margin = new Padding(0)
        };
        cancel.FlatAppearance.BorderColor = SystemColors.ControlDark;
        ok.Click += (_, _) => Result = Build();

        var dark = SystemColors.Control.GetBrightness() < 0.5f;
        var linkColor = dark ? Color.FromArgb(120, 184, 255) : Color.FromArgb(0, 102, 204);
        var edit = new LinkLabel
        {
            Text = "Edit config file…", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0),
            LinkColor = linkColor, ActiveLinkColor = linkColor, VisitedLinkColor = linkColor
        };
        edit.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(configPath) { UseShellExecute = true }); }
            catch { }
        };

        var buttonFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Margin = new Padding(0)
        };
        buttonFlow.Controls.AddRange([cancel, ok]);

        var bottom = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0), Width = ContentWidth };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(edit, 0, 0);
        bottom.Controls.Add(buttonFlow, 1, 0);

        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Add(Control c) => layout.Controls.Add(c);

        Add(Header("Resume playback"));
        Add(Note("After Apple Music restarts to pick up the new output, bring back what was playing."));
        Add(_resumeOff);
        Add(_resumeAlbum);
        Add(_resumePlay);
        var privacy = Note("Both reopen options find the song through Apple's public search service, " +
                           "so its title, artist and album are sent to Apple.");
        privacy.Margin = new Padding(4, 8, 0, 0);
        Add(privacy);
        Add(Divider());
        Add(Header("General"));
        Add(fields);
        Add(_startup);
        Add(_debug);
        Add(Divider());
        Add(bottom);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;

        AddTip("Apple Music starts empty after the restart, exactly as it does normally.", _resumeOff);
        AddTip("After the restart, open the album page of the song that was playing. Nothing starts playing.",
               _resumeAlbum);
        AddTip("Open the album page, then start that song from the beginning by double-clicking it.\n" +
               "Briefly moves the mouse pointer and brings Apple Music to the front, so don't click in the window meanwhile.\n" +
               "Only plays if music was actually playing (not paused).",
               _resumePlay);
        AddTip("The country catalog used to find the song after the restart.\n" +
               "Pick the region of your Apple Music account so the song is found.", storeLabel, _storefront);
        AddTip("How long to wait after the output device changes before restarting.\n" +
               "Bluetooth devices often report several changes in a row; a longer wait avoids repeated restarts.\n" +
               "Default: 2500 ms.", waitLabel, _wait, msLabel);
        AddTip("Launch audio-follow automatically when you sign in to Windows.", _startup);
        AddTip("Record each step of a restart and resume in audio-follow.log next to the app.\n" +
               "Handy for troubleshooting; leave off otherwise.", _debug);
        AddTip("Open config.json to change which apps get restarted.\n" +
               "Restart audio-follow afterwards for that change to apply.", edit);
    }

    void AddTip(string text, params Control[] controls)
    {
        foreach (var c in controls) _tip.SetToolTip(c, text);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tip.Dispose();
        base.OnFormClosed(e);
    }

    Config Build()
    {
        var code = _storefront.SelectedIndex >= 0 && _storefront.SelectedIndex < Storefronts.Length
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

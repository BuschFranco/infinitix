namespace ShooterLoop;

using Godot;

public partial class CharacterCreator : Control
{
    [Signal]
    public delegate void CharacterCreatedEventHandler(string slug);

    private LineEdit _nameEdit;
    private TextEdit _descEdit;
    private TextureRect _preview;
    private Label _errorLabel;
    private FileDialog _fileDialog;
    private Button _saveButton;

    private string _pickedImagePath = "";
    private Timer _galleryPollTimer;
    private PanelContainer _panel;

    public override void _Ready()
    {
        _panel = GetNode<PanelContainer>("CenterContainer/Panel");
        _nameEdit = GetNode<LineEdit>("CenterContainer/Panel/Box/NameEdit");
        _descEdit = GetNode<TextEdit>("CenterContainer/Panel/Box/DescEdit");
        _preview = GetNode<TextureRect>("CenterContainer/Panel/Box/Top/Swatch/PreviewTexture");
        _errorLabel = GetNode<Label>("CenterContainer/Panel/Box/ErrorLabel");
        _fileDialog = GetNode<FileDialog>("FileDialog");
        _saveButton = GetNode<Button>("CenterContainer/Panel/Box/SaveButton");

        var imageButton = GetNode<Button>("CenterContainer/Panel/Box/Top/Side/ImageButton");
        var cancelButton = GetNode<Button>("CenterContainer/Panel/Box/CancelButton");
        imageButton.Pressed += OnImageButtonPressed;
        _saveButton.Pressed += Save;
        cancelButton.Pressed += Close;
        UIUtil.WireDimToClose(GetNode<Control>("Dim"), Close);

        foreach (var b in new[] { imageButton, _saveButton, cancelButton })
            Juice.WireButtonFeedback(b);

        _fileDialog.FileSelected += OnFileSelected;

        _nameEdit.MaxLength = CustomCharacterStore.MaxNameLength;

        if (OS.HasFeature("android"))
        {
            _galleryPollTimer = new Timer();
            _galleryPollTimer.WaitTime = 0.15;
            _galleryPollTimer.OneShot = true;
            _galleryPollTimer.Timeout += OnGalleryPoll;
            AddChild(_galleryPollTimer);
        }
    }

    public override void _ExitTree()
    {
        if (_galleryPollTimer != null)
        {
            _galleryPollTimer.Timeout -= OnGalleryPoll;
            _galleryPollTimer.QueueFree();
            _galleryPollTimer = null;
        }
    }

    public void Open()
    {
        _nameEdit.Text = "";
        _descEdit.Text = "";
        _pickedImagePath = "";
        _preview.Texture = null;
        ShowError("");
        Visible = true;
        Juice.ModalIn(_panel);
        _nameEdit.GrabFocus();

        GameManager.Instance?.PushBackHandler(this, Close);
    }

    private void Close()
    {
        GameManager.Instance?.PopBackHandler(this);
        Juice.ModalOut(_panel, () => Visible = false);
    }

    private void OnImageButtonPressed()
    {
        if (OS.HasFeature("android"))
        {
            RequestNativeGallery();
        }
        else
        {
            _fileDialog.PopupCentered();
        }
    }

    private void RequestNativeGallery()
    {
        using var flag = FileAccess.Open("user://.gallery_request", FileAccess.ModeFlags.Write);
        if (flag == null)
        {
            ShowError("No pude abrir la galería.");
            return;
        }
        _galleryPollTimer.Start();
    }

    private void OnGalleryPoll()
    {
        using var ready = FileAccess.Open("user://.gallery_ready", FileAccess.ModeFlags.Read);
        if (ready == null)
        {
            _galleryPollTimer.Start();
            return;
        }

        string readyText = ready.GetAsText().Trim();
        ready.Close();
        using (var dir = DirAccess.Open("user://"))
            dir?.Remove(".gallery_ready");

        if (readyText == "cancelled" || readyText == "failed")
        {
            if (readyText == "failed")
                ShowError("No pude cargar la imagen.");
            return;
        }

        const string godotPath = "user://gallery_pick.png";
        var image = Image.LoadFromFile(godotPath);

        if (image == null)
        {
            byte[] bytes = null;
            try { bytes = System.IO.File.ReadAllBytes(readyText); }
            catch { }

            if (bytes != null && bytes.Length > 0)
            {
                string tmp = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "sholoop_g.png");
                System.IO.File.WriteAllBytes(tmp, bytes);
                image = Image.LoadFromFile(tmp);
                try { System.IO.File.Delete(tmp); } catch { }
            }
        }

        if (image == null)
        {
            ShowError("No pude leer esa imagen.");
            return;
        }

        _pickedImagePath = godotPath;
        _preview.Texture = ImageTexture.CreateFromImage(image);
        ShowError("");
    }

    private void OnFileSelected(string path)
    {
        var image = Image.LoadFromFile(path);
        if (image == null)
        {
            ShowError("No pude leer esa imagen.");
            return;
        }

        _pickedImagePath = path;
        _preview.Texture = ImageTexture.CreateFromImage(image);
        ShowError("");
    }

    private void Save()
    {
        string name = CustomCharacterStore.Sanitise(_nameEdit.Text);
        string description = CustomCharacterStore.Sanitise(_descEdit.Text);

        string slug = CustomCharacterStore.Create(name, description, _pickedImagePath, out string error);
        if (slug == null)
        {
            ShowError(error);
            return;
        }

        Close();
        EmitSignal(SignalName.CharacterCreated, slug);
    }

    // A shake + red flash instead of a bare Visible toggle — this is specifically feedback for a
    // failed validation ("that name's taken", "pick an image first"), so it should read as "that
    // didn't work" rather than just quietly changing text.
    private void ShowError(string message)
    {
        _errorLabel.Text = message;
        _errorLabel.Visible = message.Length > 0;
        if (message.Length > 0)
            Juice.Shake(_errorLabel, 6f, 0.3f, new Color(1f, 0.3f, 0.35f));
    }
}

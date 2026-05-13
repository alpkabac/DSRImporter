#if TOOLS
using Godot;
using System;
using System.IO;
using System.Collections.Generic;

namespace DSRImporter
{
    /// <summary>
    /// The dock UI panel visible in the Godot editor.
    /// Usage flow:
    ///   1. Set DSR game path (the folder containing "map", "chr", "obj" etc.)
    ///   2. For maps: load all required TPF texture archives first (one by one, unfortunately)
    ///   3. Load a FLVER or a map BND to place meshes in scene
    /// </summary>
    [Tool]
    public partial class DSRImporterDock : Control
    {
        private LineEdit _gamePathEdit;
        private Label _statusLabel;
        private Button _loadTPFButton;
        private Button _loadFLVERButton;
        private Button _loadMapButton;
        private Label _loadedTexturesLabel;

        // Shared texture cache — TPFs must be loaded before map FLVERs that reference them
        private readonly Dictionary<string, Texture2D> _textureCache = new();

        public override void _Ready()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            var vbox = new VBoxContainer();
            AddChild(vbox);
            vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

            vbox.AddChild(new Label { Text = "DSR Importer", HorizontalAlignment = HorizontalAlignment.Center });
            vbox.AddChild(new HSeparator());

            // Game path
            vbox.AddChild(new Label { Text = "DSR Data Path:" });
            var pathRow = new HBoxContainer();
            vbox.AddChild(pathRow);
            _gamePathEdit = new LineEdit { PlaceholderText = "C:/SteamLibrary/.../DARKSOULS.exe/../", SizeFlagsHorizontal = SizeFlags.Expand };
            pathRow.AddChild(_gamePathEdit);
            var browseBtn = new Button { Text = "..." };
            browseBtn.Pressed += OnBrowsePressed;
            pathRow.AddChild(browseBtn);

            vbox.AddChild(new HSeparator());

            // Texture loading (must happen before map loads)
            var texSection = new Label { Text = "Step 1 — Load Textures", HorizontalAlignment = HorizontalAlignment.Center };
            vbox.AddChild(texSection);
            _loadedTexturesLabel = new Label { Text = "Loaded: 0 textures", AutowrapMode = TextServer.AutowrapMode.Word };
            vbox.AddChild(_loadedTexturesLabel);
            _loadTPFButton = new Button { Text = "Load TPF / tpfbhd+tpfbdt" };
            _loadTPFButton.Pressed += OnLoadTPFPressed;
            vbox.AddChild(_loadTPFButton);

            vbox.AddChild(new HSeparator());

            // Mesh loading
            var meshSection = new Label { Text = "Step 2 — Import Mesh", HorizontalAlignment = HorizontalAlignment.Center };
            vbox.AddChild(meshSection);
            _loadFLVERButton = new Button { Text = "Import FLVER file" };
            _loadFLVERButton.Pressed += OnLoadFLVERPressed;
            vbox.AddChild(_loadFLVERButton);

            _loadMapButton = new Button { Text = "Import Map Layout (MSB)" };
            _loadMapButton.Pressed += OnLoadMapPressed;
            vbox.AddChild(_loadMapButton);

            vbox.AddChild(new HSeparator());

            // Status
            _statusLabel = new Label
            {
                Text = "Ready.",
                AutowrapMode = TextServer.AutowrapMode.Word,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            vbox.AddChild(_statusLabel);
        }

        // ── File dialogs ─────────────────────────────────────────────────────────

        private void OnBrowsePressed()
        {
            var dialog = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenDir,
                Access = FileDialog.AccessEnum.Filesystem,
                Title = "Select DSR data folder"
            };
            AddChild(dialog);
            dialog.DirSelected += path => { _gamePathEdit.Text = path; dialog.QueueFree(); };
            dialog.PopupCentered(new Vector2I(600, 400));
        }

        private void OnLoadTPFPressed()
        {
            var dialog = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Title = "Select TPF or tpfbhd file",
                Filters = new[] { "*.tpf ; TPF archive", "*.tpf.dcx ; Compressed TPF", "*.tpfbhd ; Split TPF header" }
            };
            AddChild(dialog);
            dialog.FileSelected += path =>
            {
                dialog.QueueFree();
                LoadTextures(path);
            };
            dialog.PopupCentered(new Vector2I(700, 500));
        }

        private void OnLoadFLVERPressed()
        {
            var dialog = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Title = "Select FLVER file",
                Filters = new[] { "*.flver ; FLVER model", "*.flver.dcx ; Compressed FLVER" }
            };
            AddChild(dialog);
            dialog.FileSelected += path =>
            {
                dialog.QueueFree();
                ImportFLVER(path);
            };
            dialog.PopupCentered(new Vector2I(700, 500));
        }

        private void OnLoadMapPressed()
        {
            var dialog = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Title = "Select MapStudio MSB file",
                Filters = new[] { "*.msb ; MapStudio map layout", "*.msb.dcx ; Compressed MapStudio map layout" }
            };
            AddChild(dialog);
            dialog.FileSelected += path =>
            {
                dialog.QueueFree();
                ImportMap(path);
            };
            dialog.PopupCentered(new Vector2I(700, 500));
        }

        // ── Logic ────────────────────────────────────────────────────────────────

        private void LoadTextures(string path)
        {
            try
            {
                SetStatus($"Loading textures from {System.IO.Path.GetFileName(path)}…");
                int count = TPFConverter.LoadIntoCache(path, _textureCache);
                _loadedTexturesLabel.Text = $"Loaded: {_textureCache.Count} textures (+{count} new)";
                SetStatus($"OK — {count} textures added.");
            }
            catch (Exception ex)
            {
                SetStatus($"ERROR loading textures:\n{ex.Message}");
                GD.PrintErr(ex);
            }
        }

        private void ImportFLVER(string path)
        {
            try
            {
                SetStatus($"Importing {System.IO.Path.GetFileName(path)}…");
                var node = FLVERConverter.ToMeshInstance(path, _textureCache);
                AddToScene(node);
                SetStatus($"Imported: {node.Name}");
            }
            catch (Exception ex)
            {
                SetStatus($"ERROR importing FLVER:\n{ex.Message}");
                GD.PrintErr(ex);
            }
        }

        private void ImportMap(string path)
        {
            try
            {
                SetStatus("Importing map layout... (this may take a while)");
                var options = new MapLoader.MapLoadOptions
                {
                    AutoLoadTextures = true,
                    LoadCollision = false,
                    HiResCollisionOnly = true,
                    ShowCollisionDebug = false
                };
                var root = MapLoader.LoadFromMSB(path, _textureCache, options);
                AddToScene(root);
                SetStatus($"Map imported: {CountMeshInstances(root)} mesh pieces.");
            }
            catch (Exception ex)
            {
                SetStatus($"ERROR importing map:\n{ex.Message}");
                GD.PrintErr(ex);
            }
        }

        private void AddToScene(Node3D node)
        {
            var editor = EditorInterface.Singleton;
            var scene = editor.GetEditedSceneRoot();
            if (scene == null)
            {
                SetStatus("ERROR: No scene open. Open or create a scene first.");
                return;
            }
            scene.AddChild(node);
            node.Owner = scene;
            // If it has children, set their owners too
            SetOwnerRecursive(node, scene);
        }

        private static void SetOwnerRecursive(Node node, Node owner)
        {
            foreach (Node child in node.GetChildren())
            {
                child.Owner = owner;
                SetOwnerRecursive(child, owner);
            }
        }

        private static int CountMeshInstances(Node node)
        {
            int count = node is MeshInstance3D ? 1 : 0;
            foreach (Node child in node.GetChildren())
                count += CountMeshInstances(child);
            return count;
        }

        private void SetStatus(string msg)
        {
            _statusLabel.Text = msg;
            GD.Print($"[DSRImporter] {msg}");
        }
    }
}
#endif

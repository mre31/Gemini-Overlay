using System;
using System.Windows;
using System.Windows.Forms;
using System.Drawing;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Gemeni.Services
{
    public class SystemTrayManager
    {
        private NotifyIcon _notifyIcon;
        private readonly MainWindow _mainWindow;
        private readonly Settings _settings;
        private readonly SettingsManager _settingsManager;

        public event EventHandler<string> ModelChanged;

        public SystemTrayManager(MainWindow mainWindow, Settings settings)
        {
            _mainWindow = mainWindow;
            _settings = settings;
            _settingsManager = new SettingsManager();
            InitializeNotifyIcon();
        }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }

        private void InitializeNotifyIcon()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            
            _notifyIcon = new NotifyIcon
            {
                Visible = true,
                Text = "Gemini Overlay"
            };
            
            _notifyIcon.Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            
            var contextMenu = new ContextMenuStrip();
            
            contextMenu.BackColor = Color.FromArgb(40, 40, 40);
            contextMenu.ForeColor = Color.White;
            
            contextMenu.Renderer = new DarkMenuRenderer();
            
            var showItem = new ToolStripMenuItem("Show");
            showItem.Click += (s, e) => _mainWindow.ShowOverlay();
            ApplyDarkStyleToMenuItem(showItem);
            contextMenu.Items.Add(showItem);
            
            var hideItem = new ToolStripMenuItem("Hide");
            hideItem.Click += (s, e) => _mainWindow.HideOverlay();
            ApplyDarkStyleToMenuItem(hideItem);
            contextMenu.Items.Add(hideItem);
            
            var separator1 = new ToolStripSeparator();
            separator1.Paint += (s, e) => CustomizeSeparator(separator1, e);
            contextMenu.Items.Add(separator1);
            
            var startupItem = new ToolStripMenuItem("Start with Windows");
            startupItem.Checked = _settings.StartWithWindows;
            startupItem.Click += (s, e) => ToggleStartWithWindows();
            ApplyDarkStyleToMenuItem(startupItem);
            contextMenu.Items.Add(startupItem);
            
            var modelMenu = new ToolStripMenuItem("Model Selection");
            ApplyDarkStyleToMenuItem(modelMenu);

            foreach (var model in _settings.AvailableModels)
            {
                var modelItem = new ToolStripMenuItem(model.Key);
                modelItem.Checked = model.Key == _settings.SelectedModelName;
                modelItem.Click += (s, e) => ChangeModel(model.Key);
                ApplyDarkStyleToMenuItem(modelItem);
                modelMenu.DropDownItems.Add(modelItem);
            }
            
            contextMenu.Items.Add(modelMenu);
            
            var separator2 = new ToolStripSeparator();
            separator2.Paint += (s, e) => CustomizeSeparator(separator2, e);
            contextMenu.Items.Add(separator2);
            
            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => ExitApplication();
            ApplyDarkStyleToMenuItem(exitItem);
            contextMenu.Items.Add(exitItem);
            
            _notifyIcon.ContextMenuStrip = contextMenu;
            
            _notifyIcon.DoubleClick += (s, e) => 
            {
                if (_mainWindow.IsVisible)
                {
                    _mainWindow.HideOverlay();
                }
                else
                {
                    _mainWindow.ShowOverlay();
                }
            };
        }

        private void ApplyDarkStyleToMenuItem(ToolStripMenuItem item)
        {
            item.BackColor = Color.FromArgb(40, 40, 40);
            item.ForeColor = Color.White;
            
            if (item.Checked)
            {
                item.Font = new Font(item.Font, System.Drawing.FontStyle.Bold);
            }
            
            foreach (ToolStripItem subItem in item.DropDownItems)
            {
                if (subItem is ToolStripMenuItem menuItem)
                {
                    ApplyDarkStyleToMenuItem(menuItem);
                }
                else if (subItem is ToolStripSeparator separator)
                {
                    separator.Paint += (s, e) => CustomizeSeparator(separator, e);
                }
            }
        }

        private void CustomizeSeparator(ToolStripSeparator separator, PaintEventArgs e)
        {
            Rectangle rect = new Rectangle(0, 0, separator.Width, separator.Height);
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(40, 40, 40)), rect);

            int y = rect.Height / 2;
            using (Pen pen = new Pen(Color.FromArgb(60, 60, 60)))
            {
                e.Graphics.DrawLine(pen, 4, y, rect.Width - 4, y);
            }
        }

        private class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkColorTable())
            {
                this.RoundedEdges = true;
            }
            
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected)
                {
                    base.OnRenderMenuItemBackground(e);
                    return;
                }
                
                Rectangle rect = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
                Color highlight = Color.FromArgb(60, 60, 60);
                e.Graphics.FillRectangle(new SolidBrush(highlight), rect);
                
                using (Pen pen = new Pen(highlight, 1))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }
            }
        }

        private class DarkColorTable : ProfessionalColorTable
        {
            public override Color MenuItemSelected => Color.FromArgb(60, 60, 60);
            public override Color MenuItemBorder => Color.FromArgb(70, 70, 70);
            public override Color MenuBorder => Color.FromArgb(70, 70, 70);
            public override Color MenuItemPressedGradientBegin => Color.FromArgb(70, 70, 70);
            public override Color MenuItemPressedGradientEnd => Color.FromArgb(70, 70, 70);
            public override Color ToolStripDropDownBackground => Color.FromArgb(40, 40, 40);
            public override Color ImageMarginGradientBegin => Color.FromArgb(40, 40, 40);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(40, 40, 40);
            public override Color ImageMarginGradientEnd => Color.FromArgb(40, 40, 40);
        }

        private void ToggleStartWithWindows()
        {
            bool currentValue = _settings.StartWithWindows;
            _settingsManager.SetStartWithWindows(!currentValue);
            
            _settings.StartWithWindows = _settingsManager.CurrentSettings.StartWithWindows;
            
            string message = _settings.StartWithWindows ? 
                             "Gemini Overlay will now start automatically at Windows startup." : 
                             "Gemini Overlay will no longer start automatically at Windows startup.";
            
            _notifyIcon.ShowBalloonTip(
                3000,
                "Startup Setting Changed",
                message,
                ToolTipIcon.Info);
                
            UpdateStartupMenuItem();
        }
        
        private void UpdateStartupMenuItem()
        {
            if (_notifyIcon?.ContextMenuStrip?.Items == null)
                return;
                
            foreach (ToolStripItem item in _notifyIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem menuItem && 
                    menuItem.Text == "Start with Windows")
                {
                    menuItem.Checked = _settingsManager.CurrentSettings.StartWithWindows;
                    break;
                }
            }
        }

        private void ChangeModel(string modelName)
        {
            if (!_settings.AvailableModels.ContainsKey(modelName))
                return;
                
            string modelId = _settings.AvailableModels[modelName];
            
            _settingsManager.SetSelectedModel(modelName, modelId);
            
            foreach (ToolStripItem item in _notifyIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem menuItem && menuItem.Text == "Model Selection")
                {
                    foreach (ToolStripItem subItem in menuItem.DropDownItems)
                    {
                        if (subItem is ToolStripMenuItem modelItem)
                        {
                            modelItem.Checked = modelItem.Text == modelName;
                            if (modelItem.Checked)
                            {
                                modelItem.Font = new Font(modelItem.Font, System.Drawing.FontStyle.Bold);
                            }
                            else
                            {
                                modelItem.Font = new Font(modelItem.Font, System.Drawing.FontStyle.Regular);
                            }
                        }
                    }
                    break;
                }
            }
            
            _notifyIcon.ShowBalloonTip(
                3000,
                "Model Changed",
                $"Model changed to {modelName}",
                ToolTipIcon.Info);
            
            ModelChanged?.Invoke(this, modelId);
        }

        private void ExitApplication()
        {
            _mainWindow.Close();
            System.Windows.Forms.Application.Exit();
            System.Windows.Application.Current.Shutdown();
        }
        
        public void ShowBalloonTip(int timeout, string title, string message, ToolTipIcon icon)
        {
            _notifyIcon?.ShowBalloonTip(timeout, title, message, icon);
        }
    }
}
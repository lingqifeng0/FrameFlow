using System.Drawing;
using System.Windows.Forms;
using MpvNet.Windows.Modern;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    ToolbarItem? EditToolParameter(ToolbarItem item, Form owner)
    {
        var definition = ToolbarCatalog.Find(item.Id);
        if (definition?.Parameter is not { } parameter) return item;
        using var form = ModernTheme.Dialog("设置按钮参数", 420, 180);
        form.MinimumSize = new Size(420, 220);
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        var hint = ModernTheme.Label(parameter.Label); hint.Location = new Point(20, 18);
        var number = new NumericUpDown { Location = new Point(20, 55), Width = 375, DecimalPlaces = parameter.Decimals,
            Minimum = (decimal)parameter.Minimum, Maximum = (decimal)parameter.Maximum,
            Increment = parameter.Decimals == 0 ? 1 : .01m,
            Value = (decimal)Math.Clamp(Math.Abs(item.Value), parameter.Minimum, parameter.Maximum), AccessibleName = parameter.Label };
        ModernTheme.StyleInput(number);
        var range = ModernTheme.Label($"范围 {parameter.Minimum}–{parameter.Maximum}", 9, true); range.Location = new Point(20, 103);
        var ok = new ModernButton("保存", width: 90) { Location = new Point(305, 132), Accent = true };
        ok.Click += (_, _) => form.DialogResult = DialogResult.OK;
        form.AcceptButton = ok; form.Controls.AddRange([hint, number, range, ok]);
        return form.ShowDialog(owner) == DialogResult.OK ? definition.Parameterize((double)number.Value) : null;
    }

    void ShowToolbarEditor()
    {
        using var form = ModernTheme.Dialog("编辑工具栏", 900, 600);
        form.MinimumSize = form.Size;
        var selected = new ListBox { Location = new Point(20, 60), Size = new Size(350, 423), BorderStyle = BorderStyle.None, AllowDrop = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom, AccessibleName = "已添加的工具栏按钮" };
        var catalog = new ListBox { Location = new Point(408, 105), Size = new Size(470, 378), BorderStyle = BorderStyle.None,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right, DisplayMember = nameof(ToolDefinition.Title), AccessibleName = "可添加工具栏功能" };
        ModernTheme.StyleInput(selected); ModernTheme.StyleInput(catalog);
        var selectedTitle = ModernTheme.Label("工具栏按钮 · 拖动调整顺序", 11); selectedTitle.Location = new Point(20, 16);
        var catalogTitle = ModernTheme.Label($"可添加功能 · {ToolbarCatalog.All.Count} 项", 11); catalogTitle.Location = new Point(408, 16);
        var category = new ComboBox { Location = new Point(408, 60), Width = 170, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "功能分类" };
        category.Items.Add("全部功能"); foreach (var group in ToolbarCatalog.All.Select(t => t.Category).Distinct()) category.Items.Add(group);
        var search = new TextBox { Location = new Point(592, 60), Width = 286, PlaceholderText = "搜索：字幕、倍速、缩放…", AccessibleName = "搜索工具栏功能", Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        ModernTheme.StyleInput(category); ModernTheme.StyleInput(search);
        var parameter = new ModernButton("编辑参数", width: 112) { Location = new Point(240, 493), Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Enabled = false };
        var add = new ModernButton("添加选中", "\uE710", 148) { Location = new Point(408, 493), Accent = true, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        void UpdateAddState()
        {
            add.Enabled = catalog.Items.Count > 0 && _modernSettings.CanAddToolbarItem;
            add.Text = _modernSettings.CanAddToolbarItem ? "添加选中" : "槽位已满";
            _modernTips.SetToolTip(add, _modernSettings.CanAddToolbarItem
                ? $"还可添加 {_modernSettings.RemainingToolbarSlots} 个按钮"
                : "16 个槽位已满，请先移除一个已有按钮，再添加新按钮。");
        }
        void RefreshCatalog()
        {
            catalog.Items.Clear();
            foreach (var definition in ToolbarCatalog.Filter(category.Text, search.Text)) catalog.Items.Add(definition);
            if (catalog.Items.Count > 0) catalog.SelectedIndex = 0;
            UpdateAddState();
            catalogTitle.Text = $"可添加功能 · {catalog.Items.Count} / {ToolbarCatalog.All.Count} 项";
        }
        void RefreshSelected(int index = -1)
        {
            selected.Items.Clear(); foreach (var item in _modernSettings.Toolbar) selected.Items.Add(item.Title);
            if (index >= 0 && index < selected.Items.Count) selected.SelectedIndex = index;
            selectedTitle.Text = $"槽位 {_modernSettings.Toolbar.Count}/{ModernSettings.MaxToolbarItems} · 剩余 {_modernSettings.RemainingToolbarSlots} · 拖动排序";
            UpdateAddState();
            RebuildToolbar(); SaveModernSettings();
        }
        void Move(int from, int to)
        {
            if (from < 0 || to < 0 || to >= _modernSettings.Toolbar.Count || from == to) return;
            var item = _modernSettings.Toolbar[from]; _modernSettings.Toolbar.RemoveAt(from); _modernSettings.Toolbar.Insert(to, item); RefreshSelected(to);
        }
        void EditParameter()
        {
            int index = selected.SelectedIndex;
            if (index < 0) return;
            var result = EditToolParameter(_modernSettings.Toolbar[index], form);
            if (result != null) { _modernSettings.Toolbar[index] = result; RefreshSelected(index); }
        }
        parameter.Click += (_, _) => EditParameter();
        selected.DoubleClick += (_, _) => EditParameter();
        selected.SelectedIndexChanged += (_, _) => parameter.Enabled = selected.SelectedIndex >= 0 && ToolbarCatalog.Find(_modernSettings.Toolbar[selected.SelectedIndex].Id)?.Parameter != null;
        category.SelectedIndexChanged += (_, _) => RefreshCatalog(); search.TextChanged += (_, _) => RefreshCatalog();
        int dragIndex = -1; Point dragStart = Point.Empty;
        selected.MouseDown += (_, e) => { dragIndex = selected.IndexFromPoint(e.Location); dragStart = e.Location; };
        selected.MouseMove += (_, e) => { if (e.Button == MouseButtons.Left && dragIndex >= 0 && Math.Abs(e.Y - dragStart.Y) > 8) selected.DoDragDrop(dragIndex, DragDropEffects.Move); };
        selected.DragOver += (_, e) => { if (e.Data?.GetDataPresent(typeof(int)) == true) e.Effect = DragDropEffects.Move; };
        selected.DragDrop += (_, e) => { if (e.Data?.GetData(typeof(int)) is int from) { int to = selected.IndexFromPoint(selected.PointToClient(new Point(e.X, e.Y))); if (to < 0) to = _modernSettings.Toolbar.Count - 1; Move(from, to); } };
        var up = new ModernButton("上移", "\uE70E", 94) { Location = new Point(20, 493), Anchor = AnchorStyles.Bottom | AnchorStyles.Left }; up.Click += (_, _) => Move(selected.SelectedIndex, selected.SelectedIndex - 1);
        var down = new ModernButton("下移", "\uE70D", 94) { Location = new Point(120, 493), Anchor = AnchorStyles.Bottom | AnchorStyles.Left }; down.Click += (_, _) => Move(selected.SelectedIndex, selected.SelectedIndex + 1);
        var remove = new ModernButton("移除选中", width: 106) { Location = new Point(20, 540), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        remove.Click += (_, _) => { int index = selected.SelectedIndex; if (index >= 0) { _modernSettings.Toolbar.RemoveAt(index); RefreshSelected(Math.Min(index, _modernSettings.Toolbar.Count - 1)); } };
        var reset = new ModernButton("默认按钮", width: 106) { Location = new Point(134, 540), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        reset.Click += (_, _) => { _modernSettings.Toolbar = new ModernSettings().Toolbar; RefreshSelected(); };
        add.Click += (_, _) => {
            if (catalog.SelectedItem is not ToolDefinition definition) return;
            if (!_modernSettings.CanAddToolbarItem) { ShowToast("16 个槽位已满，请先移除一个按钮"); return; }
            var item = EditToolParameter(definition.Item, form);
            if (item == null) return;
            if (!_modernSettings.TryAddToolbarItem(item)) { UpdateAddState(); return; }
            RefreshSelected(_modernSettings.Toolbar.Count - 1);
        };
        catalog.DoubleClick += (_, _) => add.PerformClick();
        var labels = new CheckBox { Text = "显示按钮文字", AutoSize = true, ForeColor = ModernTheme.Text, Checked = _modernSettings.ShowButtonLabels,
            Location = new Point(408, 545), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        labels.CheckedChanged += (_, _) => { _modernSettings.ShowButtonLabels = labels.Checked; RebuildToolbar(); SaveModernSettings(); };
        var hint = ModernTheme.Label("多出的按钮收进“更多”；参数按钮可再次修改。", 9, true); hint.Location = new Point(578, 540); hint.MaximumSize = new Size(300, 0); hint.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        form.Controls.AddRange([selectedTitle, catalogTitle, selected, catalog, category, search, up, down, remove, parameter, reset, add, labels, hint]);
        category.SelectedIndex = 0; RefreshSelected(); form.ShowDialog(this);
    }
}

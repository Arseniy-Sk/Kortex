using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Kortex.Devices;
using Kortex.Localization;
using Kortex.Models;
using Kortex.Services;
using Kortex.Via;

namespace Kortex.ViewModels
{
    public sealed class LayerVm : ObservableObject
    {
        public int Index { get; set; }
        public string Title => Loc.T("Keymap.Layer") + " " + Index;

        private bool _isCurrent;
        public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }

        public void RefreshTitle() => Raise(nameof(Title));
    }

    /// <summary>
    /// Редактор раскладки в прошивке платы: чтение слоёв по VIA, сопоставление
    /// физических клавиш с матрицей и запись новых кодов.
    /// </summary>
    public sealed class KeymapVm : ObservableObject
    {
        private readonly DeviceService _devices;
        private readonly Action<Action> _toUi;
        private readonly Dictionary<string, int[]> _matrix = new Dictionary<string, int[]>();
        private ushort[][,] _layers;
        private int _rows, _cols;

        public KeyboardVm Board { get; } = new KeyboardVm();
        public ObservableCollection<LayerVm> Layers { get; } = new ObservableCollection<LayerVm>();
        public ObservableCollection<KeycodeInfo> Codes { get; } = new ObservableCollection<KeycodeInfo>();

        public RelayCommand ReloadCommand { get; }
        public RelayCommand ResetLayerCommand { get; }
        public RelayCommand<LayerVm> SelectLayerCommand { get; }
        public RelayCommand<KeycodeInfo> PickCodeCommand { get; }
        public RelayCommand RematchCommand { get; }
        public RelayCommand ExportCommand { get; }
        public RelayCommand ImportCommand { get; }

        public KeymapVm(DeviceService devices, Action<Action> marshalToUi)
        {
            _devices = devices;
            _toUi = marshalToUi ?? (a => a());

            ReloadCommand = new RelayCommand(() => Load(true), () => HasDevice && !IsBusy);
            ResetLayerCommand = new RelayCommand(ResetLayer, () => HasDevice && !IsBusy);
            SelectLayerCommand = new RelayCommand<LayerVm>(l => { if (l != null) CurrentLayer = l.Index; });
            PickCodeCommand = new RelayCommand<KeycodeInfo>(code => { if (code != null) WriteSelected(code.Code); });
            RematchCommand = new RelayCommand(Rematch, () => HasDevice && !IsBusy);
            ExportCommand = new RelayCommand(ExportLayout, () => HasDevice && !IsBusy);
            ImportCommand = new RelayCommand(ImportLayout, () => HasDevice && !IsBusy);

            _devices.ConnectionChanged += OnConnectionChanged;
            Loc.LanguageChanged += OnLanguageChanged;
        }

        // ---------------- Состояние ----------------
        public bool HasDevice => _devices.Via != null && _devices.Connected != null;

        private bool _busy;
        public bool IsBusy
        {
            get => _busy;
            private set { if (Set(ref _busy, value)) RelayCommand.Refresh(); }
        }

        private string _status;
        public string Status { get => _status; private set => Set(ref _status, value); }

        private int _matched;
        public int MatchedCount { get => _matched; private set { if (Set(ref _matched, value)) Raise(nameof(MatchText)); } }

        private int _matchable;
        public string MatchText => MatchedCount + " / " + _matchable;

        private int _currentLayer;
        public int CurrentLayer
        {
            get => _currentLayer;
            set
            {
                if (!Set(ref _currentLayer, value)) return;
                foreach (var l in Layers) l.IsCurrent = l.Index == value;
                ApplyLabels();
                Raise(nameof(LayerTitle));
            }
        }

        public string LayerTitle => Loc.T("Keymap.Layer") + " " + _currentLayer;

        private KeyCapVm _selected;
        public KeyCapVm Selected
        {
            get => _selected;
            set
            {
                if (_selected != null) _selected.IsSelected = false;
                Set(ref _selected, value);
                if (_selected != null) _selected.IsSelected = true;
                Raise(nameof(HasSelection));
                Raise(nameof(SelectedTitle));
                Raise(nameof(SelectedCodeName));
                Raise(nameof(SelectedMatrix));
            }
        }

        public bool HasSelection => _selected != null;

        public string SelectedTitle => _selected == null ? Loc.T("Remap.NoSelection") : _selected.Def.Label;

        public string SelectedMatrix
        {
            get
            {
                if (_selected == null) return null;
                int[] cell;
                if (!_matrix.TryGetValue(_selected.Def.Uid, out cell)) return Loc.T("Keymap.Unmapped");
                return "row " + cell[0] + " · col " + cell[1];
            }
        }

        public string SelectedCodeName
        {
            get
            {
                if (_selected == null || _layers == null) return null;
                int[] cell;
                if (!_matrix.TryGetValue(_selected.Def.Uid, out cell)) return null;
                return QmkKeycodes.Name(_layers[_currentLayer][cell[0], cell[1]]);
            }
        }

        private string _search = string.Empty;
        public string Search
        {
            get => _search;
            set { if (Set(ref _search, value)) FilterCodes(); }
        }

        private KeycodeInfo _pick;
        /// <summary>Выбор кода в списке сразу пишется в плату — как в VIA.</summary>
        public KeycodeInfo PickedCode
        {
            get => _pick;
            set
            {
                if (!Set(ref _pick, value)) return;
                if (value != null) WriteSelected(value.Code);
            }
        }

        // ---------------- Загрузка ----------------
        private void OnConnectionChanged()
        {
            Raise(nameof(HasDevice));
            RelayCommand.Refresh();
            if (HasDevice) Load(false);
            else
            {
                _layers = null;
                _matrix.Clear();
                Layers.Clear();
                MatchedCount = 0;
                Status = null;
                foreach (var cap in Board.Keys) { cap.OverrideLabel = null; cap.IsDimmed = false; }
            }
        }

        private void OnLanguageChanged()
        {
            foreach (var l in Layers) l.RefreshTitle();
            Raise(nameof(LayerTitle));
            Raise(nameof(SelectedTitle));
            Raise(nameof(SelectedMatrix));
        }

        public void RebuildBoard(LayoutOptions options)
        {
            Board.Rebuild(options);
            if (HasDevice) Load(false);
        }

        /// <summary>Читает слои с платы в фоне и сопоставляет их с физической схемой.</summary>
        public void Load(bool announce)
        {
            if (!HasDevice || IsBusy) return;
            var client = _devices.Via;
            var device = _devices.Connected;
            int rows = device.Profile != null ? device.Profile.MatrixRows : 6;
            int cols = device.Profile != null ? device.Profile.MatrixCols : 17;
            int layerCount = Math.Max(1, device.LayerCount);

            IsBusy = true;
            Status = Loc.T("Keymap.Reading");

            var thread = new Thread(() =>
            {
                var read = new ushort[layerCount][,];
                bool ok = true;
                for (int layer = 0; layer < layerCount && ok; layer++)
                {
                    var map = client.ReadLayer(layer, rows, cols);
                    if (map == null) { ok = false; break; }
                    read[layer] = map;
                }

                _toUi(() =>
                {
                    IsBusy = false;
                    if (!ok)
                    {
                        Status = Loc.T("Device.ViaNo");
                        return;
                    }
                    _layers = read;
                    _rows = rows;
                    _cols = cols;

                    Layers.Clear();
                    for (int i = 0; i < layerCount; i++)
                        Layers.Add(new LayerVm { Index = i, IsCurrent = i == 0 });
                    _currentLayer = 0;
                    Raise(nameof(CurrentLayer));
                    Raise(nameof(LayerTitle));

                    BuildCodeCatalog(layerCount, device.MacroCount);

                    // Матрица платы определяется один раз по заводской раскладке и запоминается:
                    // после правки клавиш их коды уже не совпадают с заводскими.
                    if (!RestoreMatrix(device.Key))
                    {
                        MatchMatrix();
                        StoreMatrix(device.Key);
                        Status = Loc.T("Keymap.Learned", MatchedCount);
                    }
                    else Status = Loc.T("Keymap.MatrixHint");

                    ApplyLabels();
                });
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void BuildCodeCatalog(int layers, int macros)
        {
            Codes.Clear();
            foreach (var c in QmkKeycodes.Catalog(layers, macros)) Codes.Add(c);
            _allCodes = new List<KeycodeInfo>(Codes);
        }

        private List<KeycodeInfo> _allCodes = new List<KeycodeInfo>();

        private void FilterCodes()
        {
            Codes.Clear();
            string q = (_search ?? string.Empty).Trim();
            foreach (var c in _allCodes)
            {
                if (q.Length == 0 ||
                    c.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (c.Label ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (c.Category ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    Codes.Add(c);
            }
        }

        /// <summary>
        /// Сопоставляет физические клавиши с ячейками матрицы: для каждой клавиши ищется
        /// свободная ячейка нулевого слоя с тем же кодом QMK.
        /// </summary>
        /// <summary>Берёт ранее выученную матрицу этой платы, если она есть.</summary>
        private bool RestoreMatrix(string deviceKey)
        {
            Dictionary<string, int[]> saved;
            var all = AppServices.State.Matrices;
            if (all == null || !all.TryGetValue(deviceKey, out saved) || saved == null || saved.Count == 0)
                return false;

            _matrix.Clear();
            _matchable = 0;
            int matched = 0;
            foreach (var cap in Board.Keys)
            {
                if (cap.IsBlocker) continue;
                _matchable++;
                int[] cell;
                if (saved.TryGetValue(cap.Def.Uid, out cell) && cell.Length == 2 &&
                    cell[0] < _rows && cell[1] < _cols)
                {
                    _matrix[cap.Def.Uid] = new[] { cell[0], cell[1] };
                    cap.IsDimmed = false;
                    matched++;
                }
                else cap.IsDimmed = true;
            }
            MatchedCount = matched;
            return matched > 0;
        }

        private void StoreMatrix(string deviceKey)
        {
            var all = AppServices.State.Matrices ??
                      (AppServices.State.Matrices = new Dictionary<string, Dictionary<string, int[]>>());
            var saved = new Dictionary<string, int[]>();
            foreach (var pair in _matrix) saved[pair.Key] = pair.Value;
            all[deviceKey] = saved;
            AppServices.Save();
        }

        /// <summary>Забыть выученную матрицу и определить её заново по заводской раскладке.</summary>
        private void Rematch()
        {
            var device = _devices.Connected;
            if (device == null) return;
            if (AppServices.State.Matrices != null) AppServices.State.Matrices.Remove(device.Key);
            if (_layers == null) { Load(true); return; }
            MatchMatrix();
            StoreMatrix(device.Key);
            ApplyLabels();
            Status = Loc.T("Keymap.Learned", MatchedCount);
        }

        private void MatchMatrix()
        {
            _matrix.Clear();
            if (_layers == null || _layers.Length == 0) return;

            var layer0 = _layers[0];
            var byCode = new Dictionary<ushort, List<int[]>>();
            for (int r = 0; r < _rows; r++)
                for (int c = 0; c < _cols; c++)
                {
                    ushort code = layer0[r, c];
                    if (code == QmkKeycodes.KC_NO) continue;
                    List<int[]> cells;
                    if (!byCode.TryGetValue(code, out cells)) { cells = new List<int[]>(); byCode[code] = cells; }
                    cells.Add(new[] { r, c });
                }

            var used = new HashSet<string>();
            int matched = 0;
            _matchable = 0;

            foreach (var cap in Board.Keys.Where(k => !k.IsBlocker))
            {
                _matchable++;
                ushort expected = cap.IsFirmware
                    ? (ushort)(QmkKeycodes.QK_MOMENTARY | 1)
                    : QmkKeycodes.FromVirtualKey(cap.Id);

                int[] cell = Take(byCode, used, expected);
                if (cell == null && cap.IsFirmware)
                {
                    // Fn может быть назначен на любой слой
                    for (int layer = 0; layer < 8 && cell == null; layer++)
                        cell = Take(byCode, used, (ushort)(QmkKeycodes.QK_MOMENTARY | layer));
                }
                if (cell == null) { cap.IsDimmed = true; continue; }

                cap.IsDimmed = false;
                _matrix[cap.Def.Uid] = cell;
                used.Add(cell[0] + ":" + cell[1]);
                matched++;
            }

            MatchedCount = matched;
        }

        private static int[] Take(Dictionary<ushort, List<int[]>> byCode, HashSet<string> used, ushort code)
        {
            List<int[]> cells;
            if (!byCode.TryGetValue(code, out cells)) return null;
            foreach (var cell in cells)
                if (!used.Contains(cell[0] + ":" + cell[1])) return cell;
            return null;
        }

        private void ApplyLabels()
        {
            if (_layers == null) return;
            foreach (var cap in Board.Keys)
            {
                if (cap.IsBlocker) continue;
                int[] cell;
                if (!_matrix.TryGetValue(cap.Def.Uid, out cell)) { cap.OverrideLabel = "·"; continue; }
                ushort code = _layers[_currentLayer][cell[0], cell[1]];
                cap.OverrideLabel = QmkKeycodes.Label(code);
            }
            Raise(nameof(SelectedCodeName));
        }

        private void WriteSelected(ushort code)
        {
            if (_selected == null || _layers == null || !HasDevice) return;
            int[] cell;
            if (!_matrix.TryGetValue(_selected.Def.Uid, out cell)) return;

            var client = _devices.Via;
            int layer = _currentLayer;
            int row = cell[0], col = cell[1];

            IsBusy = true;
            var thread = new Thread(() =>
            {
                bool ok = client.SetKeycode(layer, row, col, code);
                _toUi(() =>
                {
                    IsBusy = false;
                    if (ok)
                    {
                        _layers[layer][row, col] = code;
                        ApplyLabels();
                        Status = Loc.T("Keymap.Saved");
                    }
                    else Status = Loc.T("Device.ViaNo");
                });
            });
            thread.IsBackground = true;
            thread.Start();
        }

        /// <summary>Сохраняет слои в файл формата VIA — его читает и сама VIA.</summary>
        private void ExportLayout()
        {
            var device = _devices.Connected;
            if (device == null || _layers == null) return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "VIA layout (*.json)|*.json",
                FileName = device.DisplayName + ".json"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                ViaLayoutFile.Save(dlg.FileName, device.DisplayName, device.VendorId, device.ProductId,
                    _layers, _rows, _cols);
                Status = Loc.T("Keymap.Exported");
            }
            catch (Exception ex)
            {
                Status = ex.Message;
            }
        }

        /// <summary>Загружает раскладку из файла VIA и пишет её в плату.</summary>
        private void ImportLayout()
        {
            if (!HasDevice || IsBusy) return;
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "VIA layout (*.json)|*.json" };
            if (dlg.ShowDialog() != true) return;

            ushort[][,] loaded;
            int count;
            try
            {
                loaded = ViaLayoutFile.Load(dlg.FileName, _rows, _cols, out count);
            }
            catch (Exception ex)
            {
                Status = ex.Message;
                return;
            }
            if (loaded == null || count == 0) { Status = Loc.T("Keymap.ImportFailed"); return; }

            var client = _devices.Via;
            int layers = Math.Min(count, _layers.Length);
            IsBusy = true;
            Status = Loc.T("Keymap.Writing");

            var thread = new Thread(() =>
            {
                bool ok = true;
                for (int layer = 0; layer < layers && ok; layer++)
                {
                    var bytes = new byte[_rows * _cols * 2];
                    for (int r = 0; r < _rows; r++)
                        for (int c = 0; c < _cols; c++)
                        {
                            int i = (r * _cols + c) * 2;
                            bytes[i] = (byte)(loaded[layer][r, c] >> 8);
                            bytes[i + 1] = (byte)(loaded[layer][r, c] & 0xFF);
                        }
                    ok = client.WriteKeymapBuffer(layer * _rows * _cols * 2, bytes);
                }

                _toUi(() =>
                {
                    IsBusy = false;
                    if (!ok) { Status = Loc.T("Device.ViaNo"); return; }
                    Status = Loc.T("Keymap.Imported", layers);
                    Load(false);
                });
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void ResetLayer()
        {
            if (!HasDevice || IsBusy) return;
            var client = _devices.Via;
            IsBusy = true;
            var thread = new Thread(() =>
            {
                client.ResetKeymap();
                _toUi(() => { IsBusy = false; Load(true); });
            });
            thread.IsBackground = true;
            thread.Start();
        }
    }
}

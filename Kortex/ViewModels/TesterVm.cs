using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Kortex.Input;
using Kortex.Localization;
using Kortex.Models;
using Kortex.Services;

namespace Kortex.ViewModels
{
    public sealed class KeyLogItem
    {
        public string Time { get; set; }
        public string Key { get; set; }
        public string Arrow { get; set; }
        public string Detail { get; set; }
        public bool IsDown { get; set; }
        public bool IsWarning { get; set; }
    }

    public sealed class ChatterItem
    {
        public string Key { get; set; }
        public double GapMs { get; set; }
        public int Count { get; set; }
        public string Info => Loc.T("Tester.ChatterInfo", GapMs.ToString("0.#"));
    }

    /// <summary>Экран «Тест клавиатуры».</summary>
    public sealed class TesterVm : ObservableObject
    {
        private readonly Dictionary<KeyId, double> _lastUp = new Dictionary<KeyId, double>();
        private readonly Dictionary<KeyId, double> _downAt = new Dictionary<KeyId, double>();
        private readonly Dictionary<KeyId, ChatterItem> _chatter = new Dictionary<KeyId, ChatterItem>();
        private readonly HashSet<KeyId> _pressed = new HashSet<KeyId>();

        public KeyboardVm Board { get; } = new KeyboardVm();
        public ObservableCollection<KeyLogItem> Log { get; } = new ObservableCollection<KeyLogItem>();
        public ObservableCollection<ChatterItem> Chatter { get; } = new ObservableCollection<ChatterItem>();

        public RelayCommand ResetCommand { get; }

        public TesterVm()
        {
            ResetCommand = new RelayCommand(Reset);
            Board.Rebuilt += RefreshStats;
        }

        // ---------- Статистика ----------
        private int _tested;
        public int TestedCount { get => _tested; private set { if (Set(ref _tested, value)) { Raise(nameof(Progress)); Raise(nameof(ProgressText)); Raise(nameof(RemainingText)); } } }

        public int TestableCount => Board.TestableCount;
        public double Progress => Board.TestableCount == 0 ? 0 : (double)TestedCount / Board.TestableCount;
        public string ProgressText => TestedCount + " / " + Board.TestableCount;
        public string RemainingText => Loc.T("Tester.Remaining", Math.Max(0, Board.TestableCount - TestedCount));

        private int _totalPresses;
        public int TotalPresses { get => _totalPresses; private set => Set(ref _totalPresses, value); }

        private int _maxSimultaneous;
        public int MaxSimultaneous { get => _maxSimultaneous; private set => Set(ref _maxSimultaneous, value); }

        private int _currentSimultaneous;
        public int CurrentSimultaneous { get => _currentSimultaneous; private set => Set(ref _currentSimultaneous, value); }

        private int _chatterCount;
        public int ChatterCount { get => _chatterCount; private set => Set(ref _chatterCount, value); }

        // ---------- Последняя клавиша ----------
        private string _lastName = "—";
        public string LastName { get => _lastName; private set => Set(ref _lastName, value); }

        private string _lastVk = "—";
        public string LastVk { get => _lastVk; private set => Set(ref _lastVk, value); }

        private string _lastScan = "—";
        public string LastScan { get => _lastScan; private set => Set(ref _lastScan, value); }

        private double? _lastHoldMs;
        private string _lastHold = "—";
        public string LastHold { get => _lastHold; private set => Set(ref _lastHold, value); }

        private string _lastFlags = "—";
        public string LastFlags { get => _lastFlags; private set => Set(ref _lastFlags, value); }

        private string _untested = string.Empty;
        public string UntestedList { get => _untested; private set => Set(ref _untested, value); }

        // ---------- Приём событий ----------
        public void Handle(HookKeyEventArgs e)
        {
            if (e.FromUs) return;

            var caps = Board.Find(e.Key);
            var id = e.Key;

            if (e.IsDown)
            {
                if (!e.IsRepeat)
                {
                    _pressed.Add(id);
                    CurrentSimultaneous = _pressed.Count;
                    if (_pressed.Count > MaxSimultaneous) MaxSimultaneous = _pressed.Count;
                    TotalPresses++;
                    _downAt[id] = e.TimestampMs;

                    double lastUp;
                    bool chatter = _lastUp.TryGetValue(id, out lastUp) &&
                                   (e.TimestampMs - lastUp) < AppServices.State.ChatterThresholdMs;
                    if (chatter) RegisterChatter(id, e.TimestampMs - lastUp);

                    if (caps != null)
                        foreach (var c in caps)
                        {
                            c.IsDown = true;
                            c.PressCount++;
                            if (!c.IsTested) { c.IsTested = true; TestedCount++; RefreshUntested(); }
                            if (chatter) c.HasChatter = true;
                        }

                    AddLog(e, caps != null, chatter);
                    UpdateLastInfo(e, null);
                    RecomputeHeat();
                }
            }
            else
            {
                _pressed.Remove(id);
                CurrentSimultaneous = _pressed.Count;
                _lastUp[id] = e.TimestampMs;

                double downTs;
                double? hold = _downAt.TryGetValue(id, out downTs) ? (double?)(e.TimestampMs - downTs) : null;

                if (caps != null)
                    foreach (var c in caps)
                    {
                        c.IsDown = false;
                        if (hold.HasValue) c.LastHoldMs = hold.Value;
                    }

                AddLog(e, caps != null, false);
                UpdateLastInfo(e, hold);
            }
        }

        private void RegisterChatter(KeyId id, double gapMs)
        {
            ChatterItem item;
            if (!_chatter.TryGetValue(id, out item))
            {
                item = new ChatterItem { Key = id.Name, Count = 0 };
                _chatter[id] = item;
                Chatter.Insert(0, item);
            }
            item.Count++;
            item.GapMs = gapMs;
            ChatterCount++;
            // ObservableCollection не отслеживает изменения полей — обновляем элемент вручную
            int idx = Chatter.IndexOf(item);
            if (idx >= 0)
            {
                Chatter.RemoveAt(idx);
                Chatter.Insert(idx, item);
            }
        }

        private void UpdateLastInfo(HookKeyEventArgs e, double? hold)
        {
            LastName = KeyCodes.Name(e.Vk, e.Extended);
            LastVk = "0x" + e.Vk.ToString("X2") + "  (" + e.Vk + ")";
            LastScan = "0x" + e.ScanCode.ToString("X2");
            LastFlags = (e.Extended ? "extended" : "standard") + (e.Injected ? " · injected" : "");
            if (hold.HasValue) _lastHoldMs = hold.Value;
            LastHold = _lastHoldMs.HasValue
                ? _lastHoldMs.Value.ToString("0") + " " + Loc.T("Common.Ms")
                : "—";
        }

        private void AddLog(HookKeyEventArgs e, bool known, bool chatter)
        {
            var item = new KeyLogItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                Key = KeyCodes.Name(e.Vk, e.Extended),
                Arrow = e.IsDown ? "▼" : "▲",
                IsDown = e.IsDown,
                IsWarning = chatter || !known,
                Detail = chatter ? Loc.T("Tester.ChatterShort")
                       : !known ? Loc.T("Tester.NotOnLayout")
                       : "VK 0x" + e.Vk.ToString("X2") + " · SC 0x" + e.ScanCode.ToString("X2")
            };
            Log.Insert(0, item);
            while (Log.Count > 150) Log.RemoveAt(Log.Count - 1);
        }

        private void RefreshUntested()
        {
            var names = Board.Keys.Where(k => k.IsActive && !k.IsTested)
                                  .Select(k => k.Id.Name)
                                  .Distinct()
                                  .Take(40)
                                  .ToList();
            UntestedList = names.Count == 0 ? Loc.T("Tester.AllTested") : string.Join("   ", names);
        }

        public void RefreshStats()
        {
            TestedCount = Board.Keys.Count(k => k.IsActive && k.IsTested);
            Raise(nameof(TestableCount));
            Raise(nameof(Progress));
            Raise(nameof(ProgressText));
            Raise(nameof(RemainingText));
            RefreshUntested();
        }

        private bool _heatmap;
        public bool HeatmapEnabled
        {
            get => _heatmap;
            set
            {
                if (!Set(ref _heatmap, value)) return;
                if (value) RecomputeHeat();
                else foreach (var k in Board.Keys) k.Heat = 0;
            }
        }

        /// <summary>Пересчитывает тепловую карту: доля нажатий от самой активной клавиши.</summary>
        private void RecomputeHeat()
        {
            if (!_heatmap) return;
            int max = 0;
            foreach (var k in Board.Keys) if (k.PressCount > max) max = k.PressCount;
            if (max <= 0) { foreach (var k in Board.Keys) k.Heat = 0; return; }
            foreach (var k in Board.Keys) k.Heat = (double)k.PressCount / max;
        }

        /// <summary>Обновляет строки после смены языка.</summary>
        public void RefreshTexts()
        {
            Raise(nameof(RemainingText));
            LastHold = _lastHoldMs.HasValue
                ? _lastHoldMs.Value.ToString("0") + " " + Loc.T("Common.Ms")
                : "—";
            RefreshUntested();
            var snapshot = Chatter.ToList();
            Chatter.Clear();
            foreach (var c in snapshot) Chatter.Add(c);
        }

        public void Reset()
        {
            Board.ResetAll();
            Log.Clear();
            Chatter.Clear();
            _chatter.Clear();
            _lastUp.Clear();
            _downAt.Clear();
            _pressed.Clear();
            TestedCount = 0;
            TotalPresses = 0;
            MaxSimultaneous = 0;
            CurrentSimultaneous = 0;
            ChatterCount = 0;
            _lastHoldMs = null;
            LastName = "—"; LastVk = "—"; LastScan = "—"; LastHold = "—"; LastFlags = "—";
            RefreshUntested();
        }

        /// <summary>Отобразить на схеме активные переназначения.</summary>
        public void ApplyMappingLabels(Profile profile)
        {
            foreach (var k in Board.Keys) k.Mapping = null;
            if (profile == null) return;
            foreach (var rule in profile.Remaps)
            {
                var caps = Board.Find(rule.FromKey);
                if (caps == null) continue;
                string label;
                switch (rule.Kind)
                {
                    case RemapTargetKind.Disabled: label = "∅"; break;
                    case RemapTargetKind.Macro:
                        var macro = profile.Macros.FirstOrDefault(m => m.Id == rule.MacroId);
                        label = macro != null ? macro.Name : "макрос";
                        break;
                    case RemapTargetKind.Text: label = "текст"; break;
                    default: label = rule.ToKey.Name; break;
                }
                foreach (var c in caps) c.Mapping = label;
            }
        }
    }
}

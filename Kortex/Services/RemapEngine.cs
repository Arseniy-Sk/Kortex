using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Kortex.Input;
using Kortex.Models;

namespace Kortex.Services
{
    /// <summary>
    /// Применяет переназначения и запускает макросы поверх глобального хука.
    /// Работает на уровне Windows (SendInput), прошивка платы не затрагивается.
    /// </summary>
    public sealed class RemapEngine
    {
        private readonly Dictionary<KeyId, RemapRule> _rules = new Dictionary<KeyId, RemapRule>();
        private readonly Dictionary<string, Macro> _macros = new Dictionary<string, Macro>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _running = new HashSet<string>();
        private readonly object _sync = new object();

        public bool Enabled { get; set; }
        /// <summary>Приостановить движок (например, на время записи макроса).</summary>
        public bool Paused { get; set; }

        public event Action<Macro> MacroStarted;
        public event Action<Macro> MacroFinished;

        public int RuleCount { get { lock (_sync) return _rules.Count; } }

        public void Load(Profile profile)
        {
            lock (_sync)
            {
                _rules.Clear();
                _macros.Clear();
                if (profile == null) return;
                foreach (var m in profile.Macros) _macros[m.Id] = m;
                foreach (var r in profile.Remaps)
                {
                    if (string.IsNullOrEmpty(r.From)) continue;
                    _rules[r.FromKey] = r;
                }
            }
        }

        /// <summary>Вызывается из хука. Возвращает true, если событие поглощено.</summary>
        public bool Process(HookKeyEventArgs e)
        {
            if (!Enabled || Paused || e.FromUs) return false;

            RemapRule rule;
            lock (_sync)
            {
                if (!_rules.TryGetValue(e.Key, out rule))
                    if (!_rules.TryGetValue(new KeyId(e.Vk, !e.Extended), out rule))
                        return false;
            }

            switch (rule.Kind)
            {
                case RemapTargetKind.Disabled:
                    return true;

                case RemapTargetKind.Key:
                    var target = rule.ToKey;
                    if (target.Vk == 0) return true;
                    InputSender.Send(target, e.IsDown);
                    return true;

                case RemapTargetKind.Text:
                    if (e.IsDown && !e.IsRepeat)
                    {
                        string text = rule.Text;
                        ThreadPool.QueueUserWorkItem(_ => InputSender.SendText(text));
                    }
                    return true;

                case RemapTargetKind.Macro:
                    if (e.IsDown && !e.IsRepeat)
                    {
                        Macro macro;
                        lock (_sync) _macros.TryGetValue(rule.MacroId ?? string.Empty, out macro);
                        if (macro != null) Play(macro);
                    }
                    return true;
            }
            return false;
        }

        /// <summary>Проиграть макрос в фоновом потоке (хук нельзя блокировать).</summary>
        public void Play(Macro macro, Action onDone = null)
        {
            if (macro == null || macro.Steps.Count == 0) return;
            lock (_sync)
            {
                if (_running.Contains(macro.Id)) return;
                _running.Add(macro.Id);
            }

            var handlerStart = MacroStarted;
            if (handlerStart != null) handlerStart(macro);

            var thread = new Thread(() =>
            {
                try { Run(macro); }
                catch { /* воспроизведение не должно ронять приложение */ }
                finally
                {
                    lock (_sync) _running.Remove(macro.Id);
                    var handlerEnd = MacroFinished;
                    if (handlerEnd != null) handlerEnd(macro);
                    if (onDone != null) onDone();
                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private void Run(Macro macro)
        {
            double speed = macro.Speed <= 0.05 ? 1.0 : macro.Speed;
            int repeat = Math.Max(1, macro.Repeat);
            var held = new List<KeyId>();

            for (int r = 0; r < repeat; r++)
            {
                foreach (var step in macro.Steps)
                {
                    switch (step.Kind)
                    {
                        case MacroStepKind.Tap:
                            InputSender.Send(step.KeyId, true);
                            PreciseSleep(1);
                            InputSender.Send(step.KeyId, false);
                            break;
                        case MacroStepKind.Down:
                            InputSender.Send(step.KeyId, true);
                            held.Add(step.KeyId);
                            break;
                        case MacroStepKind.Up:
                            InputSender.Send(step.KeyId, false);
                            held.Remove(step.KeyId);
                            break;
                        case MacroStepKind.Delay:
                            PreciseSleep(step.DelayMs / speed);
                            break;
                        case MacroStepKind.Text:
                            InputSender.SendText(step.Text);
                            break;

                        case MacroStepKind.Launch:
                            Launch(step);
                            break;

                        case MacroStepKind.MouseClick:
                            InputSender.MouseClick((int)step.Button);
                            break;

                        case MacroStepKind.MouseMove:
                            InputSender.MouseMove(step.X, step.Y, step.IsAbsoluteMove);
                            break;

                        case MacroStepKind.Scroll:
                            InputSender.Scroll(step.Amount);
                            break;

                        case MacroStepKind.Clipboard:
                            SetClipboard(step.Text);
                            break;

                        case MacroStepKind.Stamp:
                            InputSender.SendText(Stamp(step.Text));
                            break;
                    }
                }
            }

            // Подстраховка: отпускаем всё, что осталось зажатым
            for (int i = held.Count - 1; i >= 0; i--)
                InputSender.Send(held[i], false);
        }

        /// <summary>Запуск программы, файла, папки или ссылки — как двойной щелчок в проводнике.</summary>
        private static void Launch(MacroStep step)
        {
            string target = (step.Text ?? string.Empty).Trim();
            if (target.Length == 0) return;
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Environment.ExpandEnvironmentVariables(target),
                    UseShellExecute = true
                };
                if (!string.IsNullOrWhiteSpace(step.Arg))
                    info.Arguments = Environment.ExpandEnvironmentVariables(step.Arg);
                System.Diagnostics.Process.Start(info);
            }
            catch
            {
                // неверный путь не должен обрывать остальные шаги макроса
            }
        }

        /// <summary>Поток воспроизведения помечен как STA, поэтому буфер обмена доступен напрямую.</summary>
        private static void SetClipboard(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) System.Windows.Clipboard.Clear();
                else System.Windows.Clipboard.SetText(text);
            }
            catch
            {
                // буфер может быть занят другим приложением — пропускаем шаг
            }
        }

        /// <summary>Текущая дата или время по заданному формату.</summary>
        private static string Stamp(string format)
        {
            string pattern = string.IsNullOrWhiteSpace(format) ? "dd.MM.yyyy" : format;
            try { return DateTime.Now.ToString(pattern); }
            catch { return DateTime.Now.ToString("dd.MM.yyyy"); }
        }

        private static void PreciseSleep(double ms)
        {
            if (ms <= 0) return;
            if (ms > 20) { Thread.Sleep((int)ms); return; }
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < ms) Thread.SpinWait(60);
        }
    }
}

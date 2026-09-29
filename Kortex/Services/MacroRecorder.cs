using System;
using System.Collections.Generic;
using Kortex.Input;
using Kortex.Models;

namespace Kortex.Services
{
    /// <summary>Запись последовательности нажатий с реальными паузами.</summary>
    public sealed class MacroRecorder
    {
        private double _lastTs;
        private bool _first;

        public bool IsRecording { get; private set; }
        /// <summary>Сохранять реальные интервалы между нажатиями.</summary>
        public bool CaptureDelays { get; set; } = true;
        /// <summary>Паузы короче этого значения не записываются, мс.</summary>
        public int MinDelayMs { get; set; } = 8;

        public List<MacroStep> Steps { get; } = new List<MacroStep>();

        public event Action Changed;

        public void Start()
        {
            Steps.Clear();
            _first = true;
            IsRecording = true;
            Raise();
        }

        public List<MacroStep> Stop()
        {
            IsRecording = false;
            var copy = new List<MacroStep>(Steps);
            Raise();
            return copy;
        }

        public void Cancel()
        {
            IsRecording = false;
            Steps.Clear();
            Raise();
        }

        /// <summary>Принять событие хука. Вызывается только во время записи.</summary>
        public void Feed(HookKeyEventArgs e)
        {
            if (!IsRecording || e.FromUs) return;
            if (e.IsDown && e.IsRepeat) return;   // автоповтор не пишем

            if (!_first && CaptureDelays)
            {
                int gap = (int)Math.Round(e.TimestampMs - _lastTs);
                if (gap >= MinDelayMs)
                    Steps.Add(new MacroStep { Kind = MacroStepKind.Delay, DelayMs = Math.Min(gap, 10000) });
            }
            _first = false;
            _lastTs = e.TimestampMs;

            Steps.Add(new MacroStep
            {
                Kind = e.IsDown ? MacroStepKind.Down : MacroStepKind.Up,
                Key = new KeyId(e.Vk, e.Extended).ToString()
            });
            Raise();
        }

        /// <summary>Свернуть пары Down/Up одной клавиши в один шаг «Нажать».</summary>
        public static List<MacroStep> Compact(List<MacroStep> steps)
        {
            var result = new List<MacroStep>();
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                if (s.Kind == MacroStepKind.Down)
                {
                    int upIndex = -1;
                    int delayBetween = 0;
                    for (int j = i + 1; j < steps.Count && j <= i + 2; j++)
                    {
                        if (steps[j].Kind == MacroStepKind.Delay) { delayBetween += steps[j].DelayMs; continue; }
                        if (steps[j].Kind == MacroStepKind.Up && steps[j].Key == s.Key) { upIndex = j; }
                        break;
                    }
                    if (upIndex > 0 && delayBetween <= 120)
                    {
                        result.Add(new MacroStep { Kind = MacroStepKind.Tap, Key = s.Key });
                        i = upIndex;
                        continue;
                    }
                }
                result.Add(s.Clone());
            }
            return result;
        }

        private void Raise()
        {
            var h = Changed;
            if (h != null) h();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Kortex.Input;
using Kortex.Localization;
using Kortex.Models;

namespace Kortex.Services
{
    /// <summary>
    /// Готовые макросы: и как рабочие заготовки, и как наглядный пример
    /// всех типов шагов — удержание модификатора, пауза, нажатие, текст, повторы.
    /// </summary>
    public static class MacroPresets
    {
        public static List<Macro> Build()
        {
            return new List<Macro>
            {
                Combo("Alt + Tab", KeyCodes.VK_LMENU, false, KeyCodes.VK_TAB, false, 60, 140),
                Combo("Win + D", KeyCodes.VK_LWIN, true, 0x44, false, 30, 60),
                CopyPaste(),
                UndoTriple(),
                Signature()
            };
        }

        /// <summary>Добавляет в профиль только те заготовки, которых там ещё нет.</summary>
        public static int EnsureIn(Profile profile)
        {
            if (profile == null) return 0;
            int added = 0;
            foreach (var preset in Build())
            {
                if (profile.Macros.Any(m => string.Equals(m.Name, preset.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                profile.Macros.Add(preset);
                added++;
            }
            return added;
        }

        // ---------- Сборка отдельных заготовок ----------

        /// <summary>Модификатор удерживается, пока нажимается вторая клавиша.</summary>
        private static Macro Combo(string name, int modifier, bool modifierExtended,
                                   int key, bool keyExtended, int holdMs, int tailMs)
        {
            var macro = New(name);
            Down(macro, modifier, modifierExtended);
            Delay(macro, holdMs);
            Tap(macro, key, keyExtended);
            Delay(macro, tailMs);
            Up(macro, modifier, modifierExtended);
            return macro;
        }

        private static Macro CopyPaste()
        {
            var macro = New("Ctrl+C → Ctrl+V");
            Down(macro, KeyCodes.VK_LCONTROL, false);
            Delay(macro, 20);
            Tap(macro, 0x43, false);            // C
            Delay(macro, 20);
            Up(macro, KeyCodes.VK_LCONTROL, false);
            Delay(macro, 120);
            Down(macro, KeyCodes.VK_LCONTROL, false);
            Delay(macro, 20);
            Tap(macro, 0x56, false);            // V
            Delay(macro, 20);
            Up(macro, KeyCodes.VK_LCONTROL, false);
            return macro;
        }

        private static Macro UndoTriple()
        {
            var macro = New("Ctrl+Z ×3");
            macro.Repeat = 3;
            Down(macro, KeyCodes.VK_LCONTROL, false);
            Delay(macro, 20);
            Tap(macro, 0x5A, false);            // Z
            Delay(macro, 20);
            Up(macro, KeyCodes.VK_LCONTROL, false);
            Delay(macro, 90);
            return macro;
        }

        private static Macro Signature()
        {
            var macro = New(Loc.T("Macro.Preset.Signature"));
            macro.Steps.Add(new MacroStep
            {
                Kind = MacroStepKind.Text,
                Text = Loc.T("Macro.Preset.SignatureText")
            });
            return macro;
        }

        // ---------- Мелкие помощники ----------

        private static Macro New(string name) => new Macro { Name = name, Repeat = 1, Speed = 1.0 };

        private static void Down(Macro macro, int vk, bool extended) =>
            macro.Steps.Add(new MacroStep { Kind = MacroStepKind.Down, Key = new KeyId(vk, extended).ToString() });

        private static void Up(Macro macro, int vk, bool extended) =>
            macro.Steps.Add(new MacroStep { Kind = MacroStepKind.Up, Key = new KeyId(vk, extended).ToString() });

        private static void Tap(Macro macro, int vk, bool extended) =>
            macro.Steps.Add(new MacroStep { Kind = MacroStepKind.Tap, Key = new KeyId(vk, extended).ToString() });

        private static void Delay(Macro macro, int ms) =>
            macro.Steps.Add(new MacroStep { Kind = MacroStepKind.Delay, DelayMs = ms });
    }
}

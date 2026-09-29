using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kortex.Input;
using Kortex.Localization;
using Kortex.Models;
using Kortex.Services;
using Kortex.ViewModels;

namespace Kortex
{
    /// <summary>
    /// Сборка материалов для портфолио: снимки экранов на английском и составные рендеры.
    /// Запускается переменной окружения KORTEX_PORTFOLIO=1.
    /// </summary>
    internal static class Portfolio
    {
        private static string _dir;
        private static readonly Dictionary<string, BitmapSource> Shots = new Dictionary<string, BitmapSource>();

        private static readonly Color Base = Color.FromRgb(0x0B, 0x0D, 0x12);
        private static readonly Color Surface = Color.FromRgb(0x12, 0x15, 0x1D);
        private static readonly Color Violet = Color.FromRgb(0x7C, 0x6B, 0xF7);
        private static readonly Color Cyan = Color.FromRgb(0x35, 0xD6, 0xE8);
        private static readonly Color Text = Color.FromRgb(0xEA, 0xEE, 0xF7);
        private static readonly Color Dim = Color.FromRgb(0x9A, 0xA4, 0xB8);

        public static void Run(Window window, ShellVm vm)
        {
            _dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "portfolio");
            _dir = Path.GetFullPath(_dir);
            Directory.CreateDirectory(_dir);

            window.WindowState = WindowState.Normal;
            window.Width = 1440;
            window.Height = 900;
            vm.SetLanguageCommand.Execute("en");

            int step = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (s, e) =>
            {
                try
                {
                    switch (step)
                    {
                        case 0:
                            vm.Page = Pages.Device;
                            vm.Device.DemoCommand.Execute(null);
                            break;

                        case 1: SeedContent(vm); break;

                        case 2: vm.Page = Pages.Welcome; break;
                        case 3: Shot(window, "01-overview"); break;

                        case 4:
                            vm.Page = Pages.Tester;
                            vm.Heatmap = true;
                            SimulateTyping(vm);
                            break;
                        case 5: Shot(window, "02-key-tester"); break;

                        case 6:
                            vm.Page = Pages.Keymap;
                            break;
                        case 7:
                            // Нулевой слой нагляднее: на каждой клавише реальное значение из прошивки
                            vm.Keymap.CurrentLayer = 0;
                            vm.Keymap.Selected = FindCap(vm.Keymap.Board, "Caps Lock");
                            vm.Keymap.Search = "media";
                            break;
                        case 8: Shot(window, "03-board-keymap"); break;

                        case 9:
                            vm.Page = Pages.Remap;
                            break;
                        case 10: Shot(window, "04-remapping"); break;

                        case 11:
                            vm.Page = Pages.Macro;
                            vm.Macros.Selected = vm.Macros.Macros.FirstOrDefault();
                            vm.Macros.NewStepKind = vm.Macros.StepKinds[5];
                            vm.Macros.NewText = "chrome.exe";
                            vm.Macros.NewArg = "https://github.com";
                            break;
                        case 12: Shot(window, "05-macros"); break;

                        case 13: vm.Page = Pages.Device; break;
                        case 14: Shot(window, "06-device"); break;

                        case 15: vm.Page = Pages.Settings; break;
                        case 16: Shot(window, "07-profiles"); break;

                        case 17:
                            RenderLogo(512, "logo");
                            RenderPalette();
                            RenderHero();
                            RenderFeatureSheet();
                            WriteReadme();
                            break;

                        default:
                            AppServices.Devices.Disconnect();
                            timer.Stop();
                            window.Close();
                            return;
                    }
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(_dir, "error.txt"), ex.ToString());
                    timer.Stop();
                    window.Close();
                }
                step++;
            };
            timer.Start();
        }

        // ---------------- Наполнение демонстрационными данными ----------------

        private static void SeedContent(ShellVm vm)
        {
            var profile = AppServices.State.Active;
            profile.Remaps.Clear();

            var macro = profile.Macros.FirstOrDefault();
            profile.Remaps.Add(new RemapRule
            {
                From = new KeyId(KeyCodes.VK_CAPITAL).ToString(),
                Kind = RemapTargetKind.Key,
                To = new KeyId(KeyCodes.VK_ESCAPE).ToString()
            });
            profile.Remaps.Add(new RemapRule
            {
                From = new KeyId(0x74).ToString(),          // F5
                Kind = RemapTargetKind.Text,
                Text = "git status"
            });
            if (macro != null)
                profile.Remaps.Add(new RemapRule
                {
                    From = new KeyId(KeyCodes.VK_SCROLL).ToString(),
                    Kind = RemapTargetKind.Macro,
                    MacroId = macro.Id
                });
            profile.Remaps.Add(new RemapRule
            {
                From = new KeyId(KeyCodes.VK_PAUSE).ToString(),
                Kind = RemapTargetKind.Disabled
            });

            AppServices.ReloadProfile();
            AppServices.Remap.Enabled = true;
            vm.Remap.SyncEnabledFromState();
            vm.Remap.Refresh();
            vm.Remap.SelectedCap = FindCap(vm.Remap.Board, "Caps Lock");

            // Расширенные шаги в первом макросе — показать автоматизацию
            var first = vm.Macros.Macros.FirstOrDefault();
            if (first != null)
            {
                first.Steps.Add(new MacroStep { Kind = MacroStepKind.Launch, Text = "chrome.exe", Arg = "https://github.com" });
                first.Steps.Add(new MacroStep { Kind = MacroStepKind.MouseClick, Amount = 0 });
                first.Steps.Add(new MacroStep { Kind = MacroStepKind.Stamp, Text = "dd.MM.yyyy HH:mm" });
                first.Steps.Add(new MacroStep { Kind = MacroStepKind.Clipboard, Text = "build ready" });
            }
        }

        private static void SimulateTyping(ShellVm vm)
        {
            var weights = new Dictionary<int, int>
            {
                { 0x20, 9 }, { 0x45, 7 }, { 0x54, 6 }, { 0x41, 6 }, { 0x4F, 5 }, { 0x49, 5 },
                { 0x4E, 4 }, { 0x53, 4 }, { 0x52, 4 }, { 0x48, 3 }, { 0x4C, 3 }, { 0x44, 3 },
                { 0x43, 2 }, { 0x55, 2 }, { 0x4D, 2 }, { 0x57, 2 }, { 0x46, 2 }, { 0x47, 1 },
                { 0x59, 1 }, { 0x50, 1 }, { 0x42, 1 }, { 0x56, 1 }, { 0x4B, 1 }, { 0x51, 1 },
                { 0x08, 3 }, { 0x0D, 3 }, { 0x09, 2 }, { 0x1B, 1 },
                { 0x70, 1 }, { 0x71, 1 }, { 0x72, 1 }
            };

            foreach (var pair in weights)
                for (int i = 0; i < pair.Value; i++)
                {
                    Press(vm, pair.Key, true);
                    Press(vm, pair.Key, false);
                }

            // Пара зажатых клавиш для живой картинки
            Press(vm, KeyCodes.VK_LCONTROL, true);
            Press(vm, KeyCodes.VK_LSHIFT, true);
            Press(vm, 0x50, true);
        }

        private static double _clock;

        private static void Press(ShellVm vm, int vk, bool down)
        {
            _clock += down ? 115 : 48;      // правдоподобные интервалы: порог дребезга не срабатывает
            vm.Tester.Handle(new HookKeyEventArgs
            {
                Key = new KeyId(vk),
                Vk = vk,
                ScanCode = vk,
                IsDown = down,
                TimestampMs = _clock
            });
        }

        private static KeyCapVm FindCap(KeyboardVm board, string label)
        {
            foreach (var k in board.Keys)
                if (k.IsActive && k.Def.Label == label) return k;
            return null;
        }

        // ---------------- Снимки окна ----------------

        private static void Shot(Window window, string name)
        {
            window.UpdateLayout();
            int w = (int)window.ActualWidth, h = (int)window.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var rtb = new RenderTargetBitmap(w * 2, h * 2, 192, 192, PixelFormats.Pbgra32);
            rtb.Render(window);
            rtb.Freeze();
            Shots[name] = rtb;
            Save(rtb, name);
        }

        private static void Save(BitmapSource bitmap, string name)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var fs = File.Create(Path.Combine(_dir, name + ".png"))) encoder.Save(fs);
        }

        private static void RenderElement(FrameworkElement element, int width, int height, string name)
        {
            element.Width = width;
            element.Height = height;
            element.Measure(new Size(width, height));
            element.Arrange(new Rect(0, 0, width, height));
            element.UpdateLayout();

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(element);
            rtb.Freeze();
            Save(rtb, name);
        }

        // ---------------- Рендеры ----------------

        private static Brush BrandBrush => new LinearGradientBrush(Violet, Cyan, 45);

        private static Border LogoBadge(double size)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M4,3 H7.6 V10.2 L14.6,3 H19.4 L11.2,11.4 L19.8,21 H15.1 L7.6,12.6 V21 H4 Z"),
                Fill = Brushes.White,
                Stretch = Stretch.Uniform,
                Width = size * 0.48,
                Height = size * 0.48,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size * 0.26),
                Background = BrandBrush,
                Child = path
            };
        }

        private static void RenderLogo(int size, string name)
        {
            var host = new Grid { Background = Brushes.Transparent };
            host.Children.Add(LogoBadge(size * 0.86));
            RenderElement(host, size, size, name);
        }

        private static void RenderPalette()
        {
            var colors = new[]
            {
                new object[] { "#7C6BF7", "Accent" },
                new object[] { "#35D6E8", "Hardware" },
                new object[] { "#3DDC97", "Healthy" },
                new object[] { "#F5B24A", "Warning" },
                new object[] { "#FF6B6B", "Fault" },
                new object[] { "#1D2230", "Elevated" },
                new object[] { "#12151D", "Surface" },
                new object[] { "#0B0D12", "Base" }
            };

            var row = new UniformGrid { Columns = colors.Length, Margin = new Thickness(60) };
            foreach (var entry in colors)
            {
                var color = (Color)ColorConverter.ConvertFromString((string)entry[0]);
                var stack = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
                stack.Children.Add(new Border
                {
                    Height = 200,
                    CornerRadius = new CornerRadius(18),
                    Background = new SolidColorBrush(color),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x2C, 0x3B)),
                    BorderThickness = new Thickness(1)
                });
                stack.Children.Add(Label((string)entry[1], 17, Text, FontWeights.SemiBold, new Thickness(0, 16, 0, 0)));
                stack.Children.Add(Label((string)entry[0], 15, Dim, FontWeights.Normal, new Thickness(0, 4, 0, 0)));
                row.Children.Add(stack);
            }

            var host = new Grid { Background = new SolidColorBrush(Base) };
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(Label("Kortex colour system", 34, Text, FontWeights.Bold,
                new Thickness(70, 0, 0, 0)));
            content.Children.Add(Label("One accent for actions, one for hardware data, three for state",
                18, Dim, FontWeights.Normal, new Thickness(70, 10, 0, 0)));
            content.Children.Add(row);
            host.Children.Add(content);

            RenderElement(host, 1600, 560, "09-palette");
        }

        private static TextBlock Label(string text, double size, Color color, FontWeight weight, Thickness margin)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontSize = size,
                FontWeight = weight,
                Foreground = new SolidColorBrush(color),
                Margin = margin,
                TextWrapping = TextWrapping.Wrap
            };
        }

        private static void RenderHero()
        {
            BitmapSource shot;
            if (!Shots.TryGetValue("02-key-tester", out shot)) return;

            var background = new Grid();
            background.Background = new LinearGradientBrush(
                Color.FromRgb(0x0B, 0x0D, 0x12), Color.FromRgb(0x16, 0x19, 0x26), 90);

            // Фирменное свечение
            background.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 1500,
                Height = 1500,
                Margin = new Thickness(0, -700, -500, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Fill = new RadialGradientBrush(Color.FromArgb(0x55, 0x7C, 0x6B, 0xF7), Color.FromArgb(0, 0x35, 0xD6, 0xE8))
            });

            var column = new StackPanel { Margin = new Thickness(110, 90, 110, 0) };

            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(LogoBadge(84));
            var title = new StackPanel { Margin = new Thickness(28, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(Label("Kortex", 54, Text, FontWeights.Bold, new Thickness(0)));
            title.Children.Add(Label("Mechanical keyboard studio for QMK/VIA boards", 22, Dim,
                FontWeights.Normal, new Thickness(0, 4, 0, 0)));
            header.Children.Add(title);
            column.Children.Add(header);

            column.Children.Add(Label(
                "Detects the board over USB · edits firmware layers over VIA · tests every switch for chatter",
                24, Color.FromRgb(0x9E, 0x8D, 0xFF), FontWeights.SemiBold, new Thickness(0, 46, 0, 0)));

            var frame = new Border
            {
                CornerRadius = new CornerRadius(20),
                Margin = new Thickness(0, 54, 0, 0),
                ClipToBounds = true,
                Background = new SolidColorBrush(Surface),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x2C, 0x3B)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 70,
                    ShadowDepth = 26,
                    Direction = 270,
                    Opacity = 0.6,
                    Color = Colors.Black
                },
                Child = new Image { Source = shot, Stretch = Stretch.Uniform }
            };
            column.Children.Add(frame);

            background.Children.Add(column);
            RenderElement(background, 2000, 1520, "08-hero");
        }

        private static void RenderFeatureSheet()
        {
            var features = new[]
            {
                new[] { "", "Automatic board detection",
                        "Enumerates HID interfaces, reads the model from the device itself and checks VIA support." },
                new[] { "", "Firmware keymap editing",
                        "Layers, keycodes and dynamic macros are written straight into board memory over VIA." },
                new[] { "", "Dual engine",
                        "No QMK on the board? A Windows-level layer takes over with the same configuration flow." },
                new[] { "", "Switch health",
                        "Contact chatter detection, NKRO measurement, per-key counters and a wear heatmap." },
                new[] { "", "Macro automation",
                        "Keys, delays and text plus launching apps, mouse control, clipboard and timestamps." },
                new[] { "", "Profiles and localisation",
                        "Independent profiles per board build, VIA-format layout files, Russian and English." }
            };

            var grid = new UniformGrid { Columns = 3, Rows = 2, Margin = new Thickness(90, 40, 90, 90) };
            foreach (var feature in features)
            {
                var card = new Border
                {
                    Margin = new Thickness(14),
                    Padding = new Thickness(34, 30, 34, 30),
                    CornerRadius = new CornerRadius(20),
                    Background = new SolidColorBrush(Surface),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x2C, 0x3B)),
                    BorderThickness = new Thickness(1)
                };
                var stack = new StackPanel();
                stack.Children.Add(new Border
                {
                    Width = 62,
                    Height = 62,
                    CornerRadius = new CornerRadius(18),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new LinearGradientBrush(
                        Color.FromArgb(0x44, 0x7C, 0x6B, 0xF7), Color.FromArgb(0x44, 0x35, 0xD6, 0xE8), 45),
                    Child = new TextBlock
                    {
                        Text = feature[0],
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 26,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x8D, 0xFF)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
                stack.Children.Add(Label(feature[1], 26, Text, FontWeights.SemiBold, new Thickness(0, 24, 0, 0)));
                stack.Children.Add(Label(feature[2], 19, Dim, FontWeights.Normal, new Thickness(0, 12, 0, 0)));
                card.Child = stack;
                grid.Children.Add(card);
            }

            var host = new Grid { Background = new SolidColorBrush(Base) };
            var column = new StackPanel();
            column.Children.Add(Label("What Kortex does", 44, Text, FontWeights.Bold, new Thickness(104, 80, 0, 0)));
            column.Children.Add(grid);
            host.Children.Add(column);

            RenderElement(host, 2200, 800, "10-features");
        }

        private static void WriteReadme()
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine("# Kortex — portfolio assets");
            text.AppendLine();
            text.AppendLine("Mechanical keyboard studio for QMK/VIA boards. Windows desktop app,");
            text.AppendLine("WPF on .NET 9, no third-party dependencies.");
            text.AppendLine();
            text.AppendLine("| File | What it shows |");
            text.AppendLine("|---|---|");
            text.AppendLine("| `01-overview.png` | Welcome screen: positioning, feature cards, quick start |");
            text.AppendLine("| `02-key-tester.png` | Switch tester with wear heatmap, chatter diagnostics and event log |");
            text.AppendLine("| `03-board-keymap.png` | Firmware keymap editor: layers, matrix mapping, QMK keycode picker |");
            text.AppendLine("| `04-remapping.png` | Windows-level remapping for boards without QMK |");
            text.AppendLine("| `05-macros.png` | Macro editor with automation steps and firmware write-back |");
            text.AppendLine("| `06-device.png` | Detected boards, capabilities, RGB lighting and service commands |");
            text.AppendLine("| `07-profiles.png` | Profiles, board build variants, tester settings |");
            text.AppendLine("| `08-hero.png` | Title render for the portfolio header |");
            text.AppendLine("| `09-palette.png` | Colour system |");
            text.AppendLine("| `10-features.png` | Feature sheet |");
            text.AppendLine("| `logo.png` | Brand mark, 512 px, transparent background |");
            text.AppendLine();
            text.AppendLine("Screenshots are captured at 2x pixel density from the running application.");
            text.AppendLine("The connected board in the shots is the built-in demo device, so the data is real");
            text.AppendLine("application output rather than a mock-up.");
            File.WriteAllText(Path.Combine(_dir, "README.md"), text.ToString(), System.Text.Encoding.UTF8);
        }
    }
}
